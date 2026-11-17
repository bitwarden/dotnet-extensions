using System.Security.Claims;
using Bitwarden.Server.Sdk.Authorization.CallChains;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// What context authorization registers for a product service and a domain service, that a service can replace what is
/// tried, and that the fallback policy closes an operation that says nothing to anybody who is not authenticated.
/// </summary>
public sealed class CallerAuthorizationServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddCallerAuthorization_RegistersWhatEveryServiceNeeds(bool isProductService)
    {
        var services = new ServiceCollection().AddCallerAuthorization(isProductService);

        Assert.Equal(ServiceLifetime.Singleton, Single<IContext>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Single<ICallerProof>(services).Lifetime);
        Assert.Equal(typeof(TrustedProxies), Single<TrustedProxies>(services).ImplementationType);
        Assert.Equal(typeof(CallerRole), Single<ICallerRole>(services).ImplementationType);
        Assert.Equal(typeof(CallChainMetrics), Single<CallChainMetrics>(services).ImplementationType);
        Assert.Contains(services, d => d.ServiceType == typeof(IHttpContextAccessor));

        // Scoped: each reads the context of the request being authorized. The framework adds its own beside them.
        var handlers = services
            .Where(d => d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType?.Assembly == typeof(AnyScopeHandler).Assembly)
            .ToList();
        Assert.Equal([typeof(ContextRequirementsHandler), typeof(AnyScopeHandler)], handlers.Select(d => d.ImplementationType));
        Assert.All(handlers, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
        var creation = Single<IOrganizationCreation>(services);
        Assert.Equal(typeof(OrganizationCreation), creation.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, creation.Lifetime);
        Assert.Equal(typeof(AuthorizationResultHandler), Single<IAuthorizationMiddlewareResultHandler>(services).ImplementationType);
    }

    // A token is accepted only where it is addressed, always; only a product service's must name a person.
    [Theory]
    [InlineData(true, new[] { typeof(ServiceAudience), typeof(PersonSessionTokens) })]
    [InlineData(false, new[] { typeof(ServiceAudience) })]
    public void AddCallerAuthorization_TokenChecks_ForTheKindOfService(bool isProductService, Type[] expected)
    {
        var services = new ServiceCollection().AddCallerAuthorization(isProductService);

        Assert.Equal(
            expected,
            services.Where(d => d.ServiceType == typeof(IPostConfigureOptions<JwtBearerOptions>)).Select(d => d.ImplementationType));
    }

    // Only a product service has a request that names an owner its session was not issued for.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddCallerAuthorization_RequestNamedOwner_ProductServiceOnly(bool isProductService)
    {
        var services = new ServiceCollection().AddCallerAuthorization(isProductService);

        Assert.Equal(
            isProductService ? [typeof(RequestNamedOwner)] : Array.Empty<Type>(),
            services.Where(d => d.ServiceType == typeof(IRequestNamedOwner)).Select(d => d.ImplementationType));
    }

    // A product service's context is its person's session; a domain service's is its caller's signed word, never a
    // session's subject.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddCallerAuthorization_TheContext_ReadsAsTheKindOfService(bool isProductService)
    {
        var person = Guid.NewGuid();
        using var provider = new ServiceCollection().AddCallerAuthorization(isProductService).BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CurrentContext.SubjectClaim, person.ToString())], "Bearer")),
        };

        var context = provider.GetRequiredService<IContext>();

        Assert.IsType<CurrentContext>(context);
        Assert.Same(context, provider.GetRequiredService<IContext>());
        Assert.Equal(isProductService ? person : null, context.UserId);
    }

    // Every registration but the handlers and the fallback policy is tried, so a service's own comes first and stays.
    [Fact]
    public void AddCallerAuthorization_AServicesOwnRegistrations_AreKept()
    {
        var context = new StatedContext();
        var role = new FixedRole();
        var services = new ServiceCollection()
            .AddSingleton<IContext>(context)
            .AddSingleton<ICallerRole>(role)
            .AddCallerAuthorization(isProductService: true);

        Assert.Same(context, Single<IContext>(services).ImplementationInstance);
        Assert.Same(role, Single<ICallerRole>(services).ImplementationInstance);
    }

    // Every registration but the handlers and the fallback policy is tried, the organization creation and the result
    // handler included, so a service that registers its own first keeps it.
    [Fact]
    public void AddCallerAuthorization_AServicesOwnCreationAndResultHandler_AreKept()
    {
        using var provider = new ServiceCollection()
            .AddScoped<IOrganizationCreation, NoCreation>()
            .AddSingleton<IAuthorizationMiddlewareResultHandler, PassingResultHandler>()
            .AddCallerAuthorization(isProductService: true)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<NoCreation>(scope.ServiceProvider.GetRequiredService<IOrganizationCreation>());
        Assert.IsType<PassingResultHandler>(provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>());
    }

    // Added twice, the token checks and the authorization handlers are not doubled.
    [Fact]
    public void AddCallerAuthorization_Twice_AddsEachTokenCheckOnce()
    {
        var services = new ServiceCollection()
            .AddCallerAuthorization(isProductService: true)
            .AddCallerAuthorization(isProductService: true);

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IPostConfigureOptions<JwtBearerOptions>)));
        Assert.Single(services, d => d.ServiceType == typeof(IContext));
        Assert.Single(services, d => d.ImplementationType == typeof(ContextRequirementsHandler));
        Assert.Single(services, d => d.ImplementationType == typeof(AnyScopeHandler));
    }

    [Fact]
    public void AddCallerAuthorization_TheFallbackPolicy_IsTheDefaultContextRequirement()
    {
        using var provider = new ServiceCollection().AddCallerAuthorization(isProductService: false).BuildServiceProvider();

        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        Assert.NotNull(fallback);
        var requirement = Assert.IsType<ContextRequirements>(Assert.Single(fallback.Requirements));
        Assert.False(requirement.Options.Anonymous);
    }

    // A new endpoint is closed until someone opens it: the fallback refuses a caller who is not authenticated, and
    // admits one who is.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task AddCallerAuthorization_TheFallbackPolicy_AdmitsOnlyAnAuthenticatedCaller(bool authenticated, bool admitted)
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IServiceIdentity>(new Identity("widget-service"))
            .AddCallerAuthorization(isProductService: false)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy!;
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(InternalCaller.ClientClaim, "internal.gadget-service")], authenticated ? "Bearer" : null));

        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(caller, null, fallback);

        Assert.Equal(admitted, result.Succeeded);
    }

    [Fact]
    public void AddCallerAuthorization_RefuseReentry_IsBoundFromTheCallChainSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CallChain:RefuseReentry"] = "true" })
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddCallerAuthorization(isProductService: false)
            .BuildServiceProvider();

        Assert.True(provider.GetRequiredService<IOptionsMonitor<CallChainOptions>>().CurrentValue.RefuseReentry);
    }

    private static ServiceDescriptor Single<T>(IServiceCollection services) =>
        Assert.Single(services, d => d.ServiceType == typeof(T));

    private sealed record Identity(string Name) : IServiceIdentity
    {
        public string Title => Name;
    }

    private sealed class StatedContext : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => null;
    }

    private sealed class NoCreation : IOrganizationCreation
    {
        public bool Allows(Guid organization) => false;
    }

    private sealed class PassingResultHandler : IAuthorizationMiddlewareResultHandler
    {
        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult) =>
            next(context);
    }

    private sealed class FixedRole : ICallerRole
    {
        public OrganizationRole Role => OrganizationRole.None;

        public ProviderRole ProviderRole => ProviderRole.None;

        public bool Holds(OrganizationPermission permission) => false;
    }
}
