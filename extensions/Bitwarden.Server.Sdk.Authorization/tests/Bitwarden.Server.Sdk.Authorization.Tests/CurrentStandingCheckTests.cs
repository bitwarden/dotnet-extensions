using System.Security.Claims;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// A product service reads the person's standing in what their session names, once per request, and refuses a person
/// who no longer holds it.
/// </summary>
public sealed class CurrentStandingCheckTests
{
    private static readonly Guid Organization = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Provider = new("a2000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private readonly Standing _standing = new();

    [Fact]
    public void Construct_NoCurrentStandingRegistered_Throws()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var failure = Assert.Throws<InvalidOperationException>(() => new CurrentStandingCheck(
            _ => Task.CompletedTask,
            services.GetRequiredService<IServiceProviderIsService>()));

        Assert.Contains(nameof(ICurrentStanding), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoke_MembersSession_ResolvesTheirMembershipNow()
    {
        _standing.Membership = new MemberStanding(OrganizationRole.Custom, new HashSet<OrganizationPermission> { OrganizationPermission.ManageUsers });

        var http = await InvokeAsync(Session(Organization));

        var resolved = CallerStanding.Of(http)!;
        Assert.Equal(OrganizationRole.Custom, resolved.Role);
        Assert.Contains(OrganizationPermission.ManageUsers, resolved.Permissions);
        Assert.Equal(ProviderRole.None, resolved.ProviderRole);
        Assert.Equal(["membership"], _standing.Asked);
    }

    // Permissions belong to a custom member alone.
    [Fact]
    public async Task Invoke_AdminWithPermissions_KeepsNone()
    {
        _standing.Membership = new MemberStanding(OrganizationRole.Admin, new HashSet<OrganizationPermission> { OrganizationPermission.ManageUsers });

        var http = await InvokeAsync(Session(Organization));

        Assert.Empty(CallerStanding.Of(http)!.Permissions);
    }

    [Fact]
    public async Task Invoke_NoLongerAMember_RefusedNotFound()
    {
        _standing.Membership = null;

        await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(Session(Organization)));
    }

    [Fact]
    public async Task Invoke_ClientOrganizationSession_ResolvesTheStaffRoleNow()
    {
        _standing.ProviderOf = Provider;
        _standing.ProviderRole = ProviderRole.ServiceUser;

        var http = await InvokeAsync(Session(Organization, Provider));

        var resolved = CallerStanding.Of(http)!;
        Assert.Equal(OrganizationRole.None, resolved.Role);
        Assert.Equal(ProviderRole.ServiceUser, resolved.ProviderRole);
        Assert.Equal(["provider-of", "provider-role"], _standing.Asked);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Invoke_ClientNoLongerTheProvidersOrPersonNoLongerStaff_Forbidden(bool movedAway, bool removed)
    {
        _standing.ProviderOf = movedAway ? Guid.NewGuid() : Provider;
        _standing.ProviderRole = removed ? null : ProviderRole.ProviderAdmin;

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync(Session(Organization, Provider)));

        Assert.Equal(CurrentStandingCheck.NotProviderStaff, refused.Message);
    }

    [Fact]
    public async Task Invoke_ProviderSession_ResolvesTheStaffRoleNow()
    {
        _standing.ProviderRole = ProviderRole.ProviderAdmin;

        var http = await InvokeAsync(Session(provider: Provider));

        Assert.Equal(ProviderRole.ProviderAdmin, CallerStanding.Of(http)!.ProviderRole);
        Assert.Equal(["provider-role"], _standing.Asked);
    }

    [Fact]
    public async Task Invoke_ProviderStaffRemoved_RefusedNotFound()
    {
        _standing.ProviderRole = null;

        await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(Session(provider: Provider)));
    }

