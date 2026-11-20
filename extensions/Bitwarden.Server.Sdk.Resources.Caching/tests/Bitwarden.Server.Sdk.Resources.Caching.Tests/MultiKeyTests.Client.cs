using System.Net;
using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// The client's half: it fills the published template from its call, and answers only a call the operation would
/// admit. Not compiled into the generated tests, which can't see the client's internals.
/// </summary>
public sealed partial class MultiKeyTests
{
    // A client of the cache fills the template from its call and reads what the service filled.
    [Fact]
    public async Task Client_ReadCurrent_ReadsTheEntryTheServiceFilled()
    {
        await using var host = await StartAsync();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        var (connection, reads) = await ReadsAsync(host, "other-service", delegated: true);
        await using var _ = connection;

        var hit = await reads.ReadCurrentAsync("getCurrentLoan", ["returned"], Ct);
        Assert.Equal(Client.CachedAnswerKind.Hit, hit.Kind);
        Assert.Contains(_loan.ToString(), hit.Resource);

        // Read as another call: another borrower's key, which holds nothing.
        var other = await reads.ReadAsync("getCurrentLoan", null, ["returned"], new Client.OutgoingCallContext(_organization, Guid.NewGuid(), null, Asserted: false), Ct);
        Assert.Equal(Client.CachedAnswerKind.Miss, other.Kind);

        // A call naming no person can't fill the template.
        var nobody = await reads.ReadAsync("getCurrentLoan", null, ["returned"], new Client.OutgoingCallContext(_organization, null, null, Asserted: false), Ct);
        Assert.Equal(Client.CachedAnswerKind.Miss, nobody.Kind);
    }

    // A context this service asserts naming a person, with no request behind the call (a job, a message handler): the
    // operation admits it from the services it names, and the cache answers those alone. Any other is asked of the
    // service, which refuses it as before.
    [Theory]
    [InlineData("some-service", true)]
    [InlineData("other-service", false)]
    public async Task Client_AssertedContext_AnsweredOnlyForTheServicesTheOperationAdmits(string caller, bool admitted)
    {
        await using var host = await StartAsync();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        var (connection, reads) = await ReadsAsync(host, caller, delegated: false);
        await using var _ = connection;

        var answer = await reads.ReadCurrentAsync("getCurrentLoan", ["returned"], Ct);
        using var asked = await host.GetAsync("/api/v1/loans/current?fields[loan]=returned", _organization, _borrower, $"internal.{caller}", asserted: true);

        Assert.Equal(admitted ? Client.CachedAnswerKind.Hit : Client.CachedAnswerKind.Miss, answer.Kind);
        Assert.Equal(admitted ? HttpStatusCode.OK : HttpStatusCode.Forbidden, asked.StatusCode);
    }

    // A person's own context, delegated from the request behind the call, is answered whatever service makes it.
    [Fact]
    public async Task Client_DelegatedContext_AnsweredForAnyService()
    {
        await using var host = await StartAsync();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        var (connection, reads) = await ReadsAsync(host, "other-service", delegated: true);
        await using var _ = connection;

        Assert.Equal(Client.CachedAnswerKind.Hit, (await reads.ReadCurrentAsync("getCurrentLoan", ["returned"], Ct)).Kind);
        Assert.False(reads.NextCall().Asserted);
    }

    // A statement making the call the service's own word asserts the context though a request is behind it.
    [Fact]
    public async Task Client_StatementNamingAnotherPerson_Asserted()
    {
        await using var host = await StartAsync();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        var (connection, reads) = await ReadsAsync(host, "other-service", delegated: true, user: Guid.NewGuid());
        await using var _ = connection;

        using (Client.OutgoingCall.AboutUser(_borrower))
        {
            Assert.True(reads.NextCall().Asserted);
            Assert.Equal(Client.CachedAnswerKind.Miss, (await reads.ReadCurrentAsync("getCurrentLoan", ["returned"], Ct)).Kind);
        }
    }

    private async Task<(Client.CachedReadsConnection Connection, Client.CachedReads<MultiKeyTests> Reads)> ReadsAsync(ShelfHost host, string caller, bool delegated, Guid? user = null)
    {
        using var response = await host.Client().GetAsync("/api/openapi.json", Ct);
        var contract = Client.CacheableRead.FromContract(await response.Content.ReadAsStreamAsync(Ct));
        var collection = new ServiceCollection()
            .AddSingleton<IContext>(new StatedContext { OrganizationId = _organization, UserId = user ?? _borrower })
            .Configure<Client.BitwardenClientOptions>(ShelfHost.ServiceName, options =>
            {
                options.Scopes = [ShelfHost.ReadScope];
                options.CallerName = caller;
            });

        // The request behind the call, whose token a delegated context is exchanged from.
        if (delegated)
        {
            collection.AddSingleton<ICallerProof>(new RequestProof());
        }

        var services = collection.BuildServiceProvider();

        var connection = new Client.CachedReadsConnection(redis.Hostname, new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

        return (connection, new Client.CachedReads<MultiKeyTests>(
            ShelfHost.ServiceName,
            contract,
            connection,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Client.BitwardenClientOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Client.CachedReads<MultiKeyTests>>.Instance));
    }

    private sealed class RequestProof : ICallerProof
    {
        public string? SubjectToken => "a-request-proof";
    }
}
