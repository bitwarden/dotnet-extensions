using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Http;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The base class every Bitwarden service derives from.
/// </summary>
/// <remarks>
/// <para>
/// This type inverts the usual ASP.NET arrangement. Rather than a service assembling its own host
/// and calling into the platform where it chooses, the platform owns the whole startup sequence and
/// calls into the service at defined points. A service author passes a name to the constructor,
/// maps the service's endpoints in <see cref="MapEndpoints"/>, optionally overrides the other hooks, and is
/// otherwise out of the business of ordering startup.
/// </para>
/// <para>
/// The motivation is that the order of both dependency registration and middleware is
/// security-relevant and easy to get subtly wrong: authentication before authorization, exception
/// handling outside everything it must not leak through. Ordering that every service has to
/// rediscover is ordering that some service will eventually get wrong. Here it is fixed in
/// <see cref="Build"/> and cannot be reordered by a derived type, only extended at the seams.
/// </para>
/// <para>
/// Every overridable member documents precisely what the base implementation does, so an author
/// overriding one knows what they are replacing and whether they need to call
/// <see langword="base" />.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class WidgetService(string[] args)
///     : Service("widget-service", "Widget Service", args)
/// {
///     protected override void ConfigureServices(IServiceCollection services)
///         => services.AddWidgetOperations();
/// }
/// </code>
/// </example>
public abstract class Service
{
    /// <param name="name">
    /// The service's identifier, slug-like and unique across Bitwarden, for example
    /// <c>"widget-service"</c>. This is the name code uses: it appears in configuration
    /// keys, in the <c>internal.{name}</c> credential the service authenticates with, and in
    /// telemetry. Lowercase, digits and hyphens only, so it is safe everywhere those are used.
    /// </param>
    /// <param name="title">
    /// The service's name in human-readable form, for example <c>"Widget Service"</c>.
    /// This is the name people read: the title of the API documentation and of its OpenAPI
    /// document. Kept separate from <paramref name="name"/> because prose makes a poor
    /// identifier and an identifier makes poor prose.
    /// </param>
    /// <param name="args">
    /// The process command line, passed through to <see cref="WebApplication.CreateBuilder(string[])"/>
    /// so that the standard configuration sources (including command line overrides) behave as an
    /// operator expects.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="title"/> is null, empty, or whitespace, or
    /// <paramref name="name"/> is not slug-like.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    protected Service(string name, string title, string[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(args);

        // Checked rather than trusted. The two arguments are both strings and adjacent, so
        // passing them the wrong way round is the obvious mistake, and it would otherwise
        // surface far away, as a credential that does not resolve or a configuration key nobody
        // set.
        if (!SlugPattern.IsMatch(name))
        {
            throw new ArgumentException(
                $"A service name must be lowercase letters, digits and hyphens; '{name}' is not. "
                + "The human-readable form belongs in the title.",
                nameof(name));
        }

        Name = name;
        Title = title;
        Args = args;

