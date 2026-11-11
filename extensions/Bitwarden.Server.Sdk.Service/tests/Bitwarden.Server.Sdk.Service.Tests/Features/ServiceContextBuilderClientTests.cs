using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Service.Features;
using LaunchDarkly.Sdk;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// The LaunchDarkly context for a caller that is not a person: an organization's API key is evaluated under an <c>organization</c> context keyed by the organization, and a PAM
/// access connector under an <c>access-connector</c> context keyed by the connector, its organization in
/// <c>organizations</c>; a connector's token naming no connector has no such context.
/// </summary>
public sealed class ServiceContextBuilderClientTests
{
    private static readonly Guid Organization = new("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Connector = new("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Build_AnOrganizationApiKeyCaller_AnOrganizationContextKeyedByTheOrganization()
    {
        var http = Authenticated(new Claim("client_id", $"organization.{Organization}"));

        var context = Builder(http).Build();

        Assert.True(context.TryGetContextByKind(ContextKind.Of("organization"), out var organization));
        Assert.Equal(Organization.ToString(), organization.Key);
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_AnAccessConnector_AnAccessConnectorContextWithItsOrganization()
    {
        var http = Authenticated(new Claim("client_id", $"access-connector.{Guid.NewGuid()}"), new Claim("access_connector", Connector.ToString()));

        var context = Builder(http).Build();

        Assert.True(context.TryGetContextByKind(ContextKind.Of("access-connector"), out var connector));
        Assert.Equal(Connector.ToString(), connector.Key);
        Assert.Equal([Organization.ToString()], connector.GetValue("organizations").List.Select(value => value.AsString));
        Assert.False(context.TryGetContextByKind(ContextKind.Default, out _));
    }

    [Fact]
    public void Build_AnAccessConnectorTokenNamingNoConnector_NoAccessConnectorContext()
    {
        var http = Authenticated(new Claim("client_id", $"access-connector.{Guid.NewGuid()}"));

        var context = Builder(http).Build();

        Assert.False(context.TryGetContextByKind(ContextKind.Of("access-connector"), out _));
    }

    private static ServiceContextBuilder Builder(HttpContext http) =>
        new(new HttpContextAccessor { HttpContext = http }, new OrganizationContext());

    private static DefaultHttpContext Authenticated(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")) };

    private sealed class OrganizationContext : IContext
    {
        public Guid? OrganizationId => Organization;
        public Guid? UserId => null;
        public Guid? ProviderId => null;
        public string? DeviceIdentifier => null;
        public IReadOnlyList<Guid>? UserOrganizationIds => null;
    }
}
