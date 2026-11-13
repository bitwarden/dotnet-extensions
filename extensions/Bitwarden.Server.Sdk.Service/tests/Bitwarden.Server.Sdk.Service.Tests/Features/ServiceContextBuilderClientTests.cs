using System.Security.Claims;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Service.Features;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using LaunchDarkly.Sdk;
using Microsoft.AspNetCore.Http;
using LdContext = LaunchDarkly.Sdk.Context;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// The party a request's flags are evaluated for, one test per kind of client a token's <c>type</c> claim names:
/// read by a product service from the token, and the same again by a service below it from what the product
/// service carried.
/// </summary>
public sealed class ServiceContextBuilderClientTests
{
    private static readonly Guid Person = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Organization = new("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Connector = new("33333333-3333-4333-8333-333333333333");
    private static readonly Guid ServiceAccount = new("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Member = new("55555555-5555-4555-8555-555555555555");

    private static readonly ContextKind OrganizationKind = ContextKind.Of("organization");
    private static readonly ContextKind ServiceAccountKind = ContextKind.Of("service-account");
    private static readonly ContextKind AccessConnectorKind = ContextKind.Of("access-connector");

    public static TheoryData<string> Tokens => [.. Shapes.Keys];

    // Every shape of token, under the name its test uses.
    private static readonly Dictionary<string, Claim[]> Shapes = new()
    {
        ["person"] = [new("sub", Person.ToString()), new("organizations", Member.ToString())],
        ["nobody"] = [new("client_id", "installation.x"), new("client_sub", Guid.NewGuid().ToString())],
        ["scim"] = [new("client_id", $"organization.{Organization}"), new("client_sub", Organization.ToString()), new("organization", Organization.ToString())],
        ["organization-api-key"] = [new("client_id", $"organization.{Organization}"), new("client_sub", $"organization.{Organization}"), new("organization", Organization.ToString())],
        ["organization"] = [new("type", "Organization"), new("organization", Organization.ToString())],
        ["service-account"] = [new("type", "ServiceAccount"), new("service_account", ServiceAccount.ToString()), new("organization", Organization.ToString())],
        ["service-account-without-account"] = [new("type", "ServiceAccount"), new("organization", Organization.ToString())],
        ["access-connector"] = [new("type", "AccessConnector"), new("client_id", $"access-connector.{Guid.NewGuid()}"), new("access_connector", Connector.ToString()), new("organization", Organization.ToString())],
        ["send"] = [new("type", "Send"), new("send_id", "a-send"), new("client_sub", "send:a-send")],
        ["unknown-type"] = [new("type", "Something"), new("sub", Person.ToString())],
    };

    // Blue's User: a person keyed by the person, with their organizations.
    [Fact]
    public void Build_APerson_KeyedByThePersonWithTheirOrganizations()
    {
        var user = Single(Product("person"), ContextKind.Default);

        Assert.Equal(Person.ToString(), user.Key);
        Assert.False(user.Anonymous);
        Assert.Equal([Member.ToString()], Organizations(user));
    }

    // Blue's User with no person: an installation's token, anonymous with no organizations.
    [Fact]
    public void Build_NobodyInParticular_Anonymous()
    {
        var user = Single(Product("nobody"), ContextKind.Default);

        Assert.Equal(ServiceContextBuilder.AnonymousKey, user.Key);
        Assert.True(user.Anonymous);
        Assert.True(user.GetValue("organizations").IsNull);
    }

    // Blue's SCIM principal carries no type, so it is a User: anonymous, with the organization blue's orgApi reading
    // lists.
    [Fact]
    public void Build_Scim_AnonymousUserWithTheOrganization()
    {
        var user = Single(Product("scim"), ContextKind.Default);

        Assert.True(user.Anonymous);
        Assert.Equal([Organization.ToString()], Organizations(user));
    }

    // Blue's IdentityServer writes an organization API key's type as client_type, so blue reads it as a User too.
    [Fact]
    public void Build_AnOrganizationApiKey_AnonymousUserWithTheOrganization()
    {
        var context = Product("organization-api-key");

        var user = Single(context, ContextKind.Default);
        Assert.True(user.Anonymous);
        Assert.Equal([Organization.ToString()], Organizations(user));
        Assert.False(context.TryGetContextByKind(OrganizationKind, out _));
    }

    [Fact]
    public void Build_AnOrganizationType_AnOrganizationContextKeyedByTheOrganization()
    {
        var context = Product("organization");

        Assert.Equal(Organization.ToString(), Single(context, OrganizationKind).Key);
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_AMachineAccount_AServiceAccountContextKeyedByTheMachineAccount()
    {
        var context = Product("service-account");

        var serviceAccount = Single(context, ServiceAccountKind);
        Assert.Equal(ServiceAccount.ToString(), serviceAccount.Key);
        Assert.True(serviceAccount.GetValue("organizations").IsNull);
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_AMachineAccountTokenNamingNoAccount_KeyedByItsOrganization()
    {
        Assert.Equal(Organization.ToString(), Single(Product("service-account-without-account"), ServiceAccountKind).Key);
    }

    [Fact]
    public void Build_AnAccessConnector_AnAccessConnectorContextWithItsOrganization()
    {
        var context = Product("access-connector");

        var connector = Single(context, AccessConnectorKind);
        Assert.Equal(Connector.ToString(), connector.Key);
        Assert.Equal([Organization.ToString()], Organizations(connector));
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_AnAccessConnectorTokenNamingNoConnector_NoContextForTheParty()
    {
        var http = Authenticated(new Claim("type", "AccessConnector"), new Claim("organization", Organization.ToString()));
        var accessor = new HttpContextAccessor { HttpContext = http };

        var context = new ServiceContextBuilder(accessor, new CurrentContext(accessor, isProductService: true)).Build();

        Assert.False(context.TryGetContextByKind(AccessConnectorKind, out _));
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_ASend_Anonymous()
    {
        var user = Single(Product("send"), ContextKind.Default);

        Assert.Equal(ServiceContextBuilder.AnonymousKey, user.Key);
        Assert.True(user.Anonymous);
    }

    // Blue's Enum.TryParse failing leaves the default, User.
    [Fact]
    public void Build_AnUnknownType_ReadAsAUser()
    {
        Assert.Equal(Person.ToString(), Single(Product("unknown-type"), ContextKind.Default).Key);
    }

    // Below the product layer, the same context from what the product service carried.
    [Theory]
    [MemberData(nameof(Tokens))]
    public void Build_DomainService_TheSameAsTheProductService(string token)
    {
        var headers = new Dictionary<string, string>
        {
            ["Device-Type"] = "Android",
            ["Bitwarden-Client-Version"] = "2026.10.1",
            ["Is-Prerelease"] = "1",
            ["Device-Identifier"] = "device-1",
        };

        var productHttp = Authenticated(Shapes[token]);
        foreach (var (name, value) in headers)
        {
            productHttp.Request.Headers[name] = value;
        }

        var productAccessor = new HttpContextAccessor { HttpContext = productHttp };
        var product = new CurrentContext(productAccessor, isProductService: true);
        var expected = new ServiceContextBuilder(productAccessor, product).Build();

        // What a product service's call carries (OutgoingContext).
        var carried = ContextHeader.Write(new ContextValue
        {
            DeviceType = product.DeviceType,
            DeviceIdentifier = product.DeviceIdentifier,
            Organizations = product.UserOrganizationIds,
            FlagKind = product.FlagSubjectKind,
            FlagKey = product.FlagSubjectKey,
            ClientVersion = product.ClientVersion,
            ClientVersionIsPrerelease = product.ClientVersionIsPrerelease,
        });

        var domainHttp = Authenticated(new Claim("client_id", "internal.password-manager-service"));
        domainHttp.Request.Headers[BitwardenHeaders.Context] = carried;
        SignedHeaders.Keep(domainHttp);
        var domainAccessor = new HttpContextAccessor { HttpContext = domainHttp };

        var actual = new ServiceContextBuilder(domainAccessor, new CurrentContext(domainAccessor)).Build();

        Assert.Equal(expected, actual);
        Assert.Equal(0, Single(actual, ContextKind.Of("device")).GetValue("device-type").AsInt);
    }

    private static LdContext Product(string token)
    {
        var accessor = new HttpContextAccessor { HttpContext = Authenticated(Shapes[token]) };

        return new ServiceContextBuilder(accessor, new CurrentContext(accessor, isProductService: true)).Build();
    }

    private static DefaultHttpContext Authenticated(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")) };

    private static LdContext Single(LdContext context, ContextKind kind)
    {
        Assert.True(context.TryGetContextByKind(kind, out var single), $"No {kind} context.");
        return single;
    }

    private static IEnumerable<string> Organizations(LdContext context) =>
        context.GetValue("organizations").List.Select(value => value.AsString);
}