        // From the concrete type and its bases: see ProductServiceAttribute for why it is
        // inherited.
        _isProductService = GetType().IsDefined(typeof(ProductServiceAttribute), inherit: true);
        _isProductDomainService = GetType().IsDefined(typeof(ProductDomainServiceAttribute), inherit: true);
        if (_isProductService && _isProductDomainService)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} is marked both [ProductService] and [ProductDomainService]; a service is one or the other.");
        }
    }

    /// <summary>
    /// Whether the concrete type carries <see cref="ProductServiceAttribute"/>.
    /// </summary>
    private readonly bool _isProductService;

    /// <summary>
    /// Whether the concrete type carries <see cref="ProductDomainServiceAttribute"/>.
    /// </summary>
    private readonly bool _isProductDomainService;

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// The process command line this service was started with.
    /// </summary>
    protected string[] Args { get; }

    /// <summary>
    /// The service's identifier, as supplied to the constructor.
    /// </summary>
    protected string Name { get; }

    /// <summary>
    /// The service's human-readable name, as supplied to the constructor.
    /// </summary>
    protected string Title { get; }

    /// <summary>
    /// The name of the service's OpenAPI document.
    /// </summary>
    private const string DocumentName = "v1";

    /// <summary>
    /// Where the OpenAPI document is served.
    /// </summary>
    private const string DocumentPath = "/api/openapi.json";

    /// <summary>
    /// Where the API documentation UI is mounted.
    /// </summary>
    private const string DocumentationRoutePrefix = "docs";

    /// <summary>
    /// The documentation UI's page, where the service's bare address redirects.
    /// </summary>
    private const string DocumentationIndexPath = $"/{DocumentationRoutePrefix}/index.html";

    /// <summary>
    /// Where Identity is reached by a browser, for the documentation UI's token request. Optional.
    /// </summary>
    internal const string PublicIdentityUrlKey = "Service:PublicIdentityUrl";

    /// <summary>
    /// The level of ASP.NET's request lines, which carry the query string: <c>Warning</c> unless a
    /// service or deployment sets it (see <see cref="Identify"/>).
    /// </summary>
    internal const string RequestLineLogLevelKey = "Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>
    /// The browser origins admitted, read in <see cref="AddPlatformServices"/>; empty when the
    /// service admits none, in which case it has no CORS middleware at all.
    /// </summary>
    private IReadOnlyList<string> _browserOrigins = [];

    /// <summary>
    /// Runs the full startup sequence and returns the configured, un-started application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence is fixed and runs in this order:
    /// </para>
    /// <list type="number">
    ///   <item><description><see cref="CreateBuilder"/>: construct the host builder.</description></item>
    ///   <item><description><see cref="ConfigureConfiguration"/>: add configuration sources.</description></item>
    ///   <item><description>Platform service registration: see <see cref="AddPlatformServices"/>.</description></item>
    ///   <item><description><see cref="ConfigureServices"/>: the service's own registrations.</description></item>
    ///   <item><description>Platform extensions: every <see cref="IServiceExtension"/> a referenced package declares.</description></item>
    ///   <item><description><c>builder.Build()</c>: the container is now fixed; nothing further may be registered.</description></item>
    ///   <item><description><see cref="OnConfiguringPipeline"/>: middleware that must sit outside the platform's.</description></item>
    ///   <item><description>Platform middleware: see <see cref="UsePlatformMiddleware"/>.</description></item>
    ///   <item><description><see cref="OnPipelineConfigured"/>: middleware that must sit inside the platform's, before endpoints.</description></item>
    ///   <item><description><see cref="MapPlatformEndpoints"/>: endpoints every service exposes.</description></item>
    ///   <item><description><see cref="MapEndpoints"/>: the service's HTTP surface.</description></item>
    /// </list>
    /// <para>
    /// Returning the <see cref="WebApplication"/> rather than running it is deliberate: tests and
    /// hosting harnesses need a built-but-not-started application. Call <see cref="Run"/> instead
    /// when you simply want the service to start.
    /// </para>
    /// </remarks>
    /// <returns>A fully configured <see cref="WebApplication"/> that has not yet been started.</returns>
    public WebApplication Build()
    {
        var builder = CreateBuilder();

        Identify(builder.Configuration);
        // Ahead of the service's own sources, so those still win over the self-hosted settings.
        SelfHosting.LayerSettings(builder.Configuration, builder.Environment);
        ConfigureConfiguration(builder.Configuration);
        AddPlatformServices(builder);

        // A process started only to describe itself still has to start, so that endpoints mapped
        // directly reach the document, but it must not also do the service's work.
        var describing = OpenApiExport.Requested(Args, out _);
        var platformWork = describing ? OpenApiExport.HostedServices(builder.Services) : [];

        ConfigureServices(builder.Services);

        // After the service's own registrations, so an extension can see what the service
        // registered (the outbox needs to know which databases there are), and before isolation,
        // so whatever an extension starts in the background is stopped from starting when the
        // process is only describing itself.
        ServiceExtensions.Apply(GetType().Assembly, builder.Services, builder.Configuration);

        if (describing)
        {
            OpenApiExport.Isolate(builder, platformWork);
        }

        var app = builder.Build();
        app.Logger.LogInformation("Starting {Service}.", Name);

        OnConfiguringPipeline(app);
        UsePlatformMiddleware(app);
        OnPipelineConfigured(app);
        MapPlatformEndpoints(app);
        MapEndpoints(app);

        // Every endpoint is mapped now. Reading them builds them, so a minimal-API endpoint that
        // breaks a standard stops the service here, with everything wrong listed at once.
        Endpoints.StandardEndpointCheck.Require(app);

        return app;
    }

    /// <summary>
    /// Builds the service and runs it, blocking until the host shuts down.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>Build().Run()</c>. This is the normal entry point from <c>Program</c>.
    /// Given <c>--emit-openapi &lt;path&gt;</c> it writes the service's contract to that path and
    /// returns instead of serving, which is how the committed document is refreshed.
    /// </remarks>
    public void Run()
    {
        var app = Build();

        // Asked for the contract rather than the service. Answered from the built application
        // without starting it, so nothing the service depends on at runtime has to be reachable
        // to describe what it serves.
        if (OpenApiExport.Requested(Args, out var path))
        {
            OpenApiExport.WriteAsync(app, DocumentName, path, ApiDocumentation.SpecVersion).GetAwaiter().GetResult();

            // The resolved path, not the one asked for. "dotnet run --project" runs the service
            // from the project's own directory, so a relative path lands somewhere other than
            // where the person who typed it is standing, and a spec that is committed to be
            // diffed is worth nothing if it is quietly written elsewhere and never updated.
            app.Logger.LogInformation("Wrote the OpenAPI document to {Path}.", Path.GetFullPath(path));
            return;
        }

        app.Run();
    }

    /// <summary>
    /// Tells the client packages what this service is called, so it authenticates as itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A service's outbound credential is <c>internal.{name}</c>, and without this the name is
    /// guessed from the entry assembly, so <c>Bitwarden.Widget.Service</c> would ask Identity for
    /// a token as <c>internal.widget</c> while every other part of the platform, from the
    /// configuration key to service discovery, knows it as <c>widget-service</c>. One service,
    /// two names, and the mismatch only shows up as a refused credential at runtime.
    /// </para>
    /// <para>
    /// Inserted ahead of every other source rather than appended, so it is a default: a
    /// deployment that sets <c>Bitwarden:ServiceName</c> still wins.
    /// </para>
    /// </remarks>
    private void Identify(IConfigurationManager configuration)
    {
        ((IConfigurationBuilder)configuration).Sources.Insert(
            0,
            new MemoryConfigurationSource
            {
                InitialData =
                [
                    new KeyValuePair<string, string?>("Bitwarden:ServiceName", Name),

                    // The scope a request runs in is written by ContextLogScope, and a logger
                    // that does not emit scopes drops it on the floor. Defaulted rather than
                    // forced: it is inserted ahead of every other source, so a deployment that
                    // turns it off still wins.
                    new KeyValuePair<string, string?>("Logging:Console:IncludeScopes", "true"),

                    // ASP.NET's request lines ("Request starting ...", "Request finished ...") carry
                    // the query string, and a query string can carry a credential: a sign-in link's
                    // token, a download link's token. This keeps the one category that writes the URL
                    // at Warning for every service, so none can be deployed without it. Defaulted, as
                    // above: a service or deployment that wants request lines sets the category itself.
                    new KeyValuePair<string, string?>(RequestLineLogLevelKey, "Warning"),
                ]
            });
    }

    /// <summary>
    /// Creates the <see cref="WebApplicationBuilder"/> the rest of startup builds on.
    /// </summary>
    /// <remarks>
    /// The base implementation calls <see cref="WebApplication.CreateBuilder(string[])"/> with
    /// <see cref="Args"/>, which installs the standard configuration sources (appsettings.json,
    /// environment-specific appsettings, user secrets in Development, environment variables, command
    /// line) and the default logging providers. Override only to substitute a differently-configured
    /// builder, for example a slim or empty builder. Most services should not need to.
    /// </remarks>
    protected virtual WebApplicationBuilder CreateBuilder() => WebApplication.CreateBuilder(Args);

    /// <summary>
    /// Adds configuration sources beyond the defaults installed by <see cref="CreateBuilder"/>.
    /// </summary>
    /// <remarks>
    /// The base implementation does nothing. Sources added here are appended, so they take
    /// precedence over the defaults. This runs before any service registration, so registrations in
    /// <see cref="ConfigureServices"/> may read configuration added here.
    /// </remarks>
    /// <param name="configuration">The builder's configuration, open for both reading and adding sources.</param>
    protected virtual void ConfigureConfiguration(IConfigurationManager configuration)
    {
    }

    /// <summary>
    /// Registers the services every Bitwarden service is expected to have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base implementation registers, in this order:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     The exception handler, which answers any exception reaching the pipeline with the standard
    ///     error document, and <c>AddProblemDetails()</c> beneath it, which
    ///     <c>UseExceptionHandler()</c> requires before it builds. The handler answers first.
    ///   </description></item>
    ///   <item><description>
    ///     Health checks (<c>AddBitwardenHealthChecks()</c>), which the system endpoints report.
    ///   </description></item>
    ///   <item><description>
    ///     Authentication: bearer tokens, configured from <c>Authentication:Schemes:Bearer</c>
    ///     (<c>AddBitwardenAuthentication()</c>), and <c>IHttpContextAccessor</c>.
    ///   </description></item>
    ///   <item><description>
    ///     The caller's context: <see cref="IContext"/>, and <see cref="ICallerProof"/>, what this
    ///     service's own calls carry their caller's context with.
    ///   </description></item>
    ///   <item><description>
    ///     The proxies whose forwarded headers are believed, from <c>ForwardedHeaders:*</c>.
    ///   </description></item>
    ///   <item><description>
    ///     TLS between the deployment's services, from <c>Tls:*</c>: the internal authorities
    ///     trusted for calls, and whether plain HTTP is refused.
    ///   </description></item>
    ///   <item><description>
    ///     Per-address request limits, off when self-hosted or with no rules.
    ///   </description></item>
    ///   <item><description>
    ///     The audience check: a token is accepted only by the service it is addressed to.
    ///   </description></item>
    ///   <item><description>
    ///     For a product service only, the session-token check (a person's session must name the
    ///     person) and the organization or provider an operation names in its path or body.
    ///   </description></item>
    ///   <item><description>
    ///     The default row ownership and caller role, which a service may replace.
    ///   </description></item>
    ///   <item><description>
    ///     Authorization: the handlers for the caller-context requirements and for
    ///     <c>[AnyScope]</c>, which organization an operation creating one may name, how a refusal
    ///     is answered, and the fallback policy, which holds every operation to
    ///     <see cref="Authorization"/> unless it says otherwise.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="TimeProvider.System"/>, and the service's identity (its name, title and
    ///     kind).
    ///   </description></item>
    ///   <item><description>
    ///     Telemetry: traces, metrics and logs over OTLP, named by the service.
    ///   </description></item>
    ///   <item><description>
    ///     Feature flags: <c>IFeatureService</c> from <c>Bitwarden.Server.Sdk.Features</c>,
    ///     configured from <c>Features:*</c>, with the platform's context builder; whether the
    ///     install is self-hosted; and a <c>404</c> for an operation behind a flag that is off.
    ///   </description></item>
    ///   <item><description>
    ///     What the standard endpoints' conventions read: <see cref="Authorization"/>, the problems
    ///     found while they are built, and how their operations are described in the OpenAPI
    ///     document.
    ///   </description></item>
    ///   <item><description>
    ///     The JSON options every endpoint writes and reads with.
    ///   </description></item>
    ///   <item><description>
    ///     A CORS policy, only when <c>Service:BrowserOrigins</c> names origins; see
    ///     <see cref="UsePlatformMiddleware"/>.
    ///   </description></item>
    ///   <item><description>
    ///     The OpenAPI document, titled with <see cref="Title"/>, its OAuth2 client-credentials
    ///     flow pointed at Identity's token endpoint where one is configured.
    ///   </description></item>
    /// </list>
    /// <para>
    /// This runs before <see cref="ConfigureServices"/>, so a service can replace any platform
    /// registration by registering its own afterwards. It is not sealed, but overriding it replaces
    /// the platform's baseline wholesale; call <see langword="base" /> unless that is what you mean.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host builder, for registrations that need configuration or environment.</param>
    protected virtual void AddPlatformServices(WebApplicationBuilder builder)
    {
        // The catch-all: any exception reaching the pipeline becomes the standard error document
        // rather than a problem document that contradicts what every operation advertises.
        builder.Services.AddExceptionHandler<ErrorResponseExceptionHandler>();

        // UseExceptionHandler() refuses to build unless a path, an explicit handler delegate, or
        // problem details is configured; registering an IExceptionHandler is not enough for that
        // check. So problem details stays as the floor beneath our handler, which takes precedence
        // and answers first. It also still backs UseStatusCodePages().
        builder.Services.AddProblemDetails();
        builder.Services.AddBitwardenHealthChecks();

        // Every operation is authenticated unless it says otherwise. The fallback policy is what
        // makes that true by default rather than by each author remembering, so a new endpoint is
        // closed until someone opens it.
        builder.Services.AddBitwardenAuthentication();
        builder.Services.AddHttpContextAccessor();
        // Singleton, not scoped. It holds nothing: it reads IHttpContextAccessor, which flows
        // on the execution context, so one instance answers correctly for every request. That
        // matters beyond allocation: a scoped instance captured by something longer-lived, such
        // as an Entity Framework model, would answer for whichever request built it.
        var isProductService = _isProductService;
        builder.Services.TryAddSingleton<IContext>(provider =>
            new Context(provider.GetRequiredService<IHttpContextAccessor>(), isProductService));
        // What this service's own calls exchange their signed context from (CallerProof); holds nothing.
        builder.Services.TryAddSingleton<ICallerProof>(provider => new CallerProof(
            provider.GetRequiredService<IHttpContextAccessor>(), provider.GetRequiredService<IContext>(), isProductService));

        // Whose word is taken for the address a request was forwarded from (ForwardedHeaders:*),
        // for the connection's address every service sees, and a product service records.
        builder.Services.TryAddSingleton<TrustedProxies>();

        // TLS between the deployment's services (Tls:*): the internal authorities trusted for calls, and
        // whether plain HTTP is refused. Nothing changes where neither is set.
        InternalTlsTrust.Add(builder.Services, builder.Configuration);

        // Per-address request limits in the IpRateLimitOptions shape (Http/IpRateLimiting.cs):
        // off when self-hosted or with no rules.
        builder.Services.AddIpRateLimiting();

        // A token is accepted only where it is addressed: at a product service, a person's
        // token exchanged for that product; at a domain service, a service's token issued for it.
        // Always on, so no service can forget it.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, ServiceAudience>());

        // Where a person's session ends, the session must name the person. A token that does not
        // is refused at authentication, before anything could treat it as acting for nobody.
        if (isProductService)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, PersonSessionTokens>());

            // The organization or provider an operation names in its path or body, where the
            // session names none ([NamesOrganization], [NamesProvider]): carried on its outgoing
            // calls, never into this service's own context. Holds nothing, as IContext.
            builder.Services.TryAddSingleton<IRequestNamedOwner, RequestNamedOwner>();
        }

        // The same reasoning, and the same lifetime: it reads IContext and holds nothing. A
        // service whose rows are scoped by something other than an organization or a user
        // replaces this from ConfigureServices, which runs after and wins.
        builder.Services.TryAddSingleton<IRowOwnership, DefaultRowOwnership>();
        // The same reasoning again: it reads the claims off IHttpContextAccessor and holds
        // nothing. Registered for every service even though only a product service has a user
        // session to read: below the product layer it answers None, which is correct, and no
        // domain service asks.
        builder.Services.TryAddSingleton<ICallerRole, CallerRole>();
        // Scoped, not singleton: it reads the caller context of the request being authorized.
        builder.Services.AddScoped<IAuthorizationHandler, ContextRequirementsHandler>();
        // An operation reachable with any one of several scopes ([AnyScope]); holds nothing.
        builder.Services.AddScoped<IAuthorizationHandler, AnyScopeHandler>();
        // Which organization an operation creating one may name on its calls ([CreatesOrganization]).
        builder.Services.AddScoped<IOrganizationCreation, OrganizationCreation>();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddRequirements(new ContextRequirements(new AuthorizationAttribute()))
                .Build());

        // Time is a dependency, not an ambient fact. Registered with TryAdd so a service (or a
        // test) can substitute a controllable clock, which DateTime.UtcNow would never allow.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Who this process is, for anything that stamps its work with where it came from.
        builder.Services.TryAddSingleton<IServiceIdentity>(new ServiceIdentity(Name, Title, isProductService));

        // Traces, metrics and logs over OTLP, named by this service. Not exported while the process
        // only describes itself.
        Observability.Telemetry.Add(
            builder.Services, builder.Configuration, Name, export: !OpenApiExport.Requested(Args, out _));

        // Feature flags, for every service: LaunchDarkly when Features:LaunchDarkly:SdkKey is
        // set, Features:FlagValues otherwise. The context's kinds, keys and attributes are fixed
        // (see ServiceContextBuilder), so one LaunchDarkly project targets every Bitwarden server
        // alike. Added rather than tried, as the package's own AddContextBuilder does, so a service
        // that registers its own from ConfigureServices, which runs after, still wins.
        builder.Services.AddFeatureFlagServices();
        builder.Services.AddContextBuilder<Features.ServiceContextBuilder>();

        // Whether this is a self-hosted install, for IBitwardenEnvironment.SelfHosted (which AddFeatureFlagServices
        // registers): globalSettings:selfHosted. Cloud when unset.
        SelfHosting.AddDetails(builder.Services);

        // An operation behind a flag that is off answers 404. The status alone, with no body, so the status-code writer below answers it in the
        // standard error shape, indistinguishable from an address that matches nothing, rather
        // than the package's own problem document.
        builder.Services.Configure<Bitwarden.Server.Sdk.Features.FeatureCheckOptions>(options =>
            options.OnFeatureCheckFailed = failed =>
            {
                failed.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });

        // What an endpoint's conventions read while it is built (StandardEndpointExtensions): the
        // service's own authorization defaults, and where problems are collected for the check at
        // the end of Build.
        builder.Services.TryAddSingleton(Authorization);
        builder.Services.TryAddSingleton<Endpoints.StandardEndpointProblems>();
        // And how its operations are described in the OpenAPI document.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<
            Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionProvider, Endpoints.StandardEndpointApiDescriptionProvider>());

        // The platform's wire conventions, for every endpoint.
        builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        // Off unless origins are named. A service that admits no browser origin answers exactly as
        // it did before CORS existed here: no policy, no middleware, no headers.
        _browserOrigins = BrowserOrigins.Read(builder.Configuration);
        if (_browserOrigins.Count > 0)
        {
            BrowserOrigins.AddPolicy(builder.Services, _browserOrigins);
        }

        var title = Title;
        var tokenUrl = ResolveIdentityTokenUrl(builder.Configuration);
        builder.Services.AddOpenApi(options => ApiDocumentation.Configure(options, title, tokenUrl, isProductService));
    }

    /// <summary>
    /// What this service's operations demand unless they say otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base of the three levels: this, then a handler class's
    /// <see cref="AuthorizationAttribute"/>, then an operation's, each overriding the last. A
    /// service whose operations are mostly alike says so once here. A vault is mostly
    /// personal, so nearly every operation in it accepts a request naming no organization, and
    /// repeating that a hundred times is a hundred chances to leave one out.
    /// </para>
    /// <para>
    /// Declaring <see cref="AuthorizationDefaults.ReadScope"/> and
    /// <see cref="AuthorizationDefaults.WriteScope"/> is worth doing on its own account. Without
    /// them an operation that declares no scope demands none, which is a gap nobody sees; with
    /// them the platform picks by what kind of operation it is, and every operation is covered
    /// whether or not anybody remembered.
    /// </para>
    /// </remarks>
    protected virtual AuthorizationDefaults Authorization => new();

    /// <summary>
    /// Registers the service's own dependencies.
    /// </summary>
    /// <remarks>
    /// The base implementation does nothing; override it to register what the service needs. This is
    /// the last point at which anything may be added to the container. It is intentionally not
    /// abstract: a service with no dependencies of its own is legitimate and should not be made to
    /// write an empty override.
    /// </remarks>
    /// <param name="services">The service collection, still open for registration.</param>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Adds middleware that must run <em>outside</em> the platform's middleware.
    /// </summary>
    /// <remarks>
    /// The base implementation does nothing. Middleware added here sees every request before the
    /// platform's does, and sees exceptions the platform's exception handling has already
    /// translated. This is the right place for something that must wrap everything, and the wrong
    /// place for almost anything else.
    /// </remarks>
    /// <param name="app">The application, acting as the middleware pipeline.</param>
    protected virtual void OnConfiguringPipeline(WebApplication app)
    {
    }

    /// <summary>
    /// Adds the middleware every Bitwarden service is expected to run, in the order it must run in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base implementation adds, outermost first:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     A write finished when its client leaves (<see cref="FinishedWrites"/>), so a write
    ///     runs to the end once sent. First, so nothing below
    ///     sees the client's leaving on a request that isn't safe.
    ///   </description></item>
    ///   <item><description>
    ///     The connection's address and scheme as a trusted proxy saw them
    ///     (<see cref="ForwardedClientAddress"/>, <c>ForwardedHeaders:KnownProxies</c> and
    ///     <c>KnownNetworks</c>), before anything reads the connection.
    ///   </description></item>
    ///   <item><description>
    ///     A log scope naming the organization, user and provider the request is about, so
    ///     everything below logs and traces with them, the exception handler included.
    ///   </description></item>
    ///   <item><description>
    ///     <c>UseExceptionHandler()</c>: answers an unhandled exception with the standard error
    ///     document. Outside everything that follows, so that it covers every later stage,
    ///     including endpoint execution.
    ///   </description></item>
    ///   <item><description>
    ///     <c>UseStatusCodePages()</c>: answers a bare status code produced without a body with the
    ///     standard error document.
    ///   </description></item>
    ///   <item><description>
    ///     <c>UseCors()</c>, only when <c>Service:BrowserOrigins</c> names origins. Before the
    ///     refusals and authentication, so a preflight (which carries no token) is answered here
    ///     rather than refused <c>401</c>, and so a refusal a page is sent still carries the headers
    ///     that let the page read it.
    ///   </description></item>
    ///   <item><description>
    ///     Per-address request limits, off when self-hosted or with no rules.
    ///   </description></item>
    ///   <item><description>
    ///     For a product service (one marked <see cref="ProductServiceAttribute"/>), refusing a
    ///     request that carries any of the caller-context headers (<see cref="BitwardenHeaders.All"/>).
    ///   </description></item>
    ///   <item><description>
    ///     <c>UseSwaggerUI()</c> at <c>/docs</c>: the service's API documentation, rendered from
    ///     its own OpenAPI document. The UI's assets are embedded in the service, so nothing is
    ///     fetched from an external origin to read them.
    ///   </description></item>
    ///   <item><description>
    ///     Authentication, then, for a domain service, the strict reading of
    ///     <see cref="BitwardenHeaders.Context"/> from another service, then authorization.
    ///   </description></item>
    ///   <item><description>
    ///     The feature flag checks: an operation behind a flag that is off answers <c>404</c>, to an
    ///     authorized caller only.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Overriding this replaces the platform's pipeline wholesale, which is rarely what anyone
    /// wants; prefer <see cref="OnConfiguringPipeline"/> or <see cref="OnPipelineConfigured"/> to
    /// add middleware on either side of it.
    /// </para>
    /// </remarks>
    /// <param name="app">The application, acting as the middleware pipeline.</param>
    protected virtual void UsePlatformMiddleware(WebApplication app)
    {
        // Before anything takes the request's cancellation: a write runs to the end once sent.
        app.UseMiddleware<FinishedWrites>();

        // Before anything reads the connection: the address a trusted proxy was connected from,
        // and the scheme it was connected over, and nothing a client wrote.
        app.UseMiddleware<ForwardedClientAddress>();

        // Then, so that everything below logs and traces with the organization, user and
        // provider the request is about, including the exception handler, whose messages are
        // the ones somebody is reading when they most need to know whose request it was. The
        // context is read as each message is written, since it comes from the validated token.
        app.UseMiddleware<Observability.ContextLogScope>();

        app.UseExceptionHandler();

        // With a handler, not bare: the default writes a problem document for any failure that
        // set a status and no body (a 404 from routing, a 405 from a method mismatch), which is
        // the one way a service can still answer in a shape it never advertised.
        app.UseStatusCodePages(StatusCodeErrorWriter.Write);

        // Inside the exception handler, which then covers it, and ahead of everything that can
        // refuse a request. A preflight is answered and ends here, so it never meets the refusal
        // or the fallback policy, neither of which a request without a token could pass. An
        // actual request continues, and its answer (a 400 or 401 included) carries the headers
        // the browser needs to let the page read it.
        if (_browserOrigins.Count > 0)
        {
            app.UseCors(BrowserOrigins.PolicyName);
        }

        // After the preflight is answered and before anything else can answer, so a refusal
        // carries the headers a browser needs to read it.
        app.UseIpRateLimiting();

        // Before anything reads the context, and before authentication, so the answer is the same
        // whatever the token: a product service refuses the four caller-context headers, one list.
        // See ProductServiceAttribute.
        if (_isProductService)
        {
            app.UseMiddleware<CallerContextRefusal>();
        }

        // Before authorization on purpose. The documentation surface is public; the API it
        // documents is not. It is served by middleware rather than an endpoint, so the
        // fallback policy, which applies to any request without one, would close it.
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = DocumentationRoutePrefix;
            options.DocumentTitle = Title;
            options.SwaggerEndpoint(DocumentPath, Title);

            // Bitwarden branding. The stylesheet also hides Swagger UI's topbar, which carries
            // the "Select a definition" picker and a spec-URL box; both exist to switch between
            // multiple OpenAPI documents, and a Bitwarden service publishes exactly one.
            options.HeadContent = $"""
                <link rel="icon" type="image/x-icon" href="{DocumentationAssets.FaviconPath}">
                <link rel="stylesheet" type="text/css" href="{DocumentationAssets.StylesheetPath}">
                """;

            // A product service's documentation is set apart from a domain service's, so nobody
            // mistakes the browser's API for a platform one. It always renders dark: Swagger UI
            // otherwise follows the operating system, and the product background is chosen
            // against its dark theme. The stylesheet keys off the class.
            if (_isProductService)
            {
                options.HeadContent += """

                    <script>document.documentElement.classList.add("dark-mode", "bw-product-service");</script>
                    """;
            }

            // A product domain service's is set apart too, and dark for the same reason.
            if (_isProductDomainService)
            {
                options.HeadContent += """

                    <script>document.documentElement.classList.add("dark-mode", "bw-product-domain-service");</script>
                    """;
            }
        });

        app.UseBitwardenAuthentication();

        // A domain service believes another service's call only as its context token says, so
        // verifying it waits for the service token; a missing or forged one is refused 401, and a
        // malformed or disagreeing context 400, before authorization or an operation reads any of it.
        if (!_isProductService)
        {
            app.UseMiddleware<CallerContextHeaders>();
        }

        app.UseAuthorization();

        // [RequireFeature] (Bitwarden.Server.Sdk.Features) on an operation or its handler class. After
        // authorization, so a caller without a session is refused
        // 401 whatever the flag, and only an authorized caller learns that the flag is off.
        app.UseFeatureFlagChecks();
    }

    /// <summary>
    /// Adds middleware that must run <em>inside</em> the platform's middleware but before endpoints.
    /// </summary>
    /// <remarks>
    /// The base implementation does nothing. Middleware added here runs with the platform's
    /// guarantees already in place: an exception thrown from it is translated rather than escaping
    /// the process. This is the usual place for a service's own middleware.
    /// </remarks>
    /// <param name="app">The application, acting as the middleware pipeline.</param>
    protected virtual void OnPipelineConfigured(WebApplication app)
    {
    }

    /// <summary>
    /// Maps the endpoints every Bitwarden service exposes, regardless of its domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base implementation maps:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>/api/openapi.json</c>: the service's OpenAPI document, generated from its endpoint
    ///     signatures, request and response types, data annotations and XML documentation.
    ///   </description></item>
    ///   <item><description>
    ///     The documentation UI's stylesheet and icon, under <c>/docs</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>/api/v1/health</c>, <c>/api/v1/readiness</c> and <c>/api/v1/startup</c>: the
    ///     Kubernetes-style probes, tagged <c>System</c>. See <see cref="SystemEndpoints"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>GET /</c> and <c>HEAD /</c>: a <c>302</c> to the API documentation at
    ///     <c>/docs/index.html</c>, so the service's bare address leads somewhere useful rather
    ///     than to a <c>401</c>. Anonymous and left out of the OpenAPI document, like the
    ///     documentation it points at.
    ///   </description></item>
    ///   <item><description>
    ///     <c>GET /api/v1/config</c>, for a product service only: the version, environment,
    ///     settings and feature flags a client reads, anonymous, in the OpenAPI document.
    ///   </description></item>
    /// </list>
    /// </remarks>
    /// <param name="app">The application, acting as the endpoint route builder.</param>
    protected virtual void MapPlatformEndpoints(WebApplication app)
    {
        // The document describes the API; reading it requires no credential.
        app.MapOpenApi(DocumentPath).AllowAnonymous();
        app.MapDocumentationAssets();
        app.MapSystemEndpoints();

        // The bare address answers with the documentation rather than the fallback policy's 401.
        // Relative, so the client stays on the scheme, host and port it used. Behind a proxy the
        // service does not know the public address, and an absolute URL built from the request
        // would name the internal one. Temporary, because nothing promises the documentation
        // stays there, and a cached permanent redirect outlives the promise.
        app.MapMethods("/", [HttpMethods.Get, HttpMethods.Head], () => Results.Redirect(DocumentationIndexPath))
            .ExcludeFromDescription()
            .AllowAnonymous();

        // GET /api/v1/config, for a product service only: it is what a browser reads before it
        // signs in, and a browser never reaches a domain service.
        if (_isProductService)
        {
            Features.ConfigEndpoints.Map(app);
        }

    }

    /// <summary>
    /// Applies the wire conventions the API standards require.
    /// </summary>
    /// <remarks>
    /// A member with no value is omitted rather than serialised as null, so a caller never sees a
    /// <c>"meta": null</c> it has to reason about. The attributes envelope is applied by the
    /// resource document converter. Casing is camelCase, which is the web default already.
    /// </remarks>
    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        // Enumerations travel as their names, not their ordinals, and in the upper snake case
        // the standards require. A number on the wire is unreadable in a log or a support
        // ticket, and, worse, it binds the contract to declaration order, so inserting a
        // member silently changes what every existing value means to callers that have not
        // redeployed.
        options.Converters.Add(new JsonStringEnumConverter(EnumValues.Naming));

        // Every date in UTC with a literal Z, never +00:00.
        UtcDates.Apply(options);

        options.Converters.Add(new ResourceDocumentJsonConverterFactory());
    }

    /// <summary>
    /// Resolves Identity's token endpoint for the documentation UI's client-credentials flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Service:PublicIdentityUrl</c> first: the token request is made by the documentation
    /// UI in a browser, so it must go where a browser reaches Identity, which in a deployment is
    /// not the internal address the service validates tokens against. Configured and unusable
    /// fails startup, since it was asked for.
    /// </para>
    /// <para>
    /// Otherwise the authority. Returns null when no authority is configured, in which
    /// case the OAuth2 credential is left out of the document entirely rather than published
    /// pointing at a host that is not there.
    /// </para>
    /// </remarks>
    private static Uri? ResolveIdentityTokenUrl(IConfiguration configuration)
    {
        var publicUrl = configuration[PublicIdentityUrlKey];
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            return TokenEndpoint(publicUrl)
                ?? throw new InvalidOperationException(
                    $"'{publicUrl}' in {PublicIdentityUrlKey} is not an absolute http or https URL.");
        }

        var authority = configuration["Authentication:Schemes:Bearer:Authority"]
            ?? configuration["globalSettings:baseServiceUri:internalIdentity"]
            ?? configuration["globalSettings:baseServiceUri:identity"];

        return string.IsNullOrWhiteSpace(authority) ? null : TokenEndpoint(authority);
    }

    private static Uri? TokenEndpoint(string authority)
    {
        // Scheme is checked explicitly: a bare path such as "/connect/token" parses as an absolute
        // file:// URI, so TryCreate succeeding is not on its own evidence of a usable endpoint.
        return Uri.TryCreate($"{authority.TrimEnd('/')}/connect/token", UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            ? uri
            : null;
    }

    /// <summary>
    /// Maps the service's HTTP surface.
    /// </summary>
    /// <remarks>
    /// The base implementation does nothing. A service maps each resource's operations here, as
    /// minimal APIs under the platform's standards: <c>endpoints.MapGroup("api/v1/widgets").WithStandards()</c>
    /// and one <c>Map</c> line per handler (see <see cref="StandardEndpointExtensions"/>). Which
    /// features a process serves is decided by which <c>Map</c> calls it makes.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    protected virtual void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
