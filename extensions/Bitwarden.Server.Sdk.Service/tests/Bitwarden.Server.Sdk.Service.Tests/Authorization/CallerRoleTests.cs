using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// In a product service, a session issued for one organization speaks for that organization
/// alone.
/// </summary>
/// <remarks>
/// The organization a role counts in is the session's signed claim, never a header the caller
/// asserts, and the header is refused. Otherwise an owner of organization A could send B's
/// identifier and be treated as an owner of B (P05).
/// </remarks>
public sealed class CallerRoleTests : IAsyncLifetime
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid OrganizationA = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid OrganizationB = new("b0000000-0000-4000-8000-0000000000bb");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    // An owner of A naming B can't even try: a product service takes the organization from the
    // session and refuses the header.
    [Theory]
    [InlineData("Bitwarden-Organization")]
    [InlineData("Bitwarden-User")]
    [InlineData("Bitwarden-Provider")]
    [InlineData("Bitwarden-Context")]
    public async Task GetOwnersProbe_AnyCallerContextHeader_Refused400(string header)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/owners", Session(OrganizationA, "Owner"));
        request.Headers.TryAddWithoutValidation(header, OrganizationB.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await FirstError(response);
        Assert.Equal("invalid-request", error.GetProperty("code").GetString());
        Assert.Equal(header, error.GetProperty("source").GetProperty("header").GetString());
    }

    // Refused before authentication: the answer is the same whatever the token, or with none.
    [Fact]
    public async Task GetProbe_HeaderWithNoToken_Refused400()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/probes/current");
        request.Headers.Add(BitwardenHeaders.Organization, OrganizationA.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A role claim with no organization claim names no organization it could count for.
    [Fact]
    public async Task GetOwnersProbe_OwnerRoleWithoutOrganizationClaim_Forbidden403()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/owners", Session(organization: null, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOwnersProbe_OwnerSessionForA_Ok()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/owners", Session(OrganizationA, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(true, "Owner True")]
    [InlineData(false, "None False")]
    public async Task GetRoleProbe_OwnerRole_OnlyInASessionIssuedForAnOrganization(bool issuedForA, string expected)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/role", Session(issuedForA ? OrganizationA : null, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await Note(response));
    }

    // A custom member's permissions are about the same organization as their role.
    [Fact]
    public async Task GetRoleProbe_CustomSessionHoldingManageUsers_HoldsIt()
    {
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object>
            {
                ["organization"] = OrganizationA.ToString(),
                ["organization_role"] = "Custom",
                ["organization_permission"] = new[] { "ManageUsers" },
            },
            SessionHost.Scope);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/role", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Custom True", await Note(response));
    }

    // A provider's session names its provider, and its role counts there.
    [Fact]
    public async Task GetProviderRoleProbe_ProviderSession_RoleForItsProvider()
    {
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object> { ["provider"] = Guid.NewGuid().ToString(), ["provider_role"] = "ProviderAdmin" },
            SessionHost.Scope);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ProviderAdmin", await Note(response));
    }

    [Fact]
    public async Task GetProviderRoleProbe_RoleWithoutAProviderOrOrganization_None()
    {
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object> { ["provider_role"] = "ProviderAdmin" },
            SessionHost.Scope);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("None", await Note(response));
    }

    // A provider's staff member's session for a client organization: their provider
    // role counts in that organization.
    [Fact]
    public async Task GetProviderRoleProbe_ClientOrganizationSession_RoleInIt()
    {
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object>
            {
                ["organization"] = OrganizationA.ToString(),
                ["provider"] = Guid.NewGuid().ToString(),
                ["provider_role"] = "ServiceUser",
            },
            SessionHost.Scope);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ServiceUser", await Note(response));
    }

    // Below the product layer the header is the calling service's assertion and there is
    // no session to compare it with: believed from another service.
    [Fact]
    public async Task GetProbe_DomainServiceWithHeaderFromAService_Ok()
    {
        await using var domain = await SessionHost.StartAsync(productService: false);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Organization, OrganizationB.ToString());

        using var response = await domain.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string Session(Guid? organization, string role)
    {
        var claims = new Dictionary<string, object> { ["organization_role"] = role };

        if (organization is not null)
        {
            claims["organization"] = organization.Value.ToString();
        }

        return SessionHost.Token(Person, claims, SessionHost.Scope);
    }

    private static async Task<string?> Note(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("note").GetString();
    }

    private static async Task<JsonElement> FirstError(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement.GetProperty("errors")[0].Clone();
    }
}
