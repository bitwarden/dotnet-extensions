using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.ContextHandlerHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// What a call carries onward of the context it was made in.
/// </summary>
public sealed class ContextHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The device an action came from reaches the service that pushes about it, which is the one
    // that has to leave it out.
    [Fact]
    public async Task Send_WithADevice_CarriesItsTypeAndIdentifier()
    {
        var sent = await Send(new StatedContext { DeviceType = "ChromeBrowser", DeviceIdentifier = "device-1" });

        var carried = Carried(sent);
        Assert.Equal("ChromeBrowser", carried.DeviceType);
        Assert.Equal("device-1", carried.DeviceIdentifier);
    }

    [Fact]
    public async Task Send_WithNothingToCarry_SendsTheVersionAlone()
    {
        var sent = await Send(new StatedContext { OrganizationId = Guid.NewGuid() });

        Assert.Equal("""{"v":1}""", sent.Headers.GetValues(BitwardenHeaders.Context).Single());
    }

    // One closed, versioned object, written here and nowhere else.
    [Fact]
    public async Task Send_WithEveryMember_WritesVersionOneInOrder()
    {
        var sent = await Send(new StatedContext
        {
            ClientIpAddress = "203.0.113.9",
            DeviceType = "10",
            DeviceIdentifier = "device-1",
            Credential = CallerCredential.User,
        });

        Assert.Equal(
            """{"v":1,"clientIp":"203.0.113.9","deviceType":"10","deviceIdentifier":"device-1","credential":"USER"}""",
            sent.Headers.GetValues(BitwardenHeaders.Context).Single());
    }

    // A value the callee would refuse costs that member, never the call.
    [Fact]
    public async Task Send_WithAValueTheCalleeWouldRefuse_LeavesItOut()
    {
        var sent = await Send(new StatedContext { ClientIpAddress = "not an address", DeviceType = "a device; with prose", DeviceIdentifier = "device-1" });

        var carried = Carried(sent);
        Assert.Null(carried.ClientIp);
        Assert.Null(carried.DeviceType);
        Assert.Equal("device-1", carried.DeviceIdentifier);
    }

    // The four headers are this handler's: whatever a call site put there itself goes.
    [Fact]
    public async Task Send_WithHeadersTheCallSiteSet_ReplacesThemWithTheContexts()
    {
        var acting = Guid.NewGuid();
        var recorder = new Recorder();
        var services = new ServiceCollection().AddSingleton<IContext>(new StatedContext { UserId = acting }).BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");
        request.Headers.Add(BitwardenHeaders.User, Guid.NewGuid().ToString());
        request.Headers.Add(BitwardenHeaders.Organization, Guid.NewGuid().ToString());
        request.Headers.Add(BitwardenHeaders.Provider, Guid.NewGuid().ToString());
        request.Headers.Add(BitwardenHeaders.Context, """{"v":1,"credential":"SYSTEM"}""");

        using var response = await invoker.SendAsync(request, Token);

        Assert.Equal(acting.ToString(), recorder.Sent!.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.False(recorder.Sent.Headers.Contains(BitwardenHeaders.Organization));
        Assert.False(recorder.Sent.Headers.Contains(BitwardenHeaders.Provider));
        Assert.Equal("""{"v":1}""", recorder.Sent.Headers.GetValues(BitwardenHeaders.Context).Single());
    }

    // Bitwarden-Provider only when the context has one.
    [Fact]
    public async Task Send_WithNoProvider_SendsNoProviderHeader()
    {
        var sent = await Send(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid() });

        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
    }

    // The person's organizations reach the domain services evaluating feature flags for their request.
    [Fact]
    public async Task Send_AsThePerson_CarriesTheirOrganizations()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var sent = await Send(new StatedContext
        {
            UserId = Guid.NewGuid(),
            Credential = CallerCredential.User,
            UserOrganizationIds = [first, second, first, Guid.Empty],
        });

        Assert.Equal([first, second], Carried(sent).Organizations);
    }

    // The party flags are evaluated for, and the client's version, say where the request came from: carried on every
    // call, the system's own and one about an organization included.
    [Fact]
    public async Task Send_TheFlagSubjectAndVersion_OnEveryCall()
    {
        var person = Guid.NewGuid();
        var context = new StatedContext
        {
            UserId = person,
            Credential = CallerCredential.User,
            FlagSubjectKey = person,
            ClientVersion = "2026.10.1",
            ClientVersionIsPrerelease = true,
        };

        var asThePerson = await Send(context);
        HttpRequestMessage aboutAnOrganization, asSystem;
        using (OutgoingCall.AboutOrganization(Guid.NewGuid()))
        {
            aboutAnOrganization = await Send(context);
        }

        using (OutgoingCall.AsSystemAbout(person))
        {
            asSystem = await Send(context);
        }

        foreach (var sent in (HttpRequestMessage[])[asThePerson, aboutAnOrganization, asSystem])
        {
            var carried = Carried(sent);
            Assert.Equal(FlagSubjectKind.User, carried.FlagKind);
            Assert.Equal(person, carried.FlagKey);
            Assert.Equal("2026.10.1", carried.ClientVersion);
            Assert.True(carried.ClientVersionIsPrerelease);
        }
    }

    // A party that is no person: an access connector, its organization carried with it.
    [Fact]
    public async Task Send_AnAccessConnector_CarriesItAndItsOrganization()
    {
        var connector = Guid.NewGuid();
        var organization = Guid.NewGuid();

        var carried = Carried(await Send(new StatedContext
        {
            OrganizationId = organization,
            Credential = CallerCredential.Organization,
            FlagSubjectKind = FlagSubjectKind.AccessConnector,
            FlagSubjectKey = connector,
            UserOrganizationIds = [organization],
        }));

        Assert.Equal(FlagSubjectKind.AccessConnector, carried.FlagKind);
        Assert.Equal(connector, carried.FlagKey);
        Assert.Equal([organization], carried.Organizations);
    }

    // A value the callee would refuse is left out rather than sent.
    [Fact]
    public async Task Send_AVersionOfAnotherShape_LeftOutWithItsPrerelease()
    {
        var carried = Carried(await Send(new StatedContext { ClientVersion = "2026.10.0-beta", ClientVersionIsPrerelease = true }));

        Assert.Null(carried.ClientVersion);
        Assert.False(carried.ClientVersionIsPrerelease);
    }

    // They are the flag subject's, carried on a call that keeps the actor: never about somebody else, nor as the
    // system. A flag subject that is no person, such as an organization's own client, has them too.
    [Fact]
    public async Task Send_NotAsThePerson_CarriesNoOrganizations()
    {
        var organizations = new[] { Guid.NewGuid() };
        var person = Guid.NewGuid();

        var nobody = await Send(new StatedContext { UserOrganizationIds = organizations });

        var context = new StatedContext { UserId = person, UserOrganizationIds = organizations };
        HttpRequestMessage aboutSomebodyElse, asSystem, aboutThemselves;
        using (OutgoingCall.AboutUser(Guid.NewGuid()))
        {
            aboutSomebodyElse = await Send(context);
        }

        using (OutgoingCall.AsSystemAbout(person))
        {
            asSystem = await Send(context);
        }

        using (OutgoingCall.AboutUser(person))
        {
            aboutThemselves = await Send(context);
        }

        Assert.Equal(organizations, Carried(nobody).Organizations);
        Assert.Null(Carried(aboutSomebodyElse).Organizations);
        Assert.Null(Carried(asSystem).Organizations);
        Assert.Equal(organizations, Carried(aboutThemselves).Organizations);
    }

    // A person in more organizations than the callee accepts is carried without them, never a part of them.
    [Fact]
    public async Task Send_MoreOrganizationsThanTheCalleeAccepts_LeavesThemOut()
    {
        var sent = await Send(new StatedContext
        {
            UserId = Guid.NewGuid(),
            UserOrganizationIds = [.. Enumerable.Range(0, ContextHeader.MaximumOrganizations + 1).Select(_ => Guid.NewGuid())],
        });

        Assert.Null(Carried(sent).Organizations);
    }

    [Fact]
    public async Task Send_WithACredential_CarriesIt()
    {
        var sent = await Send(new StatedContext { Credential = CallerCredential.Organization });

        Assert.Equal(CallerCredential.Organization, Carried(sent).Credential);
    }

    // recordedUser travels with SYSTEM alone: a person's own request is recorded as the person.
    [Fact]
    public async Task Send_ARecordedPersonOnACallThatIsNotTheSystems_IsNotCarried()
    {
        var sent = await Send(new StatedContext { Credential = CallerCredential.User, RecordedUserId = Guid.NewGuid() });

        Assert.Null(Carried(sent).RecordedUser);
    }

    // A domain service handling a system call carries it on as one, with its person.
    [Fact]
    public async Task Send_InASystemCallsContext_CarriesTheCredentialAndThePerson()
    {
        var person = Guid.NewGuid();

        var sent = await Send(new StatedContext { Credential = CallerCredential.System, RecordedUserId = person });

        Assert.Equal(CallerCredential.System, Carried(sent).Credential);
        Assert.Equal(person, Carried(sent).RecordedUser);
    }

    // A provider's staff member in a client: the provider is carried for the record, on a person's call and on the
    // system's own call made in their request alike, and the call still names no provider.
    [Theory]
    [InlineData(CallerCredential.User)]
    [InlineData(CallerCredential.System)]
    public async Task Send_WithARecordedProvider_CarriesIt_AndNamesNoProvider(CallerCredential credential)
    {
        var provider = Guid.NewGuid();

        var sent = await Send(new StatedContext { Credential = credential, RecordedProviderId = provider });

        Assert.Equal(provider, Carried(sent).RecordedProvider);
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
    }

    // A product service's operation naming an organization in its path or body, where the session
    // names none: its calls carry that one.
    [Fact]
    public async Task Send_WithAnOrganizationTheRequestNames_CarriesItWhereTheSessionNamesNone()
    {
        var named = Guid.NewGuid();
        var person = Guid.NewGuid();
        var recorder = new Recorder();
        var services = new ServiceCollection()
            .AddSingleton<IContext>(new StatedContext { UserId = person })
            .AddSingleton<IServiceIdentity>(new Identity(isProductService: true))
            .AddSingleton<IRequestNamedOwner>(new Named(named, null))
            .BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");

        using var response = await invoker.SendAsync(request, Token);

        Assert.Equal(named.ToString(), recorder.Sent!.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.Equal(person.ToString(), recorder.Sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    [Fact]
    public async Task Send_WithAnOrganizationTheRequestNames_NeverReplacesTheSessions()
    {
        var session = Guid.NewGuid();
        var recorder = new Recorder();
        var services = new ServiceCollection()
            .AddSingleton<IContext>(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = session })
            .AddSingleton<IRequestNamedOwner>(new Named(Guid.NewGuid(), Guid.NewGuid()))
            .BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");

        using var response = await invoker.SendAsync(request, Token);

        Assert.Equal(session.ToString(), recorder.Sent!.Headers.GetValues(BitwardenHeaders.Organization).Single());
    }

    // The four headers, signed: the context token is asked for what they carry, and travels beside them.
    [Fact]
    public async Task Send_SignsWhatTheHeadersCarry()
    {
        var person = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var signer = new Signer();

        var sent = await Signed(new StatedContext { UserId = person, OrganizationId = organization }, signer, proof: "person-token");

        var asked = Assert.Single(signer.Asked);
        Assert.Equal(organization, asked.Assertion.Organization);
        Assert.Equal(person, asked.Assertion.User);
        Assert.Null(asked.Assertion.Provider);
        Assert.Equal(sent.Headers.GetValues(BitwardenHeaders.Context).Single(), asked.Assertion.Context);
        Assert.Equal("signed-1", sent.Headers.GetValues(BitwardenHeaders.Delegation).Single());
    }

    // Carrying the request's own context, the token is exchanged from what proves whom the request is for.
    [Fact]
    public async Task Send_WithTheRequestsOwnContext_IsExchangedFromItsProof()
    {
        var signer = new Signer();

        await Signed(new StatedContext { UserId = Guid.NewGuid() }, signer, proof: "person-token");

        Assert.Equal("person-token", Assert.Single(signer.Asked).Subject);
    }

    // A statement naming somebody else, or making the call the system's, is the service's word alone: its own token.
    [Fact]
    public async Task Send_AboutSomebodyElse_IsTheServicesOwnWord()
    {
        var signer = new Signer();

        using (OutgoingCall.AboutUser(Guid.NewGuid()))
        {
            await Signed(new StatedContext { UserId = Guid.NewGuid() }, signer, proof: "person-token");
        }

        using (OutgoingCall.AsSystem(Guid.NewGuid()))
        {
            await Signed(new StatedContext { UserId = Guid.NewGuid() }, signer, proof: "person-token");
        }

        Assert.All(signer.Asked, asked => Assert.Null(asked.Subject));
    }

    // A statement keeping the request's own person stays chained to their proof: naming them again, or an organization
    // where the request names none, or the same one.
    [Fact]
    public async Task Send_AStatementKeepingTheRequestsPerson_IsExchangedFromItsProof()
    {
        var person = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var signer = new Signer();

        using (OutgoingCall.AboutUser(person))
        {
            await Signed(new StatedContext { UserId = person }, signer, proof: "person-token");
        }

        using (OutgoingCall.AboutOrganization(organization))
        {
            await Signed(new StatedContext { UserId = person }, signer, proof: "person-token");
        }

        using (OutgoingCall.AboutOrganization(organization))
        {
            await Signed(new StatedContext { UserId = person, OrganizationId = organization }, signer, proof: "person-token");
        }

        Assert.Equal(3, signer.Asked.Count);
        Assert.All(signer.Asked, asked => Assert.Equal("person-token", asked.Subject));
    }

    // An organization other than the one the request names is the service's own word.
    [Fact]
    public async Task Send_AboutAnotherOrganizationThanTheRequests_IsTheServicesOwnWord()
    {
        var signer = new Signer();

        using (OutgoingCall.AboutOrganization(Guid.NewGuid()))
        {
            await Signed(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid() }, signer, proof: "person-token");
        }

        Assert.Null(Assert.Single(signer.Asked).Subject);
    }

    // A message consumer's, a job's or a host's own call, with no request to prove anything: the service's own word.
    [Fact]
    public async Task Send_WithNoRequest_IsTheServicesOwnWord()
    {
        var signer = new Signer();

        await Signed(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid() }, signer, proof: null);

        var asked = Assert.Single(signer.Asked);
        Assert.Null(asked.Subject);
        Assert.NotNull(asked.Assertion.User);
    }

    // A context token a call site set itself is removed with the other four.
    [Fact]
    public async Task Send_WithADelegationTheCallSiteSet_ReplacesIt()
    {
        var recorder = new Recorder();
        var services = new ServiceCollection().AddSingleton<IContext>(new StatedContext()).BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer(issues: false)) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");
        request.Headers.Add(BitwardenHeaders.Delegation, "forged");

        using var response = await invoker.SendAsync(request, Token);

        Assert.False(recorder.Sent!.Headers.Contains(BitwardenHeaders.Delegation));
    }
}
