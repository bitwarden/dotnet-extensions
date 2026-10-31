using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Where a service's context comes from: a product service's from the session it
/// validated, a domain service's from the headers another service sent it, and from nobody else.
/// </summary>
public sealed class ContextTests
{
    private static readonly Claim Internal = new("client_id", "internal.some-service");

    [Fact]
    public void ClientIpAddress_FromTheContextHeader_IsCanonical()
    {
        var context = During(request => request.Headers[BitwardenHeaders.Context] =
            """{"v":1,"clientIp":"2001:0db8:0000:0000:0000:0000:0000:0001"}""");

        var actual = context.ClientIpAddress;

        Assert.Equal("2001:db8::1", actual);
    }

    // A malformed value is refused before an operation reads anything (CallerContextHeaders); read
    // directly, it carries nothing.
    [Fact]
    public void ClientIpAddress_AMalformedContextHeader_CarriesNothing()
    {
        var context = During(request => request.Headers[BitwardenHeaders.Context] = """{"v":1,"clientIp":"<script>"}""");

        Assert.Null(context.ClientIpAddress);
    }

    // The client's address is the connection's, never what the client says it is.
    [Fact]
    public void ClientIpAddress_InAProductService_IsTheConnections_NotAHeaderTheClientSent()
    {
        var context = DuringProduct(request =>
        {
            request.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
            request.Headers[BitwardenHeaders.Context] = """{"v":1,"clientIp":"198.51.100.66"}""";
        });

        Assert.Equal("203.0.113.9", context.ClientIpAddress);
    }

    [Fact]
    public void ClientIpAddress_InAProductService_WithNoConnectionAddress_IsNone()
    {
        var context = DuringProduct(request => request.Headers[BitwardenHeaders.Context] = """{"v":1,"clientIp":"198.51.100.66"}""");

        Assert.Null(context.ClientIpAddress);
    }

