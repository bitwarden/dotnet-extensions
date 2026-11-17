using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.ContextHandlerHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A call made while handling an event, held to the rule that the event's actor is for the record only.
/// </summary>
public sealed class EventActorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Guid Publisher = Guid.NewGuid();

    // A consumer of an event a person's request published: the context names them, and a call with no statement would
    // carry them as the person acting, on this service's word. Refused with OutgoingCall:RefuseEventActor.
    [Fact]
    public async Task Send_HandlingAnEventNamingAPerson_NoStatement_RefusedWhereRefusing()
    {
        var context = new StatedContext { UserId = Publisher, OrganizationId = Guid.NewGuid() };

        using (ContextScope.BeginForEvent(context))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SendHandlingAnEvent(context, refuse: true));

            Assert.Contains("for the record only", refused.Message, StringComparison.Ordinal);
            Assert.Contains("OutgoingCall.AsSystem", refused.Message, StringComparison.Ordinal);
        }
    }

    // Off, as in production: logged, and sent as before, so no behaviour changes where nobody turned it on.
    [Fact]
    public async Task Send_HandlingAnEventNamingAPerson_NoStatement_SentWhereNotRefusing()
    {
        var context = new StatedContext { UserId = Publisher, OrganizationId = Guid.NewGuid() };
        HttpRequestMessage sent;

        using (ContextScope.BeginForEvent(context))
        {
            sent = await SendHandlingAnEvent(context, refuse: false);
        }

        Assert.Equal(Publisher.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // The defect the sweep found: AboutOrganization replaces the organization only, so the event's person goes along.
    // Held wrong even when the event names nobody, so a test publishing an empty context catches it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_HandlingAnEvent_AboutOrganization_Refused(bool eventNamesAPerson)
    {
        var context = new StatedContext { UserId = eventNamesAPerson ? Publisher : null };

        using (ContextScope.BeginForEvent(context))
        using (OutgoingCall.AboutOrganization(Guid.NewGuid()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SendHandlingAnEvent(context, refuse: true));
        }
    }

    // The service's own call, or one naming its person outright, is what a consumer makes.
    [Fact]
    public async Task Send_HandlingAnEventNamingAPerson_AsSystemOrNamingThePerson_Sent()
    {
        var context = new StatedContext { UserId = Publisher };
        var organization = Guid.NewGuid();
        var named = Guid.NewGuid();
        HttpRequestMessage system;
        HttpRequestMessage about;

        using (ContextScope.BeginForEvent(context))
        {
            using (OutgoingCall.AsSystem(organization, recordedAs: Publisher))
            {
                system = await SendHandlingAnEvent(context, refuse: true);
            }

            using (OutgoingCall.AboutUser(named))
            {
                about = await SendHandlingAnEvent(context, refuse: true);
            }
        }

        Assert.False(system.Headers.Contains(BitwardenHeaders.User));
        Assert.Equal(Publisher, Carried(system).RecordedUser);
        Assert.Equal(named.ToString(), about.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // An event naming nobody carries nobody: a plain call is the service's own already.
    [Fact]
    public async Task Send_HandlingAnEventNamingNobody_NoStatement_Sent()
    {
        var context = new StatedContext { OrganizationId = Guid.NewGuid() };
        HttpRequestMessage sent;

        using (ContextScope.BeginForEvent(context))
        {
            sent = await SendHandlingAnEvent(context, refuse: true);
        }

        Assert.False(sent.Headers.Contains(BitwardenHeaders.User));
    }

    // A job's own scope, or one a handler begins inside the event's to name somebody on purpose, is not the event's.
    [Fact]
    public async Task Send_InAScopeNotAnEvents_NoStatement_Sent()
    {
        var context = new StatedContext { UserId = Guid.NewGuid() };
        HttpRequestMessage sent;

        using (ContextScope.BeginForEvent(new StatedContext { UserId = Publisher }))
        using (ContextScope.Begin(context))
        {
            sent = await SendHandlingAnEvent(context, refuse: true);
        }

        Assert.Equal(context.UserId.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // The options bind from OutgoingCall:RefuseEventActor, off unless set.
    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void AddBitwardenClient_BindsRefuseEventActor(string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OutgoingCall:RefuseEventActor"] = setting })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration);
        services.AddBitwardenClient<IDisposable, Recorder>("some-service", ["some.scope"]);
        services.AddBitwardenClient<IAsyncDisposable, AsyncRecorder>("other-service", ["other.scope"]);

        var options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<OutgoingCallOptions>>().Value;

        Assert.Equal(expected, options.RefuseEventActor);
    }
}
