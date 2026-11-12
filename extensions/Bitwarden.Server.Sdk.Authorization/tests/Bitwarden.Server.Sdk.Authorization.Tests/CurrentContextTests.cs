using System.Net;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The current request's context: in a product service the validated session's and the connection's, never a header;
/// in a domain service what the verified context token says, and only from another service; outside a request, the
/// scope a consumer began.
/// </summary>
public sealed class CurrentContextTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid OtherOrganization = new("0d000000-0000-4000-8000-00000000000d");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid Provider = new("b2000000-0000-4000-8000-00000000000b");

    private static DefaultHttpContext Request(Claim[]? claims = null, bool authenticated = true) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims ?? [], authenticated ? "Bearer" : null)),
    };

    private static CurrentContext Product(HttpContext? http) =>
        new(new HttpContextAccessor { HttpContext = http }, isProductService: true);

    private static CurrentContext Domain(HttpContext? http) =>
        new(new HttpContextAccessor { HttpContext = http });

    private static Claim C(string type, string value) => new(type, value);

    /// <summary>
    /// A call from another service whose context token said all of this, kept as the middleware keeps it.
    /// </summary>
    private static DefaultHttpContext Called(string client = "internal.gadget-service", bool authenticated = true)
    {
        var http = Request([C(InternalCaller.ClientClaim, client)], authenticated);
        ContextHeaders.Keep(http, new VerifiedContext(
            Organization,
            Person,
            Provider,
            new ContextValue
            {
                ClientIp = "192.0.2.10",
                DeviceType = "9",
                DeviceIdentifier = "device-one",
                Credential = CallerCredential.System,
                RecordedUser = Person,
                RecordedProvider = Provider,
                Organizations = [Organization, OtherOrganization],
                FlagKind = FlagSubjectKind.ServiceAccount,
                FlagKey = Person,
                ClientVersion = "2026.10.1",
                ClientVersionIsPrerelease = true,
            },
            "a-context-token",
            ContextToken.Asserted));
        return http;
    }

    private static void AssertEmpty(IContext context)
    {
        Assert.Null(context.OrganizationId);
        Assert.Null(context.UserId);
        Assert.Null(context.ProviderId);
        Assert.Null(context.ClientIpAddress);
        Assert.Null(context.DeviceType);
        Assert.Null(context.DeviceIdentifier);
        Assert.Null(context.Credential);
        Assert.Null(context.RecordedUserId);
        Assert.Null(context.RecordedProviderId);
        Assert.Null(context.UserOrganizationIds);
        Assert.Equal(FlagSubjectKind.User, context.FlagSubjectKind);
        Assert.Null(context.FlagSubjectKey);
        Assert.Null(context.ClientVersion);
        Assert.False(context.ClientVersionIsPrerelease);
    }

    // ---- what flags are evaluated for ----

    [Fact]
    public void FlagSubject_DomainService_WhatTheCallingServiceCarried()
    {
        var context = Domain(Called());

        Assert.Equal(FlagSubjectKind.ServiceAccount, context.FlagSubjectKind);
        Assert.Equal(Person, context.FlagSubjectKey);
        Assert.Equal("2026.10.1", context.ClientVersion);
        Assert.True(context.ClientVersionIsPrerelease);
    }

    // Blue's IdentityClientType, from the token's type claim.
    [Theory]
    [InlineData(null, FlagSubjectKind.User)]
    [InlineData("User", FlagSubjectKind.User)]
    [InlineData("Organization", FlagSubjectKind.Organization)]
    [InlineData("ServiceAccount", FlagSubjectKind.ServiceAccount)]
    [InlineData("AccessConnector", FlagSubjectKind.AccessConnector)]
    [InlineData("Send", FlagSubjectKind.User)]
    [InlineData("organization", FlagSubjectKind.User)]
    [InlineData("1", FlagSubjectKind.User)]
    public void FlagSubject_ProductService_TheTokensType(string? type, FlagSubjectKind kind)
    {
        Claim[] claims = type is null ? [C("sub", Person.ToString())] : [C("sub", Person.ToString()), C("type", type)];

        Assert.Equal(kind, Product(Request(claims)).FlagSubjectKind);
    }

    [Fact]
    public void FlagSubject_ProductService_APersonByTheirSubject()
    {
        var context = Product(Request([C("sub", Person.ToString()), C("organizations", Organization.ToString())]));

        Assert.Equal(Person, context.FlagSubjectKey);
        Assert.Equal([Organization], context.UserOrganizationIds);
    }

    // A Send is nobody in particular, whatever else its token says.
    [Fact]
    public void FlagSubject_ProductService_ASendIsNobody()
    {
        var context = Product(Request([C("type", "Send"), C("sub", Person.ToString()), C("organizations", Organization.ToString())]));

        Assert.Null(context.FlagSubjectKey);
        Assert.Null(context.UserOrganizationIds);
    }

    [Fact]
    public void FlagSubject_ProductService_AMachineAccountByItsAccount_ElseItsOrganization()
    {
        var machine = Guid.NewGuid();

        var byAccount = Product(Request([C("type", "ServiceAccount"), C("service_account", machine.ToString()), C("organization", Organization.ToString())])).FlagSubjectKey;
        var byOrganization = Product(Request([C("type", "ServiceAccount"), C("organization", Organization.ToString())])).FlagSubjectKey;

        Assert.Equal(machine, byAccount);
        Assert.Equal(Organization, byOrganization);
    }

    // No session, nobody: a token not yet authenticated says nothing.
    [Fact]
    public void FlagSubject_ProductService_Unauthenticated_Nobody()
    {
        var context = Product(Request([C("type", "Organization"), C("organization", Organization.ToString())], authenticated: false));

        Assert.Equal(FlagSubjectKind.User, context.FlagSubjectKind);
        Assert.Null(context.FlagSubjectKey);
    }

    [Theory]
    [InlineData("2026.10.1", "1", "2026.10.1", true)]
    [InlineData("2026.010.01", null, "2026.10.1", false)]
    [InlineData("2026.10.1", "0", "2026.10.1", false)]
    [InlineData("not-a-version", "1", null, false)]
    [InlineData(null, "1", null, false)]
    public void ClientVersion_ProductService_TheHeaderAsAVersion(string? header, string? prerelease, string? version, bool isPrerelease)
    {
        var http = Request();
        if (header is not null)
        {
            http.Request.Headers["Bitwarden-Client-Version"] = header;
        }

        if (prerelease is not null)
        {
            http.Request.Headers["Is-Prerelease"] = prerelease;
        }

        var context = Product(http);

        Assert.Equal(version, context.ClientVersion);
        Assert.Equal(isPrerelease, context.ClientVersionIsPrerelease);
    }

    // Blue's Enum.TryParse takes a device type's name too; the number is what is carried.
    [Theory]
    [InlineData("Android", "0")]
    [InlineData("WindowsCLI", "23")]
    [InlineData("9", "9")]
    [InlineData("android", null)]
    [InlineData("Chrome", null)]
    public void DeviceType_ProductService_ANumberOrAName(string header, string? carried)
    {
        var http = Request();
        http.Request.Headers["Device-Type"] = header;

        Assert.Equal(carried, Product(http).DeviceType);
    }

    [Fact]
    public void Everything_DomainService_FromAnotherService_IsWhatItsTokenSaid()
    {
        var context = Domain(Called());

        Assert.Equal(Organization, context.OrganizationId);
        Assert.Equal(Person, context.UserId);
        Assert.Equal(Provider, context.ProviderId);
        Assert.Equal("192.0.2.10", context.ClientIpAddress);
        Assert.Equal("9", context.DeviceType);
        Assert.Equal("device-one", context.DeviceIdentifier);
        Assert.Equal(CallerCredential.System, context.Credential);
        Assert.Equal(Person, context.RecordedUserId);
        Assert.Equal(Provider, context.RecordedProviderId);
        Assert.Equal([Organization, OtherOrganization], context.UserOrganizationIds);
    }

    // Whatever was kept, only an internal service token's caller is believed.
    [Theory]
    [InlineData("web")]
    [InlineData("internal.")]
    [InlineData("Internal.gadget-service")]
    [InlineData("organization.0c000000-0000-4000-8000-00000000000c")]
    public void Everything_DomainService_FromAnybodyButAService_IsEmpty(string client)
    {
        AssertEmpty(Domain(Called(client)));
    }

    [Fact]
    public void Everything_DomainService_Unauthenticated_IsEmpty()
    {
        AssertEmpty(Domain(Called(authenticated: false)));
    }

    // The plain headers are only ever a copy of the token: with nothing verified, a domain service reads nothing.
    [Fact]
    public void Everything_DomainService_PlainHeadersAlone_AreIgnored()
    {
        var http = Request([C(InternalCaller.ClientClaim, "internal.gadget-service")]);
        http.Request.Headers[BitwardenHeaders.Organization] = Organization.ToString();
        http.Request.Headers[BitwardenHeaders.User] = Person.ToString();
        http.Request.Headers[BitwardenHeaders.Context] = """{"v":1,"deviceType":"9"}""";
        http.Request.Headers["Device-Type"] = "9";

        AssertEmpty(Domain(http));
    }

    // A domain service's connection only ever shows the peer that called it.
    [Fact]
    public void ClientIpAddress_DomainService_NeverTheConnections()
    {
        var http = Request([C(InternalCaller.ClientClaim, "internal.gadget-service")]);
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.99");

        Assert.Null(Domain(http).ClientIpAddress);
    }

    [Fact]
    public void Everything_NoRequest_IsTheScopes()
    {
        var scoped = new StatedContext
        {
            OrganizationId = Organization,
            UserId = Person,
            ProviderId = Provider,
            ClientIpAddress = "192.0.2.10",
            DeviceType = "9",
            DeviceIdentifier = "device-one",
            Credential = CallerCredential.System,
            RecordedUserId = Person,
            RecordedProviderId = Provider,
            UserOrganizationIds = [Organization],
        };

        using (ContextScope.Begin(scoped))
        {
            foreach (var context in new[] { Domain(null), Product(null) })
            {
                Assert.Equal(Organization, context.OrganizationId);
                Assert.Equal(Person, context.UserId);
                Assert.Equal(Provider, context.ProviderId);
                Assert.Equal("192.0.2.10", context.ClientIpAddress);
                Assert.Equal("9", context.DeviceType);
                Assert.Equal("device-one", context.DeviceIdentifier);
                Assert.Equal(CallerCredential.System, context.Credential);
                Assert.Equal(Person, context.RecordedUserId);
                Assert.Equal(Provider, context.RecordedProviderId);
                Assert.Equal([Organization], context.UserOrganizationIds);
            }
        }
    }

    [Fact]
    public void Everything_NoRequestAndNoScope_IsEmpty()
    {
        AssertEmpty(Domain(null));
        AssertEmpty(Product(null));
    }

    // Honouring a scope inside a request would let code substitute an organization the caller never named.
    [Fact]
    public void Everything_DuringARequest_IgnoresAnyScope()
    {
        using (ContextScope.Begin(new StatedContext { OrganizationId = OtherOrganization, UserId = Person }))
        {
            AssertEmpty(Domain(Request()));
            AssertEmpty(Product(Request()));
            Assert.Equal(Organization, Domain(Called()).OrganizationId);
        }
    }

    // A consumer handed IContext can tell an event's actor from a person acting.
    [Fact]
    public void IsEvent_NoRequest_InAnEventsScope_True()
    {
        using (ContextScope.BeginForEvent(new StatedContext { OrganizationId = Organization, UserId = Person }))
        {
            foreach (var context in new[] { Domain(null), Product(null) })
            {
                Assert.True(context.IsEvent);
                Assert.Equal(Person, context.UserId);
            }
        }

        Assert.False(Domain(null).IsEvent);
    }

    [Fact]
    public void IsEvent_NoRequest_InAScopeThatIsNotAnEvents_False()
    {
        using (ContextScope.Begin(new StatedContext { OrganizationId = Organization, UserId = Person }))
        {
            Assert.False(Domain(null).IsEvent);
            Assert.False(Product(null).IsEvent);
        }
    }

    // No header says a request is an event's, and a scope is never honoured inside a request.
    [Fact]
    public void IsEvent_DuringARequest_False_WhateverTheScope()
    {
        using (ContextScope.BeginForEvent(new StatedContext { OrganizationId = Organization, UserId = Person }))
        {
            Assert.False(Domain(Called()).IsEvent);
            Assert.False(Domain(Request()).IsEvent);
            Assert.False(Product(Request()).IsEvent);
        }
    }

    [Fact]
    public void Owners_ProductService_AreTheSessionsClaims_NotTheHeaders()
    {
        var http = Request([C("sub", Person.ToString()), C("organization", Organization.ToString())]);
        http.Request.Headers[BitwardenHeaders.Organization] = OtherOrganization.ToString();
        http.Request.Headers[BitwardenHeaders.User] = Guid.NewGuid().ToString();

        var context = Product(http);

        Assert.Equal(Organization, context.OrganizationId);
        Assert.Equal(Person, context.UserId);
    }

    // Claims on a principal that never authenticated prove nothing.
    [Fact]
    public void Owners_ProductService_Unauthenticated_None()
    {
        var context = Product(Request(
            [C("sub", Person.ToString()), C("organization", Organization.ToString()), C("provider", Provider.ToString())],
            authenticated: false));

        Assert.Null(context.OrganizationId);
        Assert.Null(context.UserId);
        Assert.Null(context.ProviderId);
        Assert.Null(context.Credential);
    }

    [Fact]
    public void UserId_ProductService_ASubjectThatIsNotAnIdentifier_None()
    {
        Assert.Null(Product(Request([C("sub", "not-a-person")])).UserId);
    }

    [Fact]
    public void ProviderId_ProductService_IsAProviderSessionsClaim()
    {
        var context = Product(Request([C("sub", Person.ToString()), C("provider", Provider.ToString())]));

        Assert.Equal(Provider, context.ProviderId);
        Assert.Null(context.RecordedProviderId);
    }

    // A provider's staff member's session for a client organization is about the organization, on the provider's
    // authority.
    [Fact]
    public void ProviderId_ProductService_ASessionForAClientOrganization_IsNone_AndRecordedAsTheProvider()
    {
        var context = Product(Request(
            [C("sub", Person.ToString()), C("organization", Organization.ToString()), C("provider", Provider.ToString())]));

        Assert.Equal(Organization, context.OrganizationId);
        Assert.Null(context.ProviderId);
        Assert.Equal(Provider, context.RecordedProviderId);
    }

    [Theory]
    [InlineData("sub", CallerCredential.User)]
    [InlineData("service_account", CallerCredential.Machine)]
    public void Credential_ProductService_IsTheKindOfToken(string claim, CallerCredential expected)
    {
        Assert.Equal(expected, Product(Request([C(claim, Person.ToString()), C("organization", Organization.ToString())])).Credential);
    }

    // An organization's API key: an organization and no person.
    [Fact]
    public void Credential_ProductService_AnOrganizationsKey_Organization()
    {
        Assert.Equal(CallerCredential.Organization, Product(Request([C("organization", Organization.ToString())])).Credential);
    }

    [Fact]
    public void Credential_ProductService_ASendsToken_None()
    {
        Assert.Null(Product(Request([C(SendCredential.Claim, "a-send-id")])).Credential);
    }

    // A person's request is recorded as the person, whatever a header says.
    [Fact]
    public void RecordedUserId_ProductService_Never()
    {
        var http = Request([C("sub", Person.ToString())]);
        http.Request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"recordedUser":"{{Person}}"}""";

        Assert.Null(Product(http).RecordedUserId);
    }

    [Fact]
    public void UserOrganizationIds_ProductService_TheSessionsOrganizations_IdentifiersOnlyAndEachOnce()
    {
        var context = Product(Request(
        [
            C("sub", Person.ToString()),
            C("organizations", Organization.ToString()),
            C("organizations", "not-an-identifier"),
            C("organizations", Guid.Empty.ToString()),
            C("organizations", OtherOrganization.ToString()),
            C("organizations", Organization.ToString()),
        ]));

        Assert.Equal([Organization, OtherOrganization], context.UserOrganizationIds);
    }

    [Fact]
    public void UserOrganizationIds_ProductService_NoneListed_Null()
    {
        Assert.Null(Product(Request([C("sub", Person.ToString()), C("organizations", "not-an-identifier")])).UserOrganizationIds);
    }

    // The organizations a token lists are its flag subject's, whoever it names, as blue reads them from the claims
    // alone; a token naming nobody lists none.
    [Fact]
    public void UserOrganizationIds_ProductService_WithoutAPerson_TheTokensList()
    {
        Assert.Equal([Organization], Product(Request([C("organizations", Organization.ToString())])).UserOrganizationIds);
        Assert.Null(Product(Request([C("client_id", "installation.x")])).UserOrganizationIds);
    }

    // The client does not choose the address an audit record gives it.
    [Fact]
    public void ClientIpAddress_ProductService_IsTheConnections_NotAHeader()
    {
        var http = Request();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        http.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        http.Request.Headers[BitwardenHeaders.Context] = """{"v":1,"clientIp":"198.51.100.7"}""";

        Assert.Equal("192.0.2.10", Product(http).ClientIpAddress);
    }

    [Fact]
    public void ClientIpAddress_ProductService_AnIPv4AddressCarriedInIPv6_IsTheIPv4Address()
    {
        var http = Request();
        http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");

        Assert.Equal("192.0.2.10", Product(http).ClientIpAddress);
    }

    [Fact]
    public void ClientIpAddress_ProductService_NoConnectionAddress_None()
    {
        Assert.Null(Product(Request()).ClientIpAddress);
    }

    [Fact]
    public void DeviceType_ProductService_TheHeaderBeforeTheClaim()
    {
        var http = Request([C("devicetype", "7")]);
        http.Request.Headers["Device-Type"] = "9";

        Assert.Equal("9", Product(http).DeviceType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("chrome")]
    [InlineData("256")]
    [InlineData("-1")]
    public void DeviceType_ProductService_NoNumberInTheHeader_TheClaim(string? header)
    {
        var http = Request([C("devicetype", "7")]);
        if (header is not null)
        {
            http.Request.Headers["Device-Type"] = header;
        }

        Assert.Equal("7", Product(http).DeviceType);
    }

    [Fact]
    public void DeviceType_ProductService_NeitherANumber_None()
    {
        var http = Request([C("devicetype", "a device type that is a paragraph")]);
        http.Request.Headers["Device-Type"] = "chrome";

        Assert.Null(Product(http).DeviceType);
    }

    // An unauthenticated principal's claim is not the session's.
    [Fact]
    public void DeviceType_ProductService_UnauthenticatedClaim_None()
    {
        Assert.Null(Product(Request([C("devicetype", "7")], authenticated: false)).DeviceType);
    }

    [Fact]
    public void DeviceIdentifier_ProductService_TheSessionsSignedClaimBeforeTheHeader()
    {
        var http = Request([C("device", "signed-device")]);
        http.Request.Headers["Device-Identifier"] = "header-device";

        Assert.Equal("signed-device", Product(http).DeviceIdentifier);
    }

    [Fact]
    public void DeviceIdentifier_ProductService_TheHeaderWhereTheSessionNamesNone()
    {
        var http = Request();
        http.Request.Headers["Device-Identifier"] = "header-device:1_a";

        Assert.Equal("header-device:1_a", Product(http).DeviceIdentifier);
    }

    // Dropped rather than stripped: an identifier with characters removed names some other device.
    [Theory]
    [InlineData("device one")]
    [InlineData("device\" && !userId:x")]
    [InlineData("an-identifier-that-is-longer-than-the-fifty-characters-a-device-may-have")]
    public void DeviceIdentifier_ProductService_NotOneAPushTagAdmits_Dropped(string value)
    {
        var http = Request();
        http.Request.Headers["Device-Identifier"] = value;

        Assert.Null(Product(http).DeviceIdentifier);
    }

    [Fact]
    public void Subject_IsTheAuthenticatedSessionsPerson()
    {
        Assert.Equal(Person, CurrentContext.Subject(Request([C("sub", Person.ToString())])));
        Assert.Null(CurrentContext.Subject(Request([C("sub", Person.ToString())], authenticated: false)));
        Assert.Null(CurrentContext.Subject(Request()));
    }
}
