# Bitwarden.Server.Sdk.Service

## About

The package a Bitwarden .NET service references. A service derives one class from `Service`, gives
it an identifier and a title, and maps its endpoints as minimal APIs. `Service` owns the rest of
startup in a fixed order, because the order of registration and middleware is security-relevant:

- every request authenticated by default, token audiences always checked, and each operation
  authorized by scope and by the organization, user and provider it concerns;
- the standard request and response documents, binding, validation, error documents, querying and
  bulk operations;
- an OpenAPI document and a documentation UI;
- traces, metrics and logs over OTLP, and health probes;
- trusted proxies, per-address request limits, CORS for named browser origins and internal TLS;
- feature flags, and for a product service a `GET /api/v1/config` resource;
- idempotency keys for writes from other services.

A service references this package alone. It brings and registers Resources, Errors, Authorization,
Context, RateLimiting, Http, Idempotency, Idempotency.Abstractions, Sagas, Data.Abstractions,
Resources.Caching.Abstractions, Service.Abstractions, HealthChecks, Features and Authentication (all `Bitwarden.Server.Sdk.*`). Writing resources and their
operations is in [Resources' PACKAGE.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/src/PACKAGE.md); this one covers running the
service.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected Data,
encrypted on the client. Nothing in this package decrypts an encrypted value. Declare an encrypted
member with `[Encrypted]` (Data.Abstractions).

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service --prerelease
```

In a `Microsoft.NET.Sdk.Web` project, turn on the request delegate generator and XML documentation.
XML comments become the OpenAPI document's summaries; a handler documents only what a caller sends,
so `CS1573` is off as well as `CS1591`:

```xml
<PropertyGroup>
  <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
</PropertyGroup>
```

## Configuration

Every setting is optional. With none, a service runs in the cloud configuration with no CORS, no
trusted proxies, no rate limits, and telemetry sent to the OTLP exporter's default endpoint. The
exporter reads the standard `OTEL_*` variables.

| Setting                                                                        | Default                          | What it does                                                                                                                                               |
| ------------------------------------------------------------------------------ | -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Bitwarden:ServiceName`                                                        | The name passed to `Service`     | The name the client packages authenticate as (`internal.{name}`). Supplied as the lowest-precedence source.                                                |
| `Logging:Console:IncludeScopes`                                                | `true`                           | Writes each request's organization, user and provider on console log lines. Supplied as a default.                                                         |
| `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics`                    | `Warning`                        | Keeps ASP.NET Core's request lines, which carry the query string, out of the logs. Supplied as a default.                                                  |
| `globalSettings:selfHosted`                                                    | `false`                          | Marks a self-hosted install (see "Run in a self-hosted install").                                                                                          |
| `globalSettings:selfHostFlavor`                                                | `unknown`                        | The self-hosted install's flavor, read only when self-hosted.                                                                                              |
| `Service:BrowserOrigins`                                                       | None                             | Browser origins admitted cross-origin, an array or a single value. None means no CORS at all.                                                              |
| `Service:PublicIdentityUrl`                                                    | None                             | Where a browser reaches Identity, for the documentation UI's token request. Must be absolute `http` or `https`, or the service doesn't start.              |
| `Authentication:Schemes:Bearer:Authority`                                      | None                             | The Identity service whose tokens are trusted. Also the UI's token URL when the setting above is unset.                                                    |
| `globalSettings:baseServiceUri:internalIdentity`, `:identity`                  | None                             | The UI's token URL, in that order, when neither setting above is set. With none, the OpenAPI document has no OAuth2 scheme.                                |
| `Tls:CertificateAuthorities`                                                   | None                             | PEM files of root authorities trusted for outbound calls and the bearer handler's key fetches, besides the system's.                                       |
| `Tls:RequireHttps`                                                             | `false`                          | The bearer authority and every listening address but `Tls:HealthPort` must be `https://`, or the service doesn't start.                                    |
| `Tls:ServerCertificate`, `Tls:ServerKey`                                       | None                             | The server certificate (base64 DER) and its private key (base64 PKCS #8, a secret) for HTTPS endpoints without one of their own.                           |
| `Tls:HealthPort`                                                               | None                             | The one port allowed plain HTTP with `Tls:RequireHttps`. It answers the three probes alone.                                                                |
| `SpansServices:RefuseUndeclared`                                               | `false`                          | Refuses the write that makes an operation span services unless it declares `[SpansServices]`. Off, it is counted (`bitwarden.operation.spans`) and logged. |
| `OpenTelemetry:Enabled`                                                        | `true`, `false` when self-hosted | Turns the OTLP exporters on or off.                                                                                                                        |
| `OpenTelemetry:Tracing:Enabled`, `:Metrics:Enabled`, `:Logging:Enabled`        | `OpenTelemetry:Enabled`          | Turns one exporter on or off. Spans are created either way, so a trace continues through a service that exports nothing.                                   |
| `DD_ENV` (environment variable)                                                | None                             | Added to the telemetry resource as `env`.                                                                                                                  |
| `Service:Environment:CloudRegion`, `:Vault`, `:FillAssistRules`                | None                             | The config resource's `environment.cloudRegion`, `.vault` (the web vault's address) and `.fillAssistRules`. Each is omitted when unset.                    |
| `Service:Settings:DisableUserRegistration`, `:SuppressOnboardingInterstitials` | `false`                          | The config resource's `settings.disableUserRegistration` and `settings.suppressOnboardingInterstitials`.                                                   |
| `globalSettings:enableEmailVerification`                                       | `false`                          | The config resource's `settings.enableEmailVerification`: registration mails a link.                                                                       |
| `globalSettings:enableOpenInviteLinkEmailVerification`                         | `true`                           | Its `settings.enableOpenInviteLinkEmailVerification`: following an invite link needs a verified address.                                                   |

The packages `Service` registers read their own settings, which a service sets in its own
configuration: `ForwardedHeaders:*` and `CallChain:RefuseReentry`
([Authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md)), `IpRateLimitOptions:*` and
`globalSettings:distributedIpRateLimiting:*` ([RateLimiting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.RateLimiting/src/PACKAGE.md)),
`Idempotency:*` ([Idempotency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md)), `Features:*` (Features) and `Authentication:Schemes:Bearer:*`
(Authentication).

Keep secrets (the LaunchDarkly SDK key, Redis connection strings, `Tls:ServerKey`) in user secrets or
the environment, not in `appsettings*.json`.

## Usage

The examples share one service, `widget-service`, whose people keep widgets. Types named `Widget...`
are the service's own.

### Define a service

`Program.cs` is the whole entry point:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Service;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

new WidgetService(args).Run();

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override AuthorizationDefaults Authorization => new()
    {
        // A widget belongs to a person, not to an organization.
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        // Lets your XML comments reach the OpenAPI document.
        services.AddOpenApi();
        services.AddSingleton<IWidgetStore, WidgetStore>();
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}

/// <summary>
/// A widget.
/// </summary>
public sealed record Widget : Resource
{
    // Validated before the handler runs: a longer name is refused 422, naming the field.
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device. The service stores them as they arrive.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(await store.AddAsync(request.Data, cancellationToken));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id));
}
```

- The identifier must match `^[a-z0-9]+(-[a-z0-9]+)*$`, or the constructor throws; that catches the
  identifier and title passed the wrong way round. The title names the OpenAPI document and UI.
- The identifier is the service's token audience, its outbound credential (`internal.{name}`), its
  telemetry resource and its rate-limit keys. Choose it once: changing it changes the audience, so
  every caller's tokens must change too.
- Once every endpoint is mapped, the package checks them all. An endpoint declared wrongly, or one
  that contradicts its authorization, stops the service with every problem listed at once.
- `/` redirects to the documentation UI at `/docs`, the OpenAPI document is at `/api/openapi.json`,
  and every other operation answers `401` until the caller sends a token from the Identity service
  named in `Authentication:Schemes:Bearer:Authority`.
- JSON is camel case, leaves `null` members out and writes enumerations in upper snake case. Dates
  are written in UTC with a literal `Z`, the fraction to the tick with trailing zeros left off. Any
  ISO 8601 offset is read and a date without a zone is taken as UTC; an answer never repeats the
  offset a caller sent.
- `Build()` returns the configured `WebApplication` without starting it, for tests. `Run()` builds
  and runs it; given `--emit-openapi <path>`, it writes the OpenAPI document and exits, exporting no
  telemetry.
- Don't also call `UseBitwardenSdk()` from the `Bitwarden.Server.Sdk` MSBuild SDK. `Service` already
  does what it does, and calling both fails with "Scheme already exists: Bearer". The settings both
  read (`globalSettings:selfHosted`, `OpenTelemetry:*`, `DD_ENV`) mean the same in both.

### Add to startup

Override the other hooks only when you need them:

```csharp
using Bitwarden.Server.Sdk.Service;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    // Outside the platform's middleware: sees every request first, before authentication.
    protected override void OnConfiguringPipeline(WebApplication app) =>
        app.Use(async (http, next) =>
        {
            http.Response.Headers["X-Widget-Edge"] = "1";
            await next(http);
        });

    // Inside it: after authorization, and an exception here is answered as a standard error.
    protected override void OnPipelineConfigured(WebApplication app) =>
        app.UseMiddleware<WidgetAuditMiddleware>();
}
```

Sources added in `ConfigureConfiguration` are appended, so they win over `appsettings.json` and the
environment. `ConfigureServices` runs after the platform's registrations, so registering a type the
platform registered replaces it. `Build()` runs, in an order a derived type can't change: `CreateBuilder()`, the self-hosted settings,
`ConfigureConfiguration()`, the platform's registrations, `ConfigureServices()` (the last chance to
register), every `IServiceExtension`, `builder.Build()`, `OnConfiguringPipeline()`, the platform's
middleware, `OnPipelineConfigured()`, the platform's endpoints, and `MapEndpoints()`.

The platform's middleware, outermost first:

1. Finishing writes: a request other than `GET`, `HEAD`, `OPTIONS` or `TRACE` runs to the end even
   when its client disconnects. A safe request keeps the client's cancellation.
2. The forwarded client address, from a trusted proxy.
3. The log scope naming the organization, user and provider.
4. The exception handler, then status-code pages, both answering in the standard error document.
5. CORS, only when browser origins are configured.
6. Per-address rate limits.
7. The context refusal (product services only).
8. The documentation UI at `/docs`.
9. Authentication, then the context check (domain services only), then authorization.
10. The call-chain check.
11. Feature-flag checks.
12. Idempotency keys: a write another service repeats with the same `Idempotency-Key`, to an
    operation marked `[AcceptsIdempotencyKey]`, is answered from its first run.
13. The check on operations whose writes span services.

Middleware in `OnConfiguringPipeline` runs before authentication, so it must not trust anything
about the caller. `AddPlatformServices` and `UsePlatformMiddleware` are virtual, but overriding
either replaces the platform's baseline wholesale.

### Choose the kind of service

- A **domain service**, the default, is called only by other services and takes whom a request
  concerns from the context they send.
- A **product service**, marked `[ProductService]`, is where a person's session ends: it takes the
  context from the validated token, refuses the context headers, and serves `GET /api/v1/config`.
  Mark a service so only when people call it directly.
- A **product domain service**, marked `[ProductDomainService]`, behaves as a domain service that
  owns one product's data; only its documentation UI looks different.
- Both attributes are inherited. A type carrying both makes the constructor throw.
  `IServiceIdentity.IsProductService` reports which kind is running.

### Authorize operations and read the context

Declaring what an operation demands, roles, `IContext`, what each kind of service believes, trusted
proxies, re-entry and tokens from another issuer are all in
[Authorization's PACKAGE.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md) and
[Context's](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Context/src/PACKAGE.md). What `Service` wires up for you:

- `Authorization` on the service (`AuthorizationDefaults`, as in "Define a service") is what every
  operation demands unless its handler or class says otherwise. The fallback policy holds every
  endpoint to it, so a new endpoint is closed until you open it. Set `ReadScope` and `WriteScope`:
  without them an operation that declares no scope demands none.
- A token is accepted only when its `aud` names this service's identifier. That isn't a setting.
- A product service refuses the context headers; a domain service believes them only from another
  service's call with a valid context token. The middleware runs in the order listed under "Add to
  startup".
- Roles answer `None` in a domain service, so an operation demanding a role refuses every call
  there and nothing catches it at startup: authorize a domain service by scope and context.

### Accept an idempotency key

`Service` turns on [Idempotency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md) for every service, after
authorization and the feature-flag checks, so a refusal is never recorded. To have a write done at
most once, mark its operation `[AcceptsIdempotencyKey]`, as Idempotency's
[Opt an operation in](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md#opt-an-operation-in) shows.

### Use feature flags, and keep an operation to one kind of install

```csharp
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Resources;

