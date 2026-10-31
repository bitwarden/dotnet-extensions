using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// The probe surface: who am I, call downstream as me, and accept a body.
/// </summary>
public static class Probes
{
    /// <summary>
    /// The flag the <c>flagged</c> probe is held behind.
    /// </summary>
    public const string FlaggedProbeFlag = "probe-flag";

    /// <summary>
    /// The organization the creating probes create.
    /// </summary>
    public static readonly Guid CreatedOrganization = new("c0000000-0000-4000-8000-0000000000c1");

    /// <summary>
    /// Maps the probes.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var probes = endpoints.MapGroup("api/v1/probes").WithStandards();
        probes.MapGet("role", GetRoleProbe);
        probes.MapGet("provider-role", GetProviderRoleProbe);
        probes.MapGet("owners", GetOwnersProbe);
        probes.MapGet("sparse", GetSparseProbe);
        probes.MapGet("", ListProbes);
        probes.MapGet("current", GetProbe);
        probes.MapGet("system-about-person", GetSystemProbe);
        probes.MapGet("another-system-about-person", GetAnotherSystemProbe);
        probes.MapGet("flagged", GetFlaggedProbe);
        probes.MapGet("downstream", GetDownstreamProbe);
        probes.MapGet("credential", GetCredentialProbe);
        probes.MapGet("credential-owners", GetCredentialOwnersProbe);
        probes.MapGet("credential-person", GetCredentialPersonProbe);
        probes.MapGet("credential-downstream", GetCredentialDownstreamProbe);
        probes.MapPost("current/actions/named", NameOrganization);
        probes.MapPost("current/actions/named-provider", NameProvider);
        probes.MapGet("creates", GetCreatesProbe);
        probes.MapGet("creates-two", GetCreatesTwoProbe);
        probes.MapGet("creates-undeclared", GetCreatesUndeclaredProbe);
        probes.MapGet("send", GetSendProbe);
        probes.MapGet("any-scope", GetAnyScopeProbe);
        probes.MapPost("", CreateProbe);
        probes.MapPost("current/actions/mark", Mark);