    // A role the platform doesn't define would outrank Owner where roles are compared, and None holds nothing: both
    // are no membership.
    [Theory]
    [InlineData(OrganizationRole.None)]
    [InlineData((OrganizationRole)99)]
    [InlineData((OrganizationRole)(-1))]
    public async Task Invoke_MembershipInNoKnownRole_RefusedNotFound(OrganizationRole role)
    {
        _standing.Membership = MemberStanding.Of(role);

        await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(Session(Organization)));
    }

    [Theory]
    [InlineData(ProviderRole.None)]
    [InlineData((ProviderRole)99)]
    public async Task Invoke_ProviderStaffInNoKnownRole_RefusedNotFound(ProviderRole role)
    {
        _standing.ProviderRole = role;

        await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(Session(provider: Provider)));
    }

    [Theory]
    [InlineData(ProviderRole.None)]
    [InlineData((ProviderRole)99)]
    public async Task Invoke_ClientOrganizationStaffInNoKnownRole_Forbidden(ProviderRole role)
    {
        _standing.ProviderOf = Provider;
        _standing.ProviderRole = role;

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync(Session(Organization, Provider)));

        Assert.Equal(CurrentStandingCheck.NotProviderStaff, refused.Message);
    }

    [Fact]
    public async Task Invoke_PersonalSession_AsksNothing()
    {
        var http = await InvokeAsync(Session());

        Assert.Same(CallerStanding.Nothing, CallerStanding.Of(http));
        Assert.Empty(_standing.Asked);
    }

    // An organization credential, a machine account: nobody behind the request whose standing could change.
    [Fact]
    public async Task Invoke_NoPerson_AsksNothing()
    {
        var http = await InvokeAsync(Session(Organization, person: false));

        Assert.Null(CallerStanding.Of(http));
        Assert.Empty(_standing.Asked);
    }

    [Fact]
    public async Task Invoke_OperationOpenToAnybody_AsksNothing()
    {
        var http = await InvokeAsync(Session(Organization), metadata: [new AllowAnonymousAttribute()]);

        Assert.Null(CallerStanding.Of(http));
        Assert.Empty(_standing.Asked);
    }

    // No operation matched, so nothing is authorized and nothing needs a standing.
    [Fact]
    public async Task Invoke_NoEndpoint_AsksNothing()
    {
        var http = await InvokeAsync(Session(Organization), routed: false);

        Assert.Null(CallerStanding.Of(http));
        Assert.Empty(_standing.Asked);
    }

    // A client that goes away stops the reading too.
    [Fact]
    public async Task Invoke_PassesTheRequestsCancellation()
    {
        using var aborted = new CancellationTokenSource();

        await InvokeAsync(Session(Organization), aborted: aborted);

        Assert.Equal(aborted.Token, _standing.Cancellation);
    }

    private async Task<HttpContext> InvokeAsync(
        ClaimsPrincipal user,
        object[]? metadata = null,
        bool routed = true,
        CancellationTokenSource? aborted = null)
    {
        var services = new ServiceCollection().AddSingleton<ICurrentStanding>(_standing).BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = services, RequestAborted = aborted?.Token ?? default };

        if (routed)
        {
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata ?? []), "GetWidget"));
        }

        var reached = false;

        var check = new CurrentStandingCheck(_ => { reached = true; return Task.CompletedTask; }, services.GetRequiredService<IServiceProviderIsService>());
        await check.InvokeAsync(http);

        Assert.True(reached);
        return http;
    }

    private static ClaimsPrincipal Session(Guid? organization = null, Guid? provider = null, bool person = true)
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

        if (provider is not null)
        {
            claims.Add(new Claim(CallerRole.ProviderClaim, provider.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private sealed class Standing : ICurrentStanding
    {
        public MemberStanding? Membership { get; set; } = MemberStanding.Of(OrganizationRole.User);

        public Guid? ProviderOf { get; set; }

        public ProviderRole? ProviderRole { get; set; }

        public List<string> Asked { get; } = [];

        public CancellationToken Cancellation { get; private set; }

        public Task<MemberStanding?> MembershipAsync(Guid person, Guid organization, CancellationToken cancellationToken = default)
        {
            Asked.Add("membership");
            Cancellation = cancellationToken;
            Assert.Equal((Person, Organization), (person, organization));
            return Task.FromResult(Membership);
        }

        public Task<Guid?> ProviderOfAsync(Guid organization, CancellationToken cancellationToken = default)
        {
            Asked.Add("provider-of");
            Assert.Equal(Organization, organization);
            return Task.FromResult(ProviderOf);
        }

        public Task<ProviderRole?> ProviderRoleAsync(Guid person, Guid provider, CancellationToken cancellationToken = default)
        {
            Asked.Add("provider-role");
            Assert.Equal((Person, Provider), (person, provider));
            return Task.FromResult(ProviderRole);
        }
    }
}
