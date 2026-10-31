using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Features;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using LaunchDarkly.Sdk;
using Microsoft.AspNetCore.Http;
using LdContext = LaunchDarkly.Sdk.Context;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// The LaunchDarkly context a service builds for a request: fixed kinds, keys and attribute names,
/// so one LaunchDarkly project targets every Bitwarden server alike.
/// </summary>
public sealed class ServiceContextBuilderTests
{
    private static readonly Guid Person = new("d4000000-0000-4000-8000-00000000000d");

    private static readonly ContextKind Device = ContextKind.Of("device");

    [Fact]
    public void Build_NoRequest_SingleAnonymousContext()
    {
        var context = Builder(http: null).Build();

        Assert.False(context.Multiple);
        Assert.Equal(ContextKind.Default, context.Kind);
        Assert.Equal(ServiceContextBuilder.AnonymousKey, context.Key);
        Assert.True(context.Anonymous);
    }

    [Fact]
    public void Build_RequestWithoutUser_AnonymousUserMarkedAnonymous()
    {
        var context = Builder(new DefaultHttpContext()).Build();

        var user = Kind(context, ContextKind.Default);
        Assert.Equal(ServiceContextBuilder.AnonymousKey, user.Key);
        Assert.True(user.Anonymous);
        Assert.False(context.TryGetContextByKind(Device, out _));
    }

    [Fact]
    public void Build_ActingUser_KeyedByTheUser()
    {
        var context = Builder(new DefaultHttpContext(), user: Person).Build();

        var user = Kind(context, ContextKind.Default);
        Assert.Equal(Person.ToString(), user.Key);
        Assert.False(user.Anonymous);
        Assert.True(user.GetValue("organizations").IsNull);
    }

    // The organizations attribute: the person's organizations, which a product service reads from the
    // product token and a domain service from the context the product service carried.
    [Fact]
    public void Build_PersonsOrganizations_SetOnTheUserContextOnly()
    {
        var first = new Guid("0a000000-0000-4000-8000-00000000000a");
        var second = new Guid("0b000000-0000-4000-8000-00000000000b");
        var http = new DefaultHttpContext();
        http.Request.Headers["Device-Identifier"] = "device-1";

        var context = Builder(http, user: Person, organizations: [first, second]).Build();

        var organizations = Kind(context, ContextKind.Default).GetValue("organizations");
        Assert.Equal(LdValueType.Array, organizations.Type);
        Assert.Equal([first.ToString(), second.ToString()], organizations.List.Select(value => value.AsString));
        Assert.True(Kind(context, Device).GetValue("organizations").IsNull);
    }

    // Only a person has organizations.
    [Fact]
    public void Build_OrganizationsWithoutAPerson_NotSet()
    {
        var user = Kind(Builder(new DefaultHttpContext(), organizations: [Guid.NewGuid()]).Build(), ContextKind.Default);

        Assert.True(user.Anonymous);
        Assert.True(user.GetValue("organizations").IsNull);
    }

    // Below the product layer: a domain service's real context, from what a product service carried for the
    // person on its call, and the same attribute the product service would have set.
    [Fact]
    public void Build_DomainService_OrganizationsTheProductServiceCarried()
    {
        var organization = new Guid("0c000000-0000-4000-8000-00000000000c");
        var http = Authenticated(new Claim("client_id", "internal.password-manager-service"));
        http.Request.Headers[BitwardenHeaders.User] = Person.ToString();
        http.Request.Headers[BitwardenHeaders.Context] =
            $$"""{"v":1,"credential":"USER","organizations":["{{organization}}"]}""";
        SignedHeaders.Keep(http);
        var accessor = new HttpContextAccessor { HttpContext = http };

        var context = new ServiceContextBuilder(accessor, new Context(accessor)).Build();

        var user = Kind(context, ContextKind.Default);
        Assert.Equal(Person.ToString(), user.Key);
        Assert.Equal([organization.ToString()], user.GetValue("organizations").List.Select(value => value.AsString));
    }

    // A product service's real context, from the product token's claim.
    [Fact]
    public void Build_ProductService_OrganizationsFromTheToken()
    {
        var organization = new Guid("0d000000-0000-4000-8000-00000000000d");
        var http = Authenticated(new Claim("sub", Person.ToString()), new Claim("organizations", organization.ToString()));
        var accessor = new HttpContextAccessor { HttpContext = http };

        var context = new ServiceContextBuilder(accessor, new Context(accessor, isProductService: true)).Build();

        Assert.Equal(
            [organization.ToString()],
            Kind(context, ContextKind.Default).GetValue("organizations").List.Select(value => value.AsString));
    }