    [Fact]
    public void ClientIpAddress_InAProductService_AnIPv4AddressCarriedInIPv6_IsTheIPv4Address()
    {
        var context = DuringProduct(request =>
            request.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:203.0.113.9"));

        Assert.Equal("203.0.113.9", context.ClientIpAddress);
    }

    [Fact]
    public void DeviceType_ShortName_IsCarried()
    {
        var context = During(request => request.Headers[BitwardenHeaders.Context] = """{"v":1,"deviceType":"ChromeBrowser"}""");

        Assert.Equal("ChromeBrowser", context.DeviceType);
    }

    // A product service takes the device type from the client's own request, its Device-Type
    // header first and then the token's claim, and carries it on from there.
    [Fact]
    public void DeviceType_InAProductService_TheClientsDeviceTypeHeader()
    {
        var context = DuringProduct(request => request.Headers["Device-Type"] = "2");

        Assert.Equal("2", context.DeviceType);
    }

    [Fact]
    public void DeviceType_InAProductService_TheTokensClaimWhenNoHeader()
    {
        var context = DuringProduct(_ => { }, new Claim("devicetype", "14"));

        Assert.Equal("14", context.DeviceType);
    }

    [Fact]
    public void DeviceType_InAProductService_NotANumber_IsDropped()
    {
        var context = DuringProduct(request => request.Headers["Device-Type"] = "Chrome");

        Assert.Null(context.DeviceType);
    }

    // A domain service reads only what the calling service carried.
    [Fact]
    public void DeviceType_InADomainService_IgnoresTheClientsHeader()
    {
        var context = During(request => request.Headers["Device-Type"] = "2");

        Assert.Null(context.DeviceType);
    }

    [Fact]
    public void DeviceIdentifier_AsAClientGeneratesIt_IsCarried()
    {
        var context = During(request => request.Headers[BitwardenHeaders.Context] =
            """{"v":1,"deviceIdentifier":"4f1a9c2e-7d3b-4e8a-9b6c-1d2e3f4a5b6c"}""");

        Assert.Equal("4f1a9c2e-7d3b-4e8a-9b6c-1d2e3f4a5b6c", context.DeviceIdentifier);
    }

    // The session's signed claim first, then the Device-Identifier header.
    [Fact]
    public void DeviceIdentifier_InAProductService_IsTheSessionsSignedClaim()
    {
        var context = DuringProduct(
            request => request.Headers["Device-Identifier"] = "from-the-header",
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("device", "from-the-claim"));

        Assert.Equal("from-the-claim", context.DeviceIdentifier);
    }

    [Fact]
    public void DeviceIdentifier_InAProductService_TheHeaderWhereTheSessionNamesNone()
    {
        var context = DuringProduct(request => request.Headers["Device-Identifier"] = "from-the-header");

        Assert.Equal("from-the-header", context.DeviceIdentifier);
    }

    // What a push tag admits, and nothing else. An identifier with characters taken out would
    // name some other device.
    [Theory]
    [InlineData("device one")]
    [InlineData("device\" && !userId:x")]
    [InlineData("an-identifier-that-is-longer-than-the-fifty-characters-a-device-may-have")]
    public void DeviceIdentifier_InAProductService_NotOneAPushTagAdmits_IsDropped(string value)
    {
        var context = DuringProduct(request => request.Headers["Device-Identifier"] = value);

        Assert.Null(context.DeviceIdentifier);
    }

    [Fact]
    public void DeviceIdentifier_WithNoRequest_IsTheScopes()
    {
        var context = new Context(new HttpContextAccessor());

        string? actual;
        using (ContextScope.Begin(new StatedContext { DeviceIdentifier = "device-1" }))
        {
            actual = context.DeviceIdentifier;
        }

        Assert.Equal("device-1", actual);
    }

    // What a message consumer relies on: with no request, the scope it began is the context.
    [Fact]
    public void Organization_WithNoRequest_IsTheScopes()
    {
        var expected = Guid.NewGuid();
        var context = new Context(new HttpContextAccessor());

        Guid? actual;
        using (ContextScope.Begin(new StatedContext { OrganizationId = expected, Credential = CallerCredential.System }))
        {
            actual = context.OrganizationId;
            Assert.Equal(CallerCredential.System, context.Credential);
        }

        Assert.Equal(expected, actual);
        Assert.Null(context.OrganizationId);
    }

    // A scope must not let code inside a request substitute an organization the caller never
    // named.
    [Fact]
    public void Organization_DuringARequest_IgnoresAnyScope()
    {
        var named = Guid.NewGuid();
        var context = During(request => request.Headers[BitwardenHeaders.Organization] = named.ToString());

        Guid? actual;
        using (ContextScope.Begin(new StatedContext { OrganizationId = Guid.NewGuid() }))
        {
            actual = context.OrganizationId;
        }

        Assert.Equal(named, actual);
    }

    // Where a person's session ends, the acting user is the one Identity signed for, and a header
    // naming somebody else changes nothing, though the pipeline refuses it before here.
    [Fact]
    public void UserId_ProductService_IsTheSessionsSubjectNotTheHeader()
    {
        var subject = Guid.NewGuid();
        var context = DuringProduct(
            request => request.Headers[BitwardenHeaders.User] = Guid.NewGuid().ToString(),
            new Claim("sub", subject.ToString()));

        Assert.Equal(subject, context.UserId);
    }

    // Before authentication, or with no session at all, there is nobody, never a header.
    [Fact]
    public void UserId_ProductServiceWithNoSession_IsNobody()
    {
        var context = DuringProduct(request => request.Headers[BitwardenHeaders.User] = Guid.NewGuid().ToString());

        Assert.Null(context.UserId);
    }

    // The organization is the session's signed claim, never the request's say.
    [Fact]
    public void Organization_ProductService_IsTheSessionsClaimNotTheHeader()
    {
        var issuedFor = Guid.NewGuid();
        var context = DuringProduct(
            request => request.Headers[BitwardenHeaders.Organization] = Guid.NewGuid().ToString(),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("organization", issuedFor.ToString()));

        Assert.Equal(issuedFor, context.OrganizationId);
    }

    // A provider's session (the Provider Portal's) names the provider.
    [Fact]
    public void Provider_ProductService_IsAProviderSessionsClaim()
    {
        var provider = Guid.NewGuid();
        var context = DuringProduct(
            request => request.Headers[BitwardenHeaders.Provider] = Guid.NewGuid().ToString(),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("provider", provider.ToString()));

        Assert.Equal(provider, context.ProviderId);
    }

    // A provider's staff member's session for a client organization is about the organization.
    [Fact]
    public void Provider_ProductService_ASessionForAClientOrganization_IsNone()
    {
        var organization = Guid.NewGuid();
        var context = DuringProduct(
            _ => { },
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("organization", organization.ToString()),
            new Claim("provider", Guid.NewGuid().ToString()));

        Assert.Null(context.ProviderId);
        Assert.Equal(organization, context.OrganizationId);
    }

    // ...but the provider is recorded beside them, as the provider on the organization's events.
    [Fact]
    public void RecordedProvider_ProductService_ASessionForAClientOrganization_IsItsProvider()
    {
        var provider = Guid.NewGuid();
        var context = DuringProduct(
            _ => { },
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("organization", Guid.NewGuid().ToString()),
            new Claim("provider", provider.ToString()));

        Assert.Equal(provider, context.RecordedProviderId);
    }

    // A provider's own session names it as the provider, and records nothing beside.
    [Fact]
    public void RecordedProvider_ProductService_AProvidersOwnSession_IsNone()
    {
        var context = DuringProduct(
            request => request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"recordedProvider":"{{Guid.NewGuid()}}"}""",
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("provider", Guid.NewGuid().ToString()));

        Assert.Null(context.RecordedProviderId);
    }

    [Theory]
    [InlineData(CallerCredential.User, "sub")]
    [InlineData(CallerCredential.Organization, "organization")]
    [InlineData(CallerCredential.Machine, "service_account")]
    public void Credential_ProductService_IsTheKindOfToken(CallerCredential expected, string claim)
    {
        var claims = new List<Claim> { new(claim, Guid.NewGuid().ToString()) };
        if (claim == "service_account")
        {
            claims.Add(new Claim("organization", Guid.NewGuid().ToString()));
        }

        var context = DuringProduct(_ => { }, [.. claims]);

        Assert.Equal(expected, context.Credential);
    }

    // A Send's token names a Send and nobody: no credential to record.
    [Fact]
    public void Credential_ProductService_ASendsToken_IsNone()
    {
        var context = DuringProduct(_ => { }, new Claim("send_id", Guid.NewGuid().ToString()));

        Assert.Null(context.Credential);
    }

    [Fact]
    public void RecordedUser_ProductService_IsNeverTheRequests()
    {
        var context = DuringProduct(
            request => request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"credential":"SYSTEM","recordedUser":"{{Guid.NewGuid()}}"}""",
            new Claim("sub", Guid.NewGuid().ToString()));

        Assert.Null(context.RecordedUserId);
        Assert.Equal(CallerCredential.User, context.Credential);
    }

