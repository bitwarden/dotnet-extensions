using System.Security.Claims;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The caller's role and permissions, from their standing now (resolved for the request), and counted only in the
/// organization or provider the session was issued for: otherwise an owner of one organization could act as an owner
/// of another by naming it. The session's role and permission claims count for nothing.
/// </summary>
public sealed class CallerRoleTests
{
    private static readonly Guid OrganizationA = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid OrganizationB = new("b0000000-0000-4000-8000-0000000000bb");
    private static readonly Guid ProviderA = new("a2000000-0000-4000-8000-0000000000aa");
    private static readonly Guid ProviderB = new("b2000000-0000-4000-8000-0000000000bb");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private static Claim[] Session(Guid? organization = null, Guid? provider = null, bool person = true, params Claim[] extra)
    {
        var claims = new List<Claim>(extra);

        if (person)
        {
            claims.Add(new Claim(CurrentContext.SubjectClaim, Person.ToString()));
        }

        if (organization is not null)
        {
            claims.Add(new Claim(CallerRole.OrganizationClaim, organization.Value.ToString()));
        }

        if (provider is not null)
        {
            claims.Add(new Claim(CallerRole.ProviderClaim, provider.Value.ToString()));
        }

        return [.. claims];
    }

    private static CallerStanding Member(OrganizationRole role, params OrganizationPermission[] permissions) =>
        new(role, permissions.ToHashSet(), ProviderRole.None);

    private static CallerStanding Staff(ProviderRole role) => CallerStanding.Nothing with { ProviderRole = role };