    // A web vault in Chrome: device type 9, which is a Web client (1).
    [Fact]
    public void Build_ClientHeaders_SetOnEveryContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Device-Identifier"] = "device-1";
        http.Request.Headers["Device-Type"] = "9";
        http.Request.Headers["Bitwarden-Client-Version"] = "2026.9.1";
        http.Request.Headers["Is-Prerelease"] = "1";

        var context = Builder(http, user: Person).Build();

        foreach (var kind in (ContextKind[])[ContextKind.Default, Device])
        {
            var single = Kind(context, kind);
            Assert.Equal("2026.9.1", single.GetValue("client-version").AsString);
            Assert.True(single.GetValue("client-version-is-prerelease").AsBool);
            Assert.Equal(9, single.GetValue("device-type").AsInt);
            Assert.Equal(1, single.GetValue("client-type").AsInt);
        }

        Assert.Equal("device-1", Kind(context, Device).Key);
    }

    [Theory]
    [InlineData(0, 4)]   // Android: Mobile
    [InlineData(2, 2)]   // ChromeExtension: Browser
    [InlineData(6, 3)]   // WindowsDesktop: Desktop
    [InlineData(14, 1)]  // UnknownBrowser: Web
    [InlineData(21, 0)]  // SDK: All
    [InlineData(25, 5)]  // LinuxCLI: Cli
    [InlineData(26, 1)]  // DuckDuckGoBrowser: Web
    [InlineData(27, 2)]  // DuckDuckGoExtension: Browser
    public void Build_DeviceType_ClientTypeOfItsKind(int deviceType, int clientType)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Device-Type"] = deviceType.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var user = Kind(Builder(http).Build(), ContextKind.Default);

        Assert.Equal(clientType, user.GetValue("client-type").AsInt);
    }

    // Prerelease is only said alongside a version.
    [Fact]
    public void Build_NoClientVersion_NoVersionAttributes()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Is-Prerelease"] = "1";

        var user = Kind(Builder(http).Build(), ContextKind.Default);

        Assert.True(user.GetValue("client-version").IsNull);
        Assert.True(user.GetValue("client-version-is-prerelease").IsNull);
        Assert.True(user.GetValue("device-type").IsNull);
    }

    [Fact]
    public void Build_TokenDeviceAndHeaderDevice_TokenWins()
    {
        var http = Authenticated(new Claim("device", "from-token"));
        http.Request.Headers["Device-Identifier"] = "from-header";

        var device = Kind(Builder(http, user: Person).Build(), Device);

        Assert.Equal("from-token", device.Key);
    }

    [Fact]
    public void Build_TokenDeviceTypeAndHeaderDeviceType_HeaderWins()
    {
        var http = Authenticated(new Claim("devicetype", "0"));
        http.Request.Headers["Device-Type"] = "9";

        var user = Kind(Builder(http, user: Person).Build(), ContextKind.Default);

        Assert.Equal(9, user.GetValue("device-type").AsInt);
    }

    // Free text is not carried to a third party as a device's key.
    [Fact]
    public void Build_DeviceIdentifierOutsideThePattern_NoDeviceContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Device-Identifier"] = "not a device identifier <script>";

        var context = Builder(http).Build();

        Assert.False(context.TryGetContextByKind(Device, out _));
    }

    // Below the product layer the device arrives in the context a product service carried.
    [Fact]
    public void Build_CarriedDeviceIdentifier_UsedWhenTheRequestNamesNone()
    {
        var context = Builder(new DefaultHttpContext(), user: Person, carriedDevice: "carried-device").Build();

        Assert.Equal("carried-device", Kind(context, Device).Key);
    }

    private static ServiceContextBuilder Builder(
        HttpContext? http,
        Guid? user = null,
        string? carriedDevice = null,
        IReadOnlyList<Guid>? organizations = null) =>
        new(new HttpContextAccessor { HttpContext = http }, new FixedContext(user, carriedDevice, organizations));

    private static DefaultHttpContext Authenticated(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")) };

    private static LdContext Kind(LdContext context, ContextKind kind)
    {
        Assert.True(context.TryGetContextByKind(kind, out var single), $"No {kind} context.");
        return single;
    }

    private sealed class FixedContext(Guid? user, string? device, IReadOnlyList<Guid>? organizations) : IContext
    {
        public Guid? OrganizationId => null;
        public Guid? UserId => user;
        public Guid? ProviderId => null;
        public string? DeviceIdentifier => device;
        public IReadOnlyList<Guid>? UserOrganizationIds => organizations;
    }
}