    // The product token's organizations, identifiers only, for feature flags.
    [Fact]
    public void UserOrganizationIds_ProductService_TheTokensOrganizationsIdentifiersOnly()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var context = DuringProduct(
            _ => { },
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("organizations", first.ToString()),
            new Claim("organizations", "not an organization"),
            new Claim("organizations", second.ToString()),
            new Claim("organizations", first.ToString()));

        Assert.Equal([first, second], context.UserOrganizationIds);
    }

    // A product service never takes them from a header, and only a person has them.
    [Fact]
    public void UserOrganizationIds_ProductService_NotAHeaderNorWithoutAPerson()
    {
        var header = $$"""{"v":1,"organizations":["{{Guid.NewGuid()}}"]}""";

        var person = DuringProduct(
            request => request.Headers[BitwardenHeaders.Context] = header,
            new Claim("sub", Guid.NewGuid().ToString()));
        var apiKey = DuringProduct(
            _ => { },
            new Claim("organization", Guid.NewGuid().ToString()),
            new Claim("organizations", Guid.NewGuid().ToString()));

        Assert.Null(person.UserOrganizationIds);
        Assert.Null(apiKey.UserOrganizationIds);
    }

    [Fact]
    public void UserOrganizationIds_DomainService_WhatTheCallingServiceCarried()
    {
        var organization = Guid.NewGuid();
        var context = During(
            request => request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"organizations":["{{organization}}"]}""",
            new Claim("organizations", Guid.NewGuid().ToString()));

        Assert.Equal([organization], context.UserOrganizationIds);
    }

    [Fact]
    public void UserOrganizationIds_DomainService_FromAnybodyButAService_IsNone()
    {
        var context = DuringFrom(
            request => request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"organizations":["{{Guid.NewGuid()}}"]}""",
            [new Claim("client_id", "web"), new Claim("sub", Guid.NewGuid().ToString())]);

        Assert.Null(context.UserOrganizationIds);
    }

    [Fact]
    public void UserId_DomainService_IsTheHeaderNotTheSubject()
    {
        var named = Guid.NewGuid();
        var context = During(
            request => request.Headers[BitwardenHeaders.User] = named.ToString(),
            new Claim("sub", Guid.NewGuid().ToString()));

        Assert.Equal(named, context.UserId);
    }

    [Fact]
    public void Everything_DomainService_FromAnotherService_IsWhatItCarried()
    {
        var organization = Guid.NewGuid();
        var user = Guid.NewGuid();
        var provider = Guid.NewGuid();
        var recorded = Guid.NewGuid();
        var context = During(request =>
        {
            request.Headers[BitwardenHeaders.Organization] = organization.ToString();
            request.Headers[BitwardenHeaders.User] = user.ToString();
            request.Headers[BitwardenHeaders.Provider] = provider.ToString();
            request.Headers[BitwardenHeaders.Context] = $$"""{"v":1,"credential":"SYSTEM","recordedUser":"{{recorded}}","recordedProvider":"{{provider}}"}""";
        });

        Assert.Equal(organization, context.OrganizationId);
        Assert.Equal(user, context.UserId);
        Assert.Equal(provider, context.ProviderId);
        Assert.Equal(CallerCredential.System, context.Credential);
        Assert.Equal(recorded, context.RecordedUserId);
        Assert.Equal(provider, context.RecordedProviderId);
    }

    // A person's token is not a service's: a surface browsers call would otherwise believe a forged
    // organization. Only another service is believed.
    [Theory]
    [InlineData(null)]
    [InlineData("web")]
    [InlineData("organization.a0000000-0000-4000-8000-000000000001")]
    [InlineData("internal.")]
    [InlineData("Internal.some-service")]
    public void Everything_DomainService_FromAnybodyButAService_IsEmpty(string? client)
    {
        Claim[] claims = client is null ? [] : [new Claim("client_id", client), new Claim("sub", Guid.NewGuid().ToString())];
        var context = DuringFrom(
            request =>
            {
                request.Headers[BitwardenHeaders.Organization] = Guid.NewGuid().ToString();
                request.Headers[BitwardenHeaders.User] = Guid.NewGuid().ToString();
                request.Headers[BitwardenHeaders.Provider] = Guid.NewGuid().ToString();
                request.Headers[BitwardenHeaders.Context] = """{"v":1,"clientIp":"198.51.100.66","credential":"USER"}""";
            },
            claims);

        Assert.Null(context.OrganizationId);
        Assert.Null(context.UserId);
        Assert.Null(context.ProviderId);
        Assert.Null(context.ClientIpAddress);
        Assert.Null(context.Credential);
    }

    // A Send's token, addressed to the platform as well as to Password Manager, is nobody's service.
    [Fact]
    public void Everything_DomainService_FromASendsToken_IsEmpty()
    {
        var context = DuringFrom(
            request => request.Headers[BitwardenHeaders.Organization] = Guid.NewGuid().ToString(),
            [new Claim("send_id", Guid.NewGuid().ToString()), new Claim("client_sub", "send:x")]);

        Assert.Null(context.OrganizationId);
    }

    [Fact]
    public void Everything_DomainService_Unauthenticated_IsEmpty()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[BitwardenHeaders.Organization] = Guid.NewGuid().ToString();

        var context = new Context(new HttpContextAccessor { HttpContext = http });

        Assert.Null(context.OrganizationId);
    }

    /// <summary>
    /// A domain service's request from another service: an internal service token.
    /// </summary>
    private static Context During(Action<HttpRequest> arrange, params Claim[] claims) =>
        DuringFrom(arrange, [Internal, .. claims]);

    private static Context DuringFrom(Action<HttpRequest> arrange, Claim[] claims)
    {
        var http = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        }

        arrange(http.Request);
        SignedHeaders.Keep(http);

        return new Context(new HttpContextAccessor { HttpContext = http });
    }

    private static Context DuringProduct(Action<HttpRequest> arrange, params Claim[] claims)
    {
        var http = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        }

        arrange(http.Request);

        return new Context(new HttpContextAccessor { HttpContext = http }, isProductService: true);
    }
}
