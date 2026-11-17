using Bitwarden.Server.Sdk.Context;
using static Bitwarden.Server.Sdk.Client.Tests.ContextHandlerHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// What an <see cref="OutgoingCall"/> statement changes about the call it covers, and where it is refused.
/// </summary>
public sealed class OutgoingCallTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A domain service names the person one call is about, inside a request that names someone
    // else or nobody.
    [Fact]
    public async Task Send_InsideOutgoingCall_CarriesTheStatedUserInsteadOfTheContexts()
    {
        var person = Guid.NewGuid();
        var organization = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AboutUser(person))
        {
            sent = await Send(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = organization });
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
    }

    [Fact]
    public async Task Send_InsideOutgoingCallWithNoContextUser_CarriesTheStatedUser()
    {
        var person = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AboutUser(person))
        {
            sent = await Send(new StatedContext());
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    [Fact]
    public async Task Send_AfterOutgoingCallIsDisposed_CarriesTheContextsUserAgain()
    {
        var acting = Guid.NewGuid();
        using (OutgoingCall.AboutUser(Guid.NewGuid()))
        {
            await Send(new StatedContext());
        }

        var sent = await Send(new StatedContext { UserId = acting });

        Assert.Equal(acting.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // One call, not a block of them: a second call would carry the person somewhere nobody meant.
    [Fact]
    public async Task Send_SecondCallInsideOneOutgoingCall_Throws()
    {
        using (OutgoingCall.AboutUser(Guid.NewGuid()))
        {
            await Send(new StatedContext());

            await Assert.ThrowsAsync<InvalidOperationException>(() => Send(new StatedContext()));
        }
    }

    [Fact]
    public async Task Send_NestedOutgoingCall_InnerNamesItsCallAndOuterIsRestored()
    {
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();
        HttpRequestMessage first;
        HttpRequestMessage second;

        using (OutgoingCall.AboutUser(outer))
        {
            using (OutgoingCall.AboutUser(inner))
            {
                first = await Send(new StatedContext());
            }

            second = await Send(new StatedContext());
        }

        Assert.Equal(inner.ToString(), first.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.Equal(outer.ToString(), second.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // In a product service the acting user is the session's person; nobody else may be named.
    [Fact]
    public async Task Send_InsideOutgoingCallInAProductService_ThrowsAndSendsNothing()
    {
        var recorder = new Recorder();

        using (OutgoingCall.AboutUser(Guid.NewGuid()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(new StatedContext(), new Identity(isProductService: true), recorder));
        }

        Assert.Null(recorder.Sent);
    }

    [Fact]
    public async Task Send_InsideOutgoingCallInADomainService_CarriesTheStatedUser()
    {
        var person = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AboutUser(person))
        {
            sent = await Send(new StatedContext(), new Identity(isProductService: false));
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    [Fact]
    public void About_EmptyIdentifier_Throws()
    {
        Assert.Throws<ArgumentException>(() => OutgoingCall.AboutUser(Guid.Empty));
        Assert.Throws<ArgumentException>(() => OutgoingCall.AboutOrganization(Guid.Empty));
        Assert.Throws<ArgumentException>(() => OutgoingCall.About(Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => OutgoingCall.AsSystem(Guid.Empty));
    }

    // A service listing a person's items across organizations asks about each in turn; the request
    // itself names none.
    [Fact]
    public async Task Send_InsideOutgoingCallAboutAnOrganization_CarriesItAndKeepsTheContextsUser()
    {
        var acting = Guid.NewGuid();
        var organization = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AboutOrganization(organization))
        {
            sent = await Send(new StatedContext { UserId = acting });
        }

        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.Equal(acting.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    [Fact]
    public async Task Send_InsideOutgoingCallAboutBoth_CarriesBothInsteadOfTheContexts()
    {
        var person = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var provider = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.About(person, organization))
        {
            sent = await Send(new StatedContext { UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), ProviderId = provider });
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.Equal(provider.ToString(), sent.Headers.GetValues(BitwardenHeaders.Provider).Single());
    }

    // A service removing a grantor after a takeover, inside the grantee's request: the call is the
    // service's own, so neither the grantee nor any provider goes with it.
    [Fact]
    public async Task Send_AsSystem_CarriesTheOrganizationAndNoUserOrProvider()
    {
        var organization = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystem(organization))
        {
            sent = await Send(
                new StatedContext { UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), ProviderId = Guid.NewGuid() },
                new Identity(isProductService: false));
        }

        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.User));
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
        Assert.Equal(CallerCredential.System, Carried(sent).Credential);
        Assert.Null(Carried(sent).RecordedUser);
    }

    // Where the record names a person although the call is the system's: the call names nobody
    // acting, and the record names them.
    [Fact]
    public async Task Send_AsSystemRecordedAsAPerson_CarriesThePersonOnlyForTheRecord()
    {
        var organization = Guid.NewGuid();
        var person = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystem(organization, recordedAs: person))
        {
            sent = await Send(
                new StatedContext { UserId = person, ClientIpAddress = "203.0.113.9", Credential = CallerCredential.User },
                new Identity(isProductService: false));
        }

        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.User));
        var carried = Carried(sent);
        Assert.Equal(CallerCredential.System, carried.Credential);
        Assert.Equal(person, carried.RecordedUser);
        Assert.Equal("203.0.113.9", carried.ClientIp);
    }

    // A service revoking a person wherever two-step login is required of them: nothing the request
    // named narrows the call to one organization.
    [Fact]
    public async Task Send_AsSystemAboutAPerson_CarriesThePersonAndNoOrganizationOrProvider()
    {
        var person = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystemAbout(person))
        {
            sent = await Send(
                new StatedContext { OrganizationId = Guid.NewGuid(), ProviderId = Guid.NewGuid() },
                new Identity(isProductService: false));
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Organization));
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
        Assert.Equal(CallerCredential.System, Carried(sent).Credential);
    }

    // A service asking whether an organization's single sign-on is enabled, inside an
    // administrator's invitation: the operation names the organization in its path and
    // refuses a caller naming an owner, so nothing the request names goes with the call.
    [Fact]
    public async Task Send_AsSystemAboutNobody_CarriesNoUserOrganizationOrProvider()
    {
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystem())
        {
            sent = await Send(
                new StatedContext
                {
                    UserId = Guid.NewGuid(),
                    OrganizationId = Guid.NewGuid(),
                    ProviderId = Guid.NewGuid(),
                    ClientIpAddress = "203.0.113.9",
                    Credential = CallerCredential.User,
                    RecordedUserId = Guid.NewGuid(),
                },
                new Identity(isProductService: false));
        }

        Assert.False(sent.Headers.Contains(BitwardenHeaders.User));
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Organization));
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
        var carried = Carried(sent);
        Assert.Equal(CallerCredential.System, carried.Credential);
        Assert.Null(carried.RecordedUser);
        Assert.Equal("203.0.113.9", carried.ClientIp);
    }

    // A service recording a provider's invoice lines when a payment processor's webhook arrives: the
    // call is the service's own, about the provider the invoice's subscription belongs to, and names nobody else.
    [Fact]
    public async Task Send_AsSystemForAProvider_CarriesTheProviderAndNoUserOrOrganization()
    {
        var provider = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystemForProvider(provider))
        {
            sent = await Send(
                new StatedContext
                {
                    UserId = Guid.NewGuid(),
                    OrganizationId = Guid.NewGuid(),
                    ProviderId = Guid.NewGuid(),
                    Credential = CallerCredential.User,
                    RecordedUserId = Guid.NewGuid(),
                },
                new Identity(isProductService: false));
        }

        Assert.Equal(provider.ToString(), sent.Headers.GetValues(BitwardenHeaders.Provider).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.User));
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Organization));
        var carried = Carried(sent);
        Assert.Equal(CallerCredential.System, carried.Credential);
        Assert.Null(carried.RecordedUser);
    }

    [Fact]
    public async Task Send_AsSystemForAProviderInAProductService_ThrowsAndSendsNothing()
    {
        var recorder = new Recorder();

        using (OutgoingCall.AsSystemForProvider(Guid.NewGuid()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(new StatedContext { UserId = Guid.NewGuid() }, new Identity(isProductService: true), recorder));
        }

        Assert.Null(recorder.Sent);
    }

    [Fact]
    public void AsSystemForProvider_AnEmptyProvider_Throws() =>
        Assert.Throws<ArgumentException>(() => OutgoingCall.AsSystemForProvider(Guid.Empty));

    [Fact]
    public async Task Send_AsSystemAboutNobodyInAProductService_ThrowsAndSendsNothing()
    {
        var recorder = new Recorder();

        using (OutgoingCall.AsSystem())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(new StatedContext { UserId = Guid.NewGuid() }, new Identity(isProductService: true), recorder));
        }

        Assert.Null(recorder.Sent);
    }

    // The one statement a product service may make freely: it names nobody and only takes away.
    [Fact]
    public async Task Send_WithoutProviderInAProductService_CarriesEverythingButTheProvider()
    {
        var person = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.WithoutProvider())
        {
            sent = await Send(
                new StatedContext { UserId = person, ProviderId = Guid.NewGuid(), Credential = CallerCredential.User },
                new Identity(isProductService: true));
        }

        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
        Assert.Equal(CallerCredential.User, Carried(sent).Credential);
    }

    // A service asking the acting person's membership in an organization inside a provider's session:
    // the organization stated, the person and the credential carried, the provider left behind.
    [Fact]
    public async Task Send_AboutOrganizationWithoutProvider_CarriesTheOrganizationAndPersonButNoProvider()
    {
        var person = Guid.NewGuid();
        var organization = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AboutOrganizationWithoutProvider(organization))
        {
            sent = await Send(
                new StatedContext { UserId = person, OrganizationId = Guid.NewGuid(), ProviderId = Guid.NewGuid(), Credential = CallerCredential.User },
                new Identity(isProductService: false));
        }

        Assert.Equal(organization.ToString(), sent.Headers.GetValues(BitwardenHeaders.Organization).Single());
        Assert.Equal(person.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
        Assert.False(sent.Headers.Contains(BitwardenHeaders.Provider));
        Assert.Equal(CallerCredential.User, Carried(sent).Credential);
    }

    // It names an organization, so a product service may not make it.
    [Fact]
    public async Task Send_AboutOrganizationWithoutProviderInAProductService_ThrowsAndSendsNothing()
    {
        var recorder = new Recorder();

        using (OutgoingCall.AboutOrganizationWithoutProvider(Guid.NewGuid()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(new StatedContext { UserId = Guid.NewGuid(), ProviderId = Guid.NewGuid() }, new Identity(isProductService: true), recorder));
        }

        Assert.Null(recorder.Sent);
        Assert.Throws<ArgumentException>(() => OutgoingCall.AboutOrganizationWithoutProvider(Guid.Empty));
    }

    [Fact]
    public void AsSystem_EmptyIdentifier_Throws()
    {
        Assert.Throws<ArgumentException>(() => OutgoingCall.AsSystem(Guid.NewGuid(), recordedAs: Guid.Empty));
        Assert.Throws<ArgumentException>(() => OutgoingCall.AsSystem(Guid.Empty, recordedAs: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => OutgoingCall.AsSystemAbout(Guid.Empty));
    }

    [Fact]
    public async Task Send_AsSystemInAProductService_ThrowsAndSendsNothing()
    {
        var recorder = new Recorder();

        using (OutgoingCall.AsSystem(Guid.NewGuid()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(new StatedContext { UserId = Guid.NewGuid() }, new Identity(isProductService: true), recorder));
        }

        Assert.Null(recorder.Sent);
    }

    [Fact]
    public async Task Send_AfterAsSystemIsDisposed_CarriesTheContextsUserAgain()
    {
        var acting = Guid.NewGuid();
        HttpRequestMessage sent;

        using (OutgoingCall.AsSystem(Guid.NewGuid()))
        {
        }

        sent = await Send(new StatedContext { UserId = acting });

        Assert.Equal(acting.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }

    // Each call is its own logical flow here, as each request is in a service: a statement made in
    // one never reaches another.
    [Fact]
    public async Task Send_OutsideAnyOutgoingCall_CarriesTheContextsUser()
    {
        var acting = Guid.NewGuid();

        var sent = await Send(new StatedContext { UserId = acting });

        Assert.Equal(acting.ToString(), sent.Headers.GetValues(BitwardenHeaders.User).Single());
    }
}
