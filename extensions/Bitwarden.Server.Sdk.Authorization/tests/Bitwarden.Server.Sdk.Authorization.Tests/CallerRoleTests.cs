using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The caller's role and permissions, read from the session's claims, and counted only in the organization or provider
/// the session was issued for: otherwise an owner of one organization could act as an owner of another by naming it.
/// </summary>
public sealed class CallerRoleTests
{
    private static readonly Guid OrganizationA = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid OrganizationB = new("b0000000-0000-4000-8000-0000000000bb");
    private static readonly Guid ProviderA = new("a2000000-0000-4000-8000-0000000000aa");
    private static readonly Guid ProviderB = new("b2000000-0000-4000-8000-0000000000bb");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private static Claim[] Session(Guid? organization = null, string? role = null, Guid? provider = null, string? providerRole = null, bool person = true, params string[] permissions)
    {
        var claims = new List<Claim>();

        if (person)
        {
            claims.Add(new Claim(CurrentContext.SubjectClaim, Person.ToString()));
        }

        if (organization is not null)
        {
            claims.Add(new Claim(CallerRole.OrganizationClaim, organization.Value.ToString()));
        }

        if (role is not null)
        {
            claims.Add(new Claim(CallerRole.RoleClaim, role));
        }

        if (provider is not null)
        {
            claims.Add(new Claim(CallerRole.ProviderClaim, provider.Value.ToString()));
        }

        if (providerRole is not null)
        {
            claims.Add(new Claim(CallerRole.ProviderRoleClaim, providerRole));
        }

        claims.AddRange(permissions.Select(permission => new Claim(CallerRole.PermissionClaim, permission)));
        return [.. claims];
    }

