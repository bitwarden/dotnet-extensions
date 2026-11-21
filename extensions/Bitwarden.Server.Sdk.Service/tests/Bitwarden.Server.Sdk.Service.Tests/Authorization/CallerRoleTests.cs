using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public async Task GetOwnersProbe_AnyContextHeader_Refused400(string header)
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
    public async Task GetOwnersProbe_OwnerNowForA_Ok()
    {
        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(OrganizationRole.Owner));
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/owners", Session(OrganizationA, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The role the session was issued with counts for nothing: an owner demoted since is refused with the same token.
    [Fact]
    public async Task GetOwnersProbe_SessionIssuedToAnOwnerDemotedSince_Forbidden403()
    {
        var token = Session(OrganizationA, "Owner");
        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(OrganizationRole.Owner));
        using (var before = ProductServiceAttributeTests.Get("/api/v1/probes/owners", token))
        using (var allowed = await _service.Client().SendAsync(before, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(OrganizationRole.User));
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/owners", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "The caller must have at least the Owner role in the organization.",
            (await FirstError(response)).GetProperty("detail").GetString());
    }

    // A person no longer a confirmed member of the session's organization is refused before any handler, as the
    // server answers an organization the person isn't a member of.
    [Fact]
    public async Task GetRoleProbe_MemberRemovedSince_NotFound404()
    {
        _service.Standing.Member(Person, OrganizationA, null);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/role", Session(OrganizationA, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await FirstError(response);
        Assert.Equal("not-found", error.GetProperty("code").GetString());
        Assert.Equal(ErrorDetails.NotFound, error.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, "Owner True")]
    [InlineData(OrganizationRole.Admin, "Admin True")]
    [InlineData(OrganizationRole.User, "User False")]
    public async Task GetRoleProbe_RoleNow_OnlyInASessionIssuedForTheOrganization(OrganizationRole role, string expected)
    {
        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(role));
        using var issued = ProductServiceAttributeTests.Get("/api/v1/probes/role", Session(OrganizationA, "User"));
        using var none = ProductServiceAttributeTests.Get("/api/v1/probes/role", Session(organization: null, "Owner"));

        using var forA = await _service.Client().SendAsync(issued, TestContext.Current.CancellationToken);
        using var forNone = await _service.Client().SendAsync(none, TestContext.Current.CancellationToken);

        Assert.Equal(expected, await Note(forA));
        Assert.Equal("None False", await Note(forNone));
    }

    // A custom member's permissions are theirs now, and about the same organization as their role.
    [Fact]
    public async Task GetRoleProbe_CustomMemberGrantedManageUsersNow_HoldsIt()
    {
        _service.Standing.Member(
            Person, OrganizationA, new MemberStanding(OrganizationRole.Custom, new HashSet<OrganizationPermission> { OrganizationPermission.ManageUsers }));
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object> { ["organization"] = OrganizationA.ToString(), ["organization_role"] = "Custom" },
            SessionHost.Scope);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/role", token);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Custom True", await Note(response));
    }

    // A permission claim the session was issued with grants nothing the member doesn't hold now.
    [Fact]
    public async Task GetRoleProbe_PermissionClaimTheMemberNoLongerHolds_NotHeld()
    {
        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(OrganizationRole.Custom));
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

        Assert.Equal("Custom False", await Note(response));
    }

    // A provider's session names its provider, and the person's role on its staff now counts there.
    [Fact]
    public async Task GetProviderRoleProbe_ProviderSession_RoleNowForItsProvider()
    {
        var provider = Guid.NewGuid();
        _service.Standing.Staff(Person, provider, ProviderRole.ProviderAdmin);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", ProviderSession(provider, "ServiceUser"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ProviderAdmin", await Note(response));
    }

    [Fact]
    public async Task GetProviderRoleProbe_StaffRemovedSince_NotFound404()
    {
        var provider = Guid.NewGuid();
        _service.Standing.Staff(Person, provider, null);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", ProviderSession(provider, "ProviderAdmin"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    // A provider's staff member's session for a client organization: their provider role now counts in that
    // organization, and they hold no organization role there, as the session was issued.
    [Fact]
    public async Task GetProviderRoleProbe_ClientOrganizationSession_RoleNowInIt()
    {
        var provider = Guid.NewGuid();
        _service.Standing.ProviderOf(OrganizationA, provider);
        _service.Standing.Staff(Person, provider, ProviderRole.ServiceUser);
        var token = ClientSession(provider);

        using var providerRole = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", token), TestContext.Current.CancellationToken);
        using var role = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get("/api/v1/probes/role", token), TestContext.Current.CancellationToken);

        Assert.Equal("ServiceUser", await Note(providerRole));
        Assert.Equal("None False", await Note(role));
    }

    // The client organization moved to another provider, or left: the session's provider administers it no longer. The
    // server checks a provider's staff against its database on each request, and forbids.
    [Fact]
    public async Task GetProviderRoleProbe_ClientNoLongerTheSessionsProviders_Forbidden403()
    {
        var provider = Guid.NewGuid();
        _service.Standing.ProviderOf(OrganizationA, Guid.NewGuid());
        _service.Standing.Staff(Person, provider, ProviderRole.ProviderAdmin);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", ClientSession(provider));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "The caller must be staff of the provider administering the organization.",
            (await FirstError(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetProviderRoleProbe_ClientsStaffMemberRemovedSince_Forbidden403()
    {
        var provider = Guid.NewGuid();
        _service.Standing.ProviderOf(OrganizationA, provider);
        _service.Standing.Staff(Person, provider, null);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/provider-role", ClientSession(provider));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "The caller must be staff of the provider administering the organization.",
            (await FirstError(response)).GetProperty("detail").GetString());
    }

    // Asked once per request however often the role is read: the probe reads the role and a permission.
    [Fact]
    public async Task GetRoleProbe_ReadsTheStandingOncePerRequest()
    {
        _service.Standing.Member(Person, OrganizationA, MemberStanding.Of(OrganizationRole.Admin));
        var before = _service.Standing.Reads;
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/role", Session(OrganizationA, "Admin"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Admin True", await Note(response));
        Assert.Equal(1, _service.Standing.Reads - before);
    }

    // Nothing to ask of a session naming no organization or provider, nor of an operation open to anybody.
    [Theory]
    [InlineData("/api/v1/probes/role", true)]
    [InlineData("/api/v1/health", false)]
    public async Task Request_NothingTheSessionNamesOrOpenOperation_StandingNotRead(string path, bool personal)
    {
        _service.Standing.Member(Person, OrganizationA, null);
        var before = _service.Standing.Reads;
        using var request = ProductServiceAttributeTests.Get(path, personal ? Session(organization: null, "Owner") : Session(OrganizationA, "Owner"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _service.Standing.Reads - before);
    }

    // A product service that can't read current standing doesn't start, rather than authorizing from its sessions.
    [Fact]
    public async Task Start_ProductServiceWithoutCurrentStanding_Fails()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SessionHost.StartAsync(
            productService: true,
            services: services => services.RemoveAll<ICurrentStanding>()));

        Assert.Contains(nameof(ICurrentStanding), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_DomainServiceWithoutCurrentStanding_Starts()
    {
        await using var domain = await SessionHost.StartAsync(productService: false);

        Assert.False(domain.Identity.IsProductService);
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

    private static string ProviderSession(Guid provider, string role) => SessionHost.Token(
        Person,
        new Dictionary<string, object> { ["provider"] = provider.ToString(), ["provider_role"] = role },
        SessionHost.Scope);

    private static string ClientSession(Guid provider) => SessionHost.Token(
        Person,
        new Dictionary<string, object>
        {
            ["organization"] = OrganizationA.ToString(),
            ["provider"] = provider.ToString(),
            ["provider_role"] = "ProviderAdmin",
        },
        SessionHost.Scope);

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