        endpoints.MapGroup("api/v1/organizations").WithStandards().MapGet("{organizationId}/probe", GetNamedProbe);
    }

    /// <summary>
    /// Report the caller's role, in the note
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("role")]
    public static ResourceDocument<Probe> GetRoleProbe(IContext context, ICallerRole caller) =>
        ReadOneResponse(new Probe
        {
            Id = "role",
            ActingUser = context.UserId,
            Note = $"{caller.Role} {caller.Holds(OrganizationPermission.ManageUsers)}",
        });

    /// <summary>
    /// Report the caller's provider role, in the note
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("provider-role")]
    [Authorization(Provider = ContextRequirement.Optional)]
    public static ResourceDocument<Probe> GetProviderRoleProbe(IContext context, ICallerRole caller) =>
        ReadOneResponse(new Probe
        {
            Id = "provider-role",
            ActingUser = context.UserId,
            Note = caller.ProviderRole.ToString(),
        });

    /// <summary>
    /// Answer only an owner of the organization in context
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("owners")]
    [Authorization(OrganizationRole = OrganizationRole.Owner)]
    public static ResourceDocument<Probe> GetOwnersProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "owners", ActingUser = context.UserId });

    /// <summary>
    /// Report the acting user, with only the attributes asked for
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="fields">The attributes to return.</param>
    /// <returns>The probe</returns>
    [StandardReadOne("sparse")]
    public static ResourceDocument<Probe> GetSparseProbe(IContext context, Fields<Probe> fields = default) =>
        ReadOneResponse(Sparse(context, "sparse", fields), fields);

    /// <summary>
    /// List probes, with only the attributes asked for
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="fields">The attributes to return.</param>
    /// <param name="pageNumber">Which page.</param>
    /// <param name="pageSize">How many to a page.</param>
    /// <returns>Two probes</returns>
    [StandardReadMany]
    public static PagedResourceListDocument<Probe> ListProbes(
        IContext context,
        Fields<Probe> fields = default,
        PageNumber pageNumber = default,
        PageSize pageSize = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };
        Probe[] all = [Sparse(context, "one", fields), Sparse(context, "two", fields)];

        return ReadManyResponse(all.ToPaged(page), page, fields);
    }

    /// <summary>
    /// A probe that works out only what was asked for.
    /// </summary>
    private static Probe Sparse(IContext context, string id, Fields<Probe> fields) => new()
    {
        Id = id,
        ActingUser = context.UserId,
        Note = fields.Includes("note") ? "worked out" : null,
        Secret = "2.c|i|m",
        Costly = fields.Includes("costly") ? "worked out at a cost" : null,
    };

    /// <summary>
    /// Report the acting user
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("current")]
    public static ResourceDocument<Probe> GetProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "current", ActingUser = context.UserId });

    /// <summary>
    /// Report the acting user, for a service that calls it about somebody on its own authority
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("system-about-person")]
    [AcceptsAssertedContext("The test service's own calls about a person.", "some-service")]
    public static ResourceDocument<Probe> GetSystemProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "system-about-person", ActingUser = context.UserId });

    /// <summary>
    /// Report the acting user, for another service's calls about somebody
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("another-system-about-person")]
    [AcceptsAssertedContext("Another service's own calls about a person.", "another-service")]
    public static ResourceDocument<Probe> GetAnotherSystemProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "another-system-about-person", ActingUser = context.UserId });

    /// <summary>
    /// Report the acting user, only while <c>probe-flag</c> is on
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("flagged")]
    [Bitwarden.Server.Sdk.Features.RequireFeature(Probes.FlaggedProbeFlag)]
    public static ResourceDocument<Probe> GetFlaggedProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "flagged", ActingUser = context.UserId });

    /// <summary>
    /// Call downstream, then report the acting user
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe</returns>
    [StandardReadOne("downstream")]
    public static async Task<ResourceDocument<Probe>> GetDownstreamProbe(
        IContext context,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);

        return ReadOneResponse(new Probe { Id = "downstream", ActingUser = context.UserId });
    }

    /// <summary>
    /// Report the caller, accepting an organization credential
    /// </summary>
    /// <returns>The probe: the acting user, and the organization, role and permission in the note</returns>
    [StandardReadOne("credential")]
    [AcceptsOrganizationCredential(SessionHost.OrganizationCredentialScope)]
    public static ResourceDocument<Probe> GetCredentialProbe(IContext context, ICallerRole caller) =>
        ReadOneResponse(new Probe
        {
            Id = "credential",
            ActingUser = context.UserId,
            Note = $"{context.OrganizationId} {caller.Role} {caller.Holds(OrganizationPermission.ManageUsers)}",
        });

    /// <summary>
    /// Answer only an owner, accepting an organization credential
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("credential-owners")]
    [Authorization(OrganizationRole = OrganizationRole.Owner, Permission = OrganizationPermission.ManagePolicies)]
    [AcceptsOrganizationCredential(SessionHost.OrganizationCredentialScope)]
    public static ResourceDocument<Probe> GetCredentialOwnersProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "credential-owners", ActingUser = context.UserId });

    /// <summary>
    /// Demand a person, and accept an organization credential all the same
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("credential-person")]
    [Authorization(User = ContextRequirement.Required, Organization = ContextRequirement.Required)]
    [AcceptsOrganizationCredential(SessionHost.OrganizationCredentialScope)]
    public static ResourceDocument<Probe> GetCredentialPersonProbe(IContext context) =>
        ReadOneResponse(new Probe { Id = "credential-person", ActingUser = context.UserId });

    /// <summary>
    /// Call downstream, accepting an organization credential
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe</returns>
    [StandardReadOne("credential-downstream")]
    [AcceptsOrganizationCredential(SessionHost.OrganizationCredentialScope)]
    public static async Task<ResourceDocument<Probe>> GetCredentialDownstreamProbe(
        IContext context,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);

        return ReadOneResponse(new Probe { Id = "credential-downstream", ActingUser = context.UserId });
    }

    /// <summary>
    /// Name an organization the session wasn't issued for in the path, call downstream twice, report the role
    /// </summary>
    /// <param name="organizationId">The organization the request names.</param>
    /// <param name="context">The caller context.</param>
    /// <param name="caller">What the caller's session says they may do.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe: the acting user, and the context's organization, role and permission in the note</returns>
    [StandardReadOne("/api/v1/organizations/{organizationId}/probe")]
    [Authorization(Organization = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    [NamesOrganization]
    public static async Task<ResourceDocument<Probe>> GetNamedProbe(
        Guid organizationId,
        IContext context,
        ICallerRole caller,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
        await downstream.CallAsync(cancellationToken);

        return ReadOneResponse(new Probe
        {
            Id = organizationId.ToString(),
            ActingUser = context.UserId,
            Note = $"{context.OrganizationId} {caller.Role} {caller.Holds(OrganizationPermission.ManageUsers)}",
        });
    }

    /// <summary>
    /// Name an organization the session wasn't issued for in the body, and call downstream
    /// </summary>
    /// <param name="request">The organization the action is about.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [StandardAction("named", template: "current")]
    [Authorization(Organization = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    [NamesOrganization]
    public static async Task NameOrganization(NamedOrganization request, DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
    }

    /// <summary>
    /// Name a provider the session wasn't issued for in the body, and call downstream
    /// </summary>
    /// <param name="request">The provider the action is about.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [StandardAction("named-provider", template: "current")]
    [Authorization(Organization = ContextRequirement.Forbidden, Provider = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    [NamesProvider]
    public static async Task NameProvider(NamedProvider request, DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
    }

    /// <summary>
    /// Create an organization, then call downstream about it
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe</returns>
    [StandardReadOne("creates")]
    [Authorization(Organization = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    [CreatesOrganization]
    public static async Task<ResourceDocument<Probe>> GetCreatesProbe(
        IContext context,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        using (Bitwarden.Server.Sdk.Client.OutgoingCall.AboutCreatedOrganization(Probes.CreatedOrganization))
        {
            await downstream.CallAsync(cancellationToken);
        }

        return ReadOneResponse(new Probe { Id = "creates", ActingUser = context.UserId });
    }

    /// <summary>
    /// Create an organization, then call downstream about it and about another
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe, never: the second call is refused</returns>
    [StandardReadOne("creates-two")]
    [Authorization(Organization = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    [CreatesOrganization]
    public static async Task<ResourceDocument<Probe>> GetCreatesTwoProbe(
        IContext context,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        using (Bitwarden.Server.Sdk.Client.OutgoingCall.AboutCreatedOrganization(Probes.CreatedOrganization))
        {
            await downstream.CallAsync(cancellationToken);
        }

        using (Bitwarden.Server.Sdk.Client.OutgoingCall.AboutCreatedOrganization(Guid.NewGuid()))
        {
            await downstream.CallAsync(cancellationToken);
        }

        return ReadOneResponse(new Probe { Id = "creates-two", ActingUser = context.UserId });
    }

    /// <summary>
    /// Name an organization as created, on an operation that doesn't create one
    /// </summary>
    /// <param name="context">The caller context.</param>
    /// <param name="downstream">A domain service this one calls.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe, never: the call is refused</returns>
    [StandardReadOne("creates-undeclared")]
    [Authorization(Organization = ContextRequirement.Forbidden, User = ContextRequirement.Required)]
    public static async Task<ResourceDocument<Probe>> GetCreatesUndeclaredProbe(
        IContext context,
        DownstreamClient downstream,
        CancellationToken cancellationToken)
    {
        using (Bitwarden.Server.Sdk.Client.OutgoingCall.AboutCreatedOrganization(Probes.CreatedOrganization))
        {
            await downstream.CallAsync(cancellationToken);
        }

        return ReadOneResponse(new Probe { Id = "creates-undeclared", ActingUser = context.UserId });
    }

    /// <summary>
    /// Accept a Send credential and report the Send it names
    /// </summary>
    /// <returns>The probe: the acting user, and the Send in the note</returns>
    [StandardReadOne("send")]
    [Authorization(Scope = SessionHost.SendScope, User = ContextRequirement.Forbidden, Organization = ContextRequirement.Forbidden)]
    [AcceptsSendCredential]
    public static ResourceDocument<Probe> GetSendProbe(IContext context, ClaimsPrincipal user) =>
        ReadOneResponse(new Probe { Id = "send", ActingUser = context.UserId, Note = SendCredential.SendId(user) });

    /// <summary>
    /// Answer a caller holding either of two scopes
    /// </summary>
    /// <returns>The probe: the acting user, and in the note which of the two scopes the caller holds</returns>
    [StandardReadOne("any-scope")]
    [Authorization(User = ContextRequirement.Optional)]
    [AnyScope(SessionHost.Scope, SessionHost.OtherScope)]
    public static ResourceDocument<Probe> GetAnyScopeProbe(IContext context, ClaimsPrincipal user) =>
        ReadOneResponse(new Probe
        {
            Id = "any-scope",
            ActingUser = context.UserId,
            Note = $"{CallerScopes.Holds(user, SessionHost.Scope)} {CallerScopes.Holds(user, SessionHost.OtherScope)}",
        });

    /// <summary>
    /// Accept a probe
    /// </summary>
    /// <param name="request">The probe.</param>
    /// <param name="context">The caller context.</param>
    /// <returns>The probe as accepted</returns>
    [StandardCreate]
    public static ResourceDocument<Probe> CreateProbe(ResourceDocument<Probe> request, IContext context) =>
        CreateResponse(request.Data with { Id = Guid.NewGuid().ToString(), ActingUser = context.UserId });

    /// <summary>
    /// Mark the caller's own probe
    /// </summary>
    /// <remarks>
    /// An action on the singleton the caller's scope names, so it has nothing to return.
    /// </remarks>
    [StandardAction(template: "current")]
    public static void Mark()
    {
    }
}
