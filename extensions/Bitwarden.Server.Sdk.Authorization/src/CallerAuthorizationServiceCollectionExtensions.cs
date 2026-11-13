using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Authorization.CallChains;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers context authorization: who a request concerns, what an operation demands of its caller, and the
/// checks that refuse a caller who doesn't meet it.
/// </summary>
public static class CallerAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Adds context authorization, with a fallback policy that closes every operation that doesn't say
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it after the Bearer scheme is added (<c>AddBitwardenAuthentication</c>). Every registration but the
    /// handlers and the fallback policy is tried, so a service can replace one by registering its own first. Each
    /// handler is added once, however often this is called.
    /// </para>
    /// <para>
    /// The middleware it needs is added with <see cref="CallerAuthorizationApplicationBuilderExtensions"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="isProductService">
    /// Whether the service is a product service, which people call with their own session, rather than a domain
    /// service, which other services call.
    /// </param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddCallerAuthorization(this IServiceCollection services, bool isProductService)
    {
        services.AddHttpContextAccessor();
        // Singleton, not scoped. It holds nothing: it reads IHttpContextAccessor, which flows
        // on the execution context, so one instance answers correctly for every request. That
        // matters beyond allocation: a scoped instance captured by something longer-lived, such
        // as an Entity Framework model, would answer for whichever request built it.
        services.TryAddSingleton<IContext>(provider =>
            new CurrentContext(provider.GetRequiredService<IHttpContextAccessor>(), isProductService));
        // What this service's own calls exchange their signed context from (CallerProof); holds nothing.
        services.TryAddSingleton<ICallerProof>(provider => new CallerProof(
            provider.GetRequiredService<IHttpContextAccessor>(), provider.GetRequiredService<IContext>(), isProductService));

        // Whose word is taken for the address a request was forwarded from (ForwardedHeaders:*),
        // for the connection's address every service sees, and a product service records.
        services.TryAddSingleton<TrustedProxies>();

        // A token is accepted only where it is addressed: at a product service, a person's
        // token exchanged for that product; at a domain service, a service's token issued for it.
        // Always on, so no service can forget it.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, ServiceAudience>());

        // Where a person's session ends, the session must name the person. A token that does not
        // is refused at authentication, before anything could treat it as acting for nobody.
        if (isProductService)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, PersonSessionTokens>());

            // The organization or provider an operation names in its path or body, where the
            // session names none ([NamesOrganization], [NamesProvider]): carried on its outgoing
            // calls, never into this service's own context. Holds nothing, as IContext.
            services.TryAddSingleton<IRequestNamedOwner, RequestNamedOwner>();
        }

        // It reads the claims off IHttpContextAccessor and holds nothing. Registered for every
        // service even though only a product service has a user session to read: below the
        // product layer it answers None, which is correct, and no domain service asks.
        services.TryAddSingleton<ICallerRole, CallerRole>();
        // Scoped, not singleton: it reads the context of the request being authorized.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, ContextRequirementsHandler>());
        // An operation reachable with any one of several scopes ([AnyScope]); holds nothing.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, AnyScopeHandler>());
        // Which organization an operation creating one may name on its calls ([CreatesOrganization]).
        services.TryAddScoped<IOrganizationCreation, OrganizationCreation>();
        services.TryAddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();

        // Every operation is authenticated unless it says otherwise. The fallback policy is what
        // makes that true by default rather than by each author remembering, so a new endpoint is
        // closed until someone opens it.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddRequirements(new ContextRequirements(new AuthorizationAttribute()))
                .Build());

        // Whether a call back into this service while its own request waits is refused (CallChain:RefuseReentry), and
        // the count of such calls (CallChains/CallChainCheck.cs).
        services.AddOptions<CallChainOptions>().BindConfiguration(CallChainOptions.SectionName);
        services.TryAddSingleton<CallChainMetrics>();

        return services;
    }
}