public static class WidgetsEndpoints
{
    /// <summary>
    /// Polish a widget
    /// </summary>
    // Answers 404 to an authorized caller while the flag is off.
    [RequireFeature("pm-12345-widget-polish")]
    [StandardAction]
    public static Task Polish(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.PolishAsync(id, cancellationToken);
}

// Every operation in the class is refused 422 in Bitwarden's cloud.
[SelfHostedOnly]
public static class WidgetLicensesEndpoints
{
    /// <summary>
    /// Refresh the install's widget license
    /// </summary>
    [StandardAction("refresh", template: "current")]
    public static Task Refresh(IBitwardenEnvironment environment, IWidgetStore store, CancellationToken cancellationToken) =>
        store.RefreshLicenseAsync(environment.SelfHostFlavor, cancellationToken);
}
```

- Flags come from LaunchDarkly when `Features:LaunchDarkly:SdkKey` is set, otherwise from the
  offline `Features:FlagValues`. Inside an operation, read one with `IFeatureService.IsEnabled(key)`.
  Only flags listed in `Features:KnownFlags` reach the config
  resource, so list every flag a client needs there.
- A flag is checked after authorization, so a caller the operation would refuse gets `401` or `403`
  whatever the flag. Once a feature ships, remove `[RequireFeature]` and the flag.
- How `[SelfHostedOnly]` and `[NotSelfHostedOnly]` refuse is in Resources'
  [Report a failure](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/src/PACKAGE.md#report-a-failure).

### Run in a self-hosted install

With `globalSettings:selfHosted` set to `true`:

- `appsettings.SelfHosted.json`, when present, is layered in right after
  `appsettings.{Environment}.json`, overridden by user secrets, the environment and the command
  line. Ship one for what differs, rather than branching on `IBitwardenEnvironment.SelfHosted`.
- `IBitwardenEnvironment.SelfHosted` is true, and `SelfHostFlavor` is `globalSettings:selfHostFlavor`.
- Per-address rate limits are always off (put any in a proxy in front), telemetry exporters default
  to off, `AddCloudSsrfProtection()` clients are unguarded, and the config resource reports
  `settings.selfHosted`.

### Publish the config resource (product services)

A product service serves `GET /api/v1/config`, which a client reads before and after sign-in. You fill
it from the `Service:Environment:*`, `Service:Settings:*` and `globalSettings:enable*` settings.

- Anyone may read it. A valid session token has the flags evaluated for the person; one that fails
  validation gets the anonymous answer.
- `featureFlags` is `IFeatureService.GetAll()`. `version` is `IBitwardenEnvironment.Version`, or the
  service assembly's informational version without build metadata.
- Flags are evaluated for a LaunchDarkly context built from the request: a `user` context keyed by
  the acting user (or a shared anonymous key) with their confirmed organizations, a `device` context,
  and the client's version and type. With an SDK key that context goes to LaunchDarkly: identifiers,
  never Vault Data. Register your own builder with `services.AddContextBuilder<T>()`.

### Admit browser origins

Name the web pages that may call the service from a browser in `Service:BrowserOrigins`, for
example `["https://vault.example.com", "https://*.example.com"]`.

- CORS decides which web pages may read a service's answers, so name origins only for a service a
  browser calls directly. With none there is no CORS policy, middleware or headers.
- `*.` admits a host's subdomains on the same scheme and port. Credentials are never admitted, and
  the context headers are never among the allowed request headers, so a page's preflight for one
  fails.
- `*`, an empty entry, a path, a trailing slash, a written-out default port, user info, a scheme
  other than `http` or `https`, or a `*` other than the host's first label stops the service at
  startup.
- CORS runs ahead of rate limits and authentication: a preflight is answered without a token, and a
  refusal still carries the headers a page needs to read it.

Trusted proxies are configured as [Authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md) describes, and
per-address limits as [RateLimiting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.RateLimiting/src/PACKAGE.md) describes; `Service`
registers both, and turns the limits off when self-hosted.

### Serve and call other services over TLS

`Service` applies the `Tls:*` settings that [Http](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Http/src/PACKAGE.md) defines, both to what it
calls and to what it serves. Guarding a client whose destination someone else chose
(`AddSsrfProtection()`) is in Http's PACKAGE.md too.

```json
{
    "Tls": {
        "CertificateAuthorities": ["/etc/bitwarden/ca/internal-root.pem"],
        "RequireHttps": true,
        "HealthPort": 8081
    }
}
```

- A service serves HTTPS with Kestrel's own settings, or with `Tls:ServerCertificate` and
  `Tls:ServerKey` for a deployment that hands out secrets as settings.
- `Tls:CertificateAuthorities` is trusted by every client the HTTP client factory makes and by the
  bearer handler's key fetches. Name the same authorities on every service.
- `Tls:RequireHttps` refuses a plain-HTTP authority or configured listening address at startup, and
  stops the service if it ends up listening on one (Kestrel's default `http://localhost:5000`).
- `Tls:HealthPort` stays plain HTTP and answers only the probes (anything else `404`). It is
  unauthenticated: bind it to an address only the probing infrastructure reaches, such as loopback.
- An authority file that can't be read, or a server certificate that can't be read, isn't valid now
  or doesn't match its key, stops the service. The message names the setting, never its value.
- TLS gives calls between services a Secure Channel; with the service token each call carries and the
  callee's audience check, a Trusted Channel.

### Add telemetry and health checks

```csharp
using System.Diagnostics;
using Bitwarden.Server.Sdk.Service;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        // Asked by GET /api/v1/readiness; unhealthy answers 503.
        services.AddHealthChecks().AddCheck<WidgetStoreHealthCheck>("widget-store");
}

internal sealed class WidgetPolisher(ILogger<WidgetPolisher> logger)
{
    // Named "Bitwarden.*", so the platform exports it without further registration.
    private static readonly ActivitySource Source = new("Bitwarden.Widgets");

    public void Polish(string widgetId)
    {
        using var activity = Source.StartActivity("Polish widget");
        // Logged with the request's organization, user and provider. Identifiers only.
        logger.LogInformation("Polished widget {WidgetId}.", widgetId);
    }
}

internal sealed class WidgetStoreHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        // The description is what readiness reports; exception text never is.
        Task.FromResult(HealthCheckResult.Unhealthy("The widget store is not reachable."));
}
```

- Name your activity sources and meters `Bitwarden.*`. Exported: traces from ASP.NET Core (exceptions as span events), `HttpClient`, SqlClient and the
  `Bitwarden.*`, `Azure.*` and `RabbitMQ.Client.*` sources; metrics from ASP.NET Core, `HttpClient`,
  SqlClient, the runtime and the `Bitwarden.*` and `Bit.*` meters; logs with their formatted message
  and scopes. The resource is the service's
  identifier. Entity Framework Core and StackExchange.Redis aren't instrumented.
- Each request's log scope and span carry its `OrganizationId`, `UserId` and `ProviderId`
  (`bitwarden.organization_id`, and so on). They say who did what: protect the log and trace
  backends accordingly. Never log a token, a key or an `[Encrypted]` value.
- Request lines carry the query string, which can carry a credential, so they stay at `Warning`.
  Lower that only where no query ever carries one.
- `GET /api/v1/health` and `/api/v1/startup` answer `200` while the process serves; `/api/v1/readiness`
  asks every health check and answers `503` when one is unhealthy, joining their descriptions. A check
  that threw, or has no description, reads "A dependency is not ready.". The probes are anonymous
  and never traced.
- Tag a check `HealthCheckTags.Monitoring` when it describes something every instance shares, such
  as a backlog. Readiness skips it; its status is published as the `bitwarden.health.status` gauge
  and a log line on each change, where an alert belongs.

### Notice re-entries and writes that span services

`Service` registers two checks that count and log by default and refuse when a setting turns them
on (do so in tests and Development):

- A call back into the service while one of its own requests waits on it, refused with
  `CallChain:RefuseReentry` unless the operation declares `[AcceptsReentry]`. See
  [Authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md).
- An operation that writes to another service and anywhere else without `[SpansServices]`, counted
  as `bitwarden.operation.spans` and refused with `SpansServices:RefuseUndeclared`.
  `[SpansServicesUnhandled]`, an admitted defect, is logged and counted as loudly but never refused.
  See [Sagas](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Sagas/src/PACKAGE.md) for the shapes.

### Cache another service's answer briefly

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Caching;
using Microsoft.Extensions.Caching.Memory;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        // The platform doesn't register IMemoryCache.
        services.AddMemoryCache();
}

internal sealed class WidgetGate(IMemoryCache cache, IWidgetPolicies policies)
{
    public Task<bool> IsEnabledAsync(Guid organizationId, CancellationToken cancellationToken) =>
        cache.GetOrResolveOnceAsync(
            ("widget-enabled", organizationId),
            TimeSpan.FromSeconds(30),
            () => policies.IsWidgetEnabledAsync(organizationId),
            cancellationToken);
}
```

- Call `services.AddMemoryCache()` yourself, without a `SizeLimit`: the entry has no size, so a
  size-limited cache refuses it.
- Concurrent requests that miss together share one resolution. A failed or cancelled one isn't
  kept. A caller's token only stops that caller waiting; it never cancels the shared resolution.
- Key by everything the answer depends on, so one caller's answer is never served to another.
- It is a short in-process memo of other services' answers. For a cache shared between instances,
  or one that must be invalidated, use Bitwarden.Server.Sdk.Caching. Never cache Vault Data in
  plaintext.

### Extend every service from a package

`Service` applies every `[ServiceExtension]` declared by the packages a service references, as
[Service.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service.Abstractions/src/PACKAGE.md) describes.
