using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Decides whether a caller satisfies an operation's settled <see cref="AuthorizationAttribute"/>: its scope, each
/// context member, the caller's roles and permission, in that order, each refusal saying what was missing.
/// </summary>
public sealed class ContextRequirementsHandlerTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");

    /// <summary>
    /// What an operation demanding nothing beyond a token asks, so each test says only what it is about.
    /// </summary>
    private static AuthorizationAttribute Demands() => new()
    {
        Organization = ContextRequirement.Optional,
        User = ContextRequirement.Optional,
        Provider = ContextRequirement.Optional,
        Settled = true,
    };

    private static ClaimsPrincipal Caller(params string[] scopes) =>
        new(new ClaimsIdentity(scopes.Select(scope => new Claim("scope", scope)), "Bearer"));

    private static async Task<AuthorizationHandlerContext> Handle(
        AuthorizationAttribute options,
        ClaimsPrincipal? caller = null,
        StatedContext? context = null,
        Role? role = null,
        bool productService = false,
        object? resource = null)
    {
        var requirement = new ContextRequirements(options);
        var authorization = new AuthorizationHandlerContext([requirement], caller ?? Caller(), resource);
        await new ContextRequirementsHandler(context ?? new StatedContext(), role ?? new Role(), new Identity(productService))
            .HandleAsync(authorization);
        return authorization;
    }

    private static string Refusal(AuthorizationHandlerContext authorization)
    {
        Assert.True(authorization.HasFailed);
        return Assert.Single(authorization.FailureReasons).Message;
    }

    [Fact]
    public async Task Handle_DemandsMet_Succeeds()
    {
        var authorization = await Handle(Demands());

        Assert.True(authorization.HasSucceeded);
    }

    // An anonymous operation admits anybody, even with no token and a context it would otherwise refuse.
    [Fact]
    public async Task Handle_Anonymous_SucceedsWithoutAToken()
    {
        var options = Demands();
        options.Anonymous = true;
        options.Scope = "vault.read";
        options.Organization = ContextRequirement.Required;

        var authorization = await Handle(options, new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(authorization.HasSucceeded);
    }

    // Left unsucceeded and unfailed, so the caller is challenged with a 401 rather than forbidden.
    [Fact]
    public async Task Handle_Unauthenticated_NeitherSucceedsNorFails()
    {
        var authorization = await Handle(Demands(), new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "vault.read")])));

        Assert.False(authorization.HasSucceeded);
        Assert.False(authorization.HasFailed);
    }

    [Theory]
    [InlineData("vault.read", true)]
    [InlineData("vault.write", false)]
    [InlineData("Vault.Read", false)]
    public async Task Handle_Scope_MustBeHeldExactly(string held, bool admitted)
    {
        var options = Demands();
        options.Scope = "vault.read";

        var authorization = await Handle(options, Caller(held));

        Assert.Equal(admitted, authorization.HasSucceeded);
        if (!admitted)
        {
            Assert.Equal("The 'vault.read' scope is required.", Refusal(authorization));
        }
    }

    // An empty scope is what a service that named no default scope settles: it demands none.
    [Fact]
    public async Task Handle_EmptyScope_DemandsNone()
    {
        var options = Demands();
        options.Scope = string.Empty;

        var authorization = await Handle(options, Caller());

        Assert.True(authorization.HasSucceeded);
    }

    // Below the product layer, each refusal names the header the calling service sent or didn't.
    [Theory]
    [InlineData("organization", ContextRequirement.Required, false, $"The '{BitwardenHeaders.Organization}' header is required.")]
    [InlineData("organization", ContextRequirement.Forbidden, true, $"The '{BitwardenHeaders.Organization}' header is not accepted by this operation.")]
    [InlineData("user", ContextRequirement.Required, false, $"The '{BitwardenHeaders.User}' header is required.")]
    [InlineData("user", ContextRequirement.Forbidden, true, $"The '{BitwardenHeaders.User}' header is not accepted by this operation.")]
    [InlineData("provider", ContextRequirement.Required, false, $"The '{BitwardenHeaders.Provider}' header is required.")]
    [InlineData("provider", ContextRequirement.Forbidden, true, $"The '{BitwardenHeaders.Provider}' header is not accepted by this operation.")]
    public async Task Handle_DomainService_ContextRefusalNamesTheHeader(
        string member, ContextRequirement requirement, bool present, string expected)
    {
        var (options, context) = Context(member, requirement, present);

        var authorization = await Handle(options, context: context);

        Assert.Equal(expected, Refusal(authorization));
    }

    // In a product service the context is the session's, so each refusal says what the session names.
    [Theory]
    [InlineData("organization", ContextRequirement.Required, false, "The session must name an organization.")]
    [InlineData("organization", ContextRequirement.Forbidden, true, "This operation does not accept a session that names an organization.")]
    [InlineData("user", ContextRequirement.Required, false, "The session must name a person.")]
    [InlineData("user", ContextRequirement.Forbidden, true, "This operation does not accept a session that names a person.")]
    [InlineData("provider", ContextRequirement.Required, false, "The session must name a provider.")]
    [InlineData("provider", ContextRequirement.Forbidden, true, "This operation does not accept a session that names a provider.")]
    public async Task Handle_ProductService_ContextRefusalNamesTheSession(
        string member, ContextRequirement requirement, bool present, string expected)
    {
        var (options, context) = Context(member, requirement, present);

        var authorization = await Handle(options, context: context, productService: true);

        Assert.Equal(expected, Refusal(authorization));
    }

    // The other way round each is met, and an optional member is met either way.
    [Theory]
    [InlineData("organization", ContextRequirement.Required, true)]
    [InlineData("organization", ContextRequirement.Forbidden, false)]
    [InlineData("organization", ContextRequirement.Optional, true)]
    [InlineData("organization", ContextRequirement.Optional, false)]
    [InlineData("user", ContextRequirement.Required, true)]
    [InlineData("user", ContextRequirement.Forbidden, false)]
    [InlineData("provider", ContextRequirement.Required, true)]
    [InlineData("provider", ContextRequirement.Forbidden, false)]
    public async Task Handle_ContextRequirementMet_Succeeds(string member, ContextRequirement requirement, bool present)
    {
        var (options, context) = Context(member, requirement, present);

        var authorization = await Handle(options, context: context);

        Assert.True(authorization.HasSucceeded);
    }

    // At least the role named: the role itself or any above it.
    [Theory]
    [InlineData(OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.User, false)]
    [InlineData(OrganizationRole.None, false)]
    public async Task Handle_OrganizationRole_AtLeastTheOneNamed(OrganizationRole held, bool admitted)
    {
        var options = Demands();
        options.OrganizationRole = OrganizationRole.Admin;

        var authorization = await Handle(options, role: new Role(held));

        Assert.Equal(admitted, authorization.HasSucceeded);
        if (!admitted)
        {
            Assert.Equal("The caller must have at least the Admin role in the organization.", Refusal(authorization));
        }
    }

    [Theory]
    [InlineData(ProviderRole.ProviderAdmin, true)]
    [InlineData(ProviderRole.ServiceUser, true)]
    [InlineData(ProviderRole.None, false)]
    public async Task Handle_ProviderRole_AtLeastTheOneNamed(ProviderRole held, bool admitted)
    {
        var options = Demands();
        options.ProviderRole = ProviderRole.ServiceUser;

        var authorization = await Handle(options, role: new Role(provider: held));

        Assert.Equal(admitted, authorization.HasSucceeded);
        if (!admitted)
        {
            Assert.Equal("The caller must have at least the ServiceUser role in the provider.", Refusal(authorization));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_Permission_MustBeHeld(bool held)
    {
        var options = Demands();
        options.Permission = OrganizationPermission.ManageUsers;

        var authorization = await Handle(options, role: new Role(granted: held ? OrganizationPermission.ManageUsers : null));

        Assert.Equal(held, authorization.HasSucceeded);
        if (!held)
        {
            Assert.Equal("The caller must hold the ManageUsers permission.", Refusal(authorization));
        }
    }

    // Checked in order: a caller missing the scope is told about the scope, not the context it also lacks.
    [Fact]
    public async Task Handle_SeveralUnmet_RefusesForTheFirst()
    {
        var options = Demands();
        options.Scope = "vault.read";
        options.Organization = ContextRequirement.Required;
        options.OrganizationRole = OrganizationRole.Owner;

        var authorization = await Handle(options, Caller("vault.write"));

        Assert.Equal("The 'vault.read' scope is required.", Refusal(authorization));
    }

    // --- An organization credential, at a product service operation that accepts one -------------------------------

    // The scope is the declared one in place of the operation's, and no person is asked for.
    [Fact]
    public async Task Handle_OrganizationCredential_HoldsTheDeclaredScopeAndNeedsNoPerson()
    {
        var options = Demands();
        options.Scope = "vault.read";
        options.User = ContextRequirement.Required;
        var (credential, http) = OrganizationCredential("api.organization", "api.organization");

        var authorization = await Handle(
            options, credential, new StatedContext(organization: Organization), productService: true, resource: http);

        Assert.True(authorization.HasSucceeded);
    }

    // A person's scope doesn't stand in for the declared one.
    [Fact]
    public async Task Handle_OrganizationCredential_WithoutTheDeclaredScope_Fails()
    {
        var options = Demands();
        options.Scope = "vault.read";
        var (credential, http) = OrganizationCredential("api.organization", "vault.read");

        var authorization = await Handle(
            options, credential, new StatedContext(organization: Organization), productService: true, resource: http);

        Assert.Equal("The 'api.organization' scope is required.", Refusal(authorization));
    }

    // Below the product layer there is no organization credential: the operation's own demands apply.
    [Fact]
    public async Task Handle_OrganizationCredential_AtADomainService_IsNotRecognised()
    {
        var options = Demands();
        options.Scope = "vault.read";
        var (credential, http) = OrganizationCredential("api.organization", "api.organization");

        var authorization = await Handle(
            options, credential, new StatedContext(organization: Organization), productService: false, resource: http);

        Assert.Equal("The 'vault.read' scope is required.", Refusal(authorization));
    }

    // A request that is not the HTTP context (no endpoint to read a declaration from) has no organization credential.
    [Fact]
    public async Task Handle_OrganizationCredential_WithoutTheRequestAsResource_IsNotRecognised()
    {
        var options = Demands();
        options.Scope = "vault.read";
        var (credential, _) = OrganizationCredential("api.organization", "api.organization");

        var authorization = await Handle(
            options, credential, new StatedContext(organization: Organization), productService: true, resource: null);

        Assert.Equal("The 'vault.read' scope is required.", Refusal(authorization));
    }

    private static (AuthorizationAttribute Options, StatedContext Context) Context(
        string member, ContextRequirement requirement, bool present)
    {
        var options = Demands();
        var named = present ? Guid.NewGuid() : (Guid?)null;

        switch (member)
        {
            case "organization":
                options.Organization = requirement;
                return (options, new StatedContext(organization: named));
            case "user":
                options.User = requirement;
                return (options, new StatedContext(user: named));
            default:
                options.Provider = requirement;
                return (options, new StatedContext(provider: named));
        }
    }

    /// <summary>
    /// An organization's API key token (an organization, no person) calling an operation that accepts one.
    /// </summary>
    private static (ClaimsPrincipal Caller, HttpContext Http) OrganizationCredential(string declared, string held)
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("organization", Organization.ToString()), new Claim("scope", held)], "Bearer"));
        var http = new DefaultHttpContext { User = caller };
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AcceptsOrganizationCredentialAttribute(declared)),
            "operation"));
        return (caller, http);
    }

    /// <summary>
    /// What a request names.
    /// </summary>
    private sealed class StatedContext(Guid? organization = null, Guid? user = null, Guid? provider = null) : IContext
    {
        public Guid? OrganizationId => organization;

        public Guid? UserId => user;

        public Guid? ProviderId => provider;
    }

    /// <summary>
    /// What the caller's session lets them do.
    /// </summary>
    private sealed class Role(
        OrganizationRole organization = OrganizationRole.None,
        ProviderRole provider = ProviderRole.None,
        OrganizationPermission? granted = null) : ICallerRole
    {
        OrganizationRole ICallerRole.Role => organization;

        ProviderRole ICallerRole.ProviderRole => provider;

        public bool Holds(OrganizationPermission permission) =>
            permission == OrganizationPermission.None || permission == granted;
    }

    private sealed class Identity(bool productService) : IServiceIdentity
    {
        public string Name => "test-service";

        public string Title => "Test Service";

        public bool IsProductService => productService;
    }
}