    private static CallerRole Role(
        Claim[] claims,
        CallerStanding? standing,
        Guid? organization = null,
        Guid? provider = null,
        params object[] metadata)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "GetWidget"));

        if (standing is not null)
        {
            http.Features.Set(standing);
        }

        var context = new StatedContext { OrganizationId = organization, ProviderId = provider };
        return new CallerRole(new HttpContextAccessor { HttpContext = http }, context);
    }

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Admin)]
    [InlineData(OrganizationRole.Custom)]
    [InlineData(OrganizationRole.User)]
    public void Role_SessionForTheOrganizationInContext_IsTheRoleNow(OrganizationRole now)
    {
        Assert.Equal(now, Role(Session(OrganizationA), Member(now), organization: OrganizationA).Role);
    }

    // What the session was issued with is history: an owner's session for somebody who is a plain member now.
    [Fact]
    public void Role_RoleClaimsOfTheSession_Ignored()
    {
        var role = Role(
            Session(
                OrganizationA,
                extra: [new Claim("organization_role", "Owner"), new Claim("organization_permission", "ManageUsers")]),
            Member(OrganizationRole.User),
            organization: OrganizationA);

        Assert.Equal(OrganizationRole.User, role.Role);
        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    [Fact]
    public void Role_SessionForAnotherOrganization_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA), Member(OrganizationRole.Owner), organization: OrganizationB).Role);
    }

    // A session with no organization claim names no organization a role could count for.
    [Fact]
    public void Role_SessionNamingNoOrganization_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(), CallerStanding.Nothing, organization: OrganizationA).Role);
    }

    [Fact]
    public void Role_NoOrganizationInContext_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA), Member(OrganizationRole.Owner)).Role);
    }

    [Fact]
    public void Role_NoRequest_None()
    {
        var role = new CallerRole(new HttpContextAccessor(), new StatedContext { OrganizationId = OrganizationA });

        Assert.Equal(OrganizationRole.None, role.Role);
        Assert.Equal(ProviderRole.None, role.ProviderRole);
        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // Never answered from the session's claims instead: a person's role read where it wasn't resolved is a defect.
    [Fact]
    public void Role_APersonsStandingNotResolved_Throws()
    {
        var role = Role(Session(OrganizationA, extra: new Claim("organization_role", "Owner")), standing: null, organization: OrganizationA);

        Assert.Throws<InvalidOperationException>(() => role.Role);
        Assert.Throws<InvalidOperationException>(() => role.Holds(OrganizationPermission.ManageUsers));
    }

    // An organization's API key acts as its owner, where the operation accepts one. No person, nothing to resolve.
    [Fact]
    public void Role_AnOrganizationCredentialWhereAccepted_Owner()
    {
        var role = Role(
            Session(OrganizationA, person: false),
            standing: null,
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
            standing: null,
            organization: OrganizationB,
            metadata: new AcceptsOrganizationCredentialAttribute("widgets.read"));

        Assert.Equal(OrganizationRole.None, role.Role);
    }

    [Fact]
    public void Role_AnOrganizationCredentialWhereNotAccepted_None()
    {
        Assert.Equal(OrganizationRole.None, Role(Session(OrganizationA, person: false), standing: null, organization: OrganizationA).Role);
    }

    // A person's session for the organization is the person's, whatever the operation accepts.
    [Fact]
    public void Role_APersonsSessionWhereAnOrganizationCredentialIsAccepted_IsTheirOwnRole()
    {
        var role = Role(
            Session(OrganizationA),
            Member(OrganizationRole.User),
            organization: OrganizationA,
            metadata: new AcceptsOrganizationCredentialAttribute("widgets.read"));

        Assert.Equal(OrganizationRole.User, role.Role);
    }

    [Fact]
    public void ProviderRole_SessionForTheProviderInContext_IsTheRoleNow()
    {
        var role = Role(
            Session(provider: ProviderA, extra: new Claim("provider_role", "ServiceUser")),
            Staff(ProviderRole.ProviderAdmin),
            provider: ProviderA);

        Assert.Equal(ProviderRole.ProviderAdmin, role.ProviderRole);
    }

    // A provider's staff member administering a client organization holds no membership there, only the provider role.
    [Fact]
    public void ProviderRole_StaffSessionForTheOrganizationInContext_IsTheRoleNow()
    {
        var role = Role(Session(OrganizationA, provider: ProviderA), Staff(ProviderRole.ServiceUser), organization: OrganizationA);

        Assert.Equal(ProviderRole.ServiceUser, role.ProviderRole);
        Assert.Equal(OrganizationRole.None, role.Role);
    }

    [Fact]
    public void ProviderRole_SessionForAnotherProvider_None()
    {
        var role = Role(Session(provider: ProviderA), Staff(ProviderRole.ProviderAdmin), provider: ProviderB);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    [Fact]
    public void ProviderRole_StaffSessionForAnotherOrganization_None()
    {
        var role = Role(Session(OrganizationA, provider: ProviderA), Staff(ProviderRole.ProviderAdmin), organization: OrganizationB);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    [Fact]
    public void ProviderRole_NothingInContext_None()
    {
        var role = Role(Session(OrganizationA, provider: ProviderA), Staff(ProviderRole.ProviderAdmin));

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    // A member's session holds no provider role, whatever the session carries.
    [Fact]
    public void ProviderRole_MembersSession_None()
    {
        var role = Role(
            Session(OrganizationA, extra: new Claim("provider_role", "ProviderAdmin")),
            Member(OrganizationRole.Owner),
            organization: OrganizationA);

        Assert.Equal(ProviderRole.None, role.ProviderRole);
    }

    // Asking for no permission asks nothing of the caller.
    [Fact]
    public void Holds_NoPermission_TrueForAnybody()
    {
        Assert.True(Role(Session(), CallerStanding.Nothing).Holds(OrganizationPermission.None));
    }

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Admin)]
    public void Holds_AnOwnerOrAdministrator_HoldsEveryPermission(OrganizationRole now)
    {
        var role = Role(Session(OrganizationA), Member(now), organization: OrganizationA);

        Assert.All(
            Enum.GetValues<OrganizationPermission>(),
            permission => Assert.True(role.Holds(permission)));
    }

    [Fact]
    public void Holds_ACustomMember_HoldsWhatTheyAreGrantedNow()
    {
        var role = Role(
            Session(OrganizationA),
            Member(OrganizationRole.Custom, OrganizationPermission.ManageGroups, OrganizationPermission.AccessReports),
            organization: OrganizationA);

        Assert.True(role.Holds(OrganizationPermission.ManageGroups));
        Assert.True(role.Holds(OrganizationPermission.AccessReports));
        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // Permissions belong to a custom role; a plain member holds none.
    [Fact]
    public void Holds_AMemberWithPermissions_HoldsNone()
    {
        var role = Role(Session(OrganizationA), Member(OrganizationRole.User, OrganizationPermission.ManageUsers), organization: OrganizationA);

        Assert.False(role.Holds(OrganizationPermission.ManageUsers));
    }

    // Granted in one organization, held in no other.
    [Fact]
    public void Holds_ACustomMemberOfAnotherOrganization_HoldsNone()
    {
        var role = Role(Session(OrganizationA), Member(OrganizationRole.Custom, OrganizationPermission.ManageUsers), organization: OrganizationB);

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

    [Fact]
    public void ProviderIssuedFor_IsTheSessionsProviderClaim()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(Session(OrganizationA, ProviderA), "Bearer")) };

        Assert.Equal(ProviderA, CallerRole.ProviderIssuedFor(http));
    }
}