    private static CallerRole Role(Claim[] claims, Guid? organization = null, Guid? provider = null, params object[] metadata)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "GetWidget"));
        var context = new StatedContext { OrganizationId = organization, ProviderId = provider };
        return new CallerRole(new HttpContextAccessor { HttpContext = http }, context);
    }

    [Theory]
    [InlineData("Owner", OrganizationRole.Owner)]
    [InlineData("admin", OrganizationRole.Admin)]
    [InlineData("CUSTOM", OrganizationRole.Custom)]
    [InlineData("User", OrganizationRole.User)]
    public void Role_SessionForTheOrganizationInContext_IsTheClaimedRole(string claimed, OrganizationRole expected)
    {
        Assert.Equal(expected, Role(Session(OrganizationA, claimed), organization: OrganizationA).Role);
    }

    // A role is named, never numbered or listed: Enum.TryParse would read "4" as Owner, "99" as a role above every
    // other, and "User, Custom" as Admin.
    [Theory]
    [InlineData("4")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("User, Custom")]
    [InlineData("User,Custom")]
    [InlineData(" Owner")]
    [InlineData("")]
    public void Role_AClaimThatNamesNoRole_None(string claimed)
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, claimed), organization: OrganizationA).Role);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("ServiceUser, Admin")]
    public void ProviderRole_AClaimThatNamesNoRole_None(string claimed)
    {
        Assert.Equal(
            ProviderRole.None,
            Role(Session(provider: ProviderA, providerRole: claimed), provider: ProviderA).ProviderRole);
    }

    [Fact]
    public void Role_SessionForAnotherOrganization_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, "Owner"), organization: OrganizationB).Role);
    }

    // A role claim with no organization claim names no organization it could count for.
    [Fact]
    public void Role_SessionNamingNoOrganization_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(role: "Owner"), organization: OrganizationA).Role);
    }

    [Fact]
    public void Role_NoOrganizationInContext_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, "Owner")).Role);
    }

    [Theory]
    [InlineData("Superuser")]
    [InlineData("")]
    public void Role_AnUnknownRole_None(string claimed)
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, claimed), organization: OrganizationA).Role);
    }

    [Fact]
    public void Role_NoRequest_None()
    {
        var role = new CallerRole(new HttpContextAccessor(), new StatedContext { OrganizationId = OrganizationA });

        Assert.Equal(OrganizationRole.None, role.Role);
        Assert.Equal(ProviderRole.None, role.ProviderRole);
        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // An organization's API key acts as its owner, where the operation accepts one.
    [Fact]
    public void Role_AnOrganizationCredentialWhereAccepted_Owner()
    {
        var role = Role(
            Session(OrganizationA, person: false),
            organization: OrganizationA,
            metadata: new AcceptsOrganizationCredentialAttribute("widgets.read"));

        Assert.Equal(OrganizationRole.Owner, role.Role);
        Assert.True(role.Holds(OrganizationPermission.ManageUsers));
    }

    // And of no other organization.
    [Fact]
    public void Role_AnOrganizationCredentialForAnotherOrganization_None()
    {
        var role = Role(
            Session(OrganizationA, person: false),
            organization: OrganizationB,
            metadata: new AcceptsOrganizationCredentialAttribute("widgets.read"));

        Assert.Equal(OrganizationRole.None, role.Role);
    }

    [Fact]
    public void Role_AnOrganizationCredentialWhereNotAccepted_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, person: false), organization: OrganizationA).Role);
    }

    // A person's session for the organization is the person's, whatever the operation accepts.
    [Fact]
    public void Role_APersonsSessionWhereAnOrganizationCredentialIsAccepted_IsTheirOwnRole()
    {
        var role = Role(
            Session(OrganizationA, "User"),
            organization: OrganizationA,
            metadata: new AcceptsOrganizationCredentialAttribute("widgets.read"));

        Assert.Equal(OrganizationRole.User, role.Role);
    }

    [Fact]
    public void ProviderRole_SessionForTheProviderInContext_IsTheClaimedRole()
    {
        var role = Role(Session(provider: ProviderA, providerRole: "ProviderAdmin"), provider: ProviderA);

        Assert.Equal(ProviderRole.ProviderAdmin, role.ProviderRole);
    }

    // A provider's staff member administering a client organization holds no membership there, only the provider role.
    [Fact]
    public void ProviderRole_StaffSessionForTheOrganizationInContext_IsTheClaimedRole()
    {
        var role = Role(
            Session(OrganizationA, provider: ProviderA, providerRole: "serviceuser"),
            organization: OrganizationA);

        Assert.Equal(ProviderRole.ServiceUser, role.ProviderRole);
        Assert.Equal(OrganizationRole.None, role.Role);
    }

    [Fact]
    public void ProviderRole_SessionForAnotherProvider_None()
    {
        var role = Role(Session(provider: ProviderA, providerRole: "ProviderAdmin"), provider: ProviderB);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    [Fact]
    public void ProviderRole_StaffSessionForAnotherOrganization_None()
    {
        var role = Role(
            Session(OrganizationA, provider: ProviderA, providerRole: "ProviderAdmin"),
            organization: OrganizationB);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    [Fact]
    public void ProviderRole_NothingInContext_None()
    {
        var role = Role(Session(OrganizationA, provider: ProviderA, providerRole: "ProviderAdmin"));

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    [Fact]
    public void ProviderRole_AnUnknownRole_None()
    {
        var role = Role(Session(provider: ProviderA, providerRole: "Superuser"), provider: ProviderA);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    // Asking for no permission asks nothing of the caller.
    [Fact]
    public void Holds_NoPermission_TrueForAnybody()
    {
        Assert.True(Role(Session()).Holds(OrganizationPermission.None));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public void Holds_AnOwnerOrAdministrator_HoldsEveryPermission(string claimed)
    {
        var role = Role(Session(OrganizationA, claimed), organization: OrganizationA);

        Assert.All(
            Enum.GetValues<OrganizationPermission>(),
            permission => Assert.True(role.Holds(permission)));
    }

    [Fact]
    public void Holds_ACustomMember_HoldsWhatTheyAreGranted()
    {
        var role = Role(
            Session(OrganizationA, "Custom", permissions: ["manageGroups", "AccessReports"]),
            organization: OrganizationA);

        Assert.True(role.Holds(OrganizationPermission.ManageGroups));
        Assert.True(role.Holds(OrganizationPermission.AccessReports));
        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // Permission claims belong to a custom role; a plain member holds none, whatever the session lists.
    [Fact]
    public void Holds_AMemberWithPermissionClaims_HoldsNone()
    {
        var role = Role(Session(OrganizationA, "User", permissions: ["ManageUsers"]), organization: OrganizationA);

        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // Granted in one organization, held in no other.
    [Fact]
    public void Holds_ACustomMemberOfAnotherOrganization_HoldsNone()
    {
        var role = Role(Session(OrganizationA, "Custom", permissions: ["ManageUsers"]), organization: OrganizationB);

        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    [Fact]
    public void IssuedFor_IsTheSessionsOrganizationClaim()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(Session(OrganizationA), "Bearer")) };

        Assert.Equal(OrganizationA, CallerRole.IssuedFor(http));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-identifier")]
    public void IssuedFor_NoOrganization_Null(string? claimed)
    {
        Claim[] claims = claimed is null ? [] : [new Claim(CallerRole.OrganizationClaim, claimed)];
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };

        Assert.Null(CallerRole.IssuedFor(http));
    }
}
