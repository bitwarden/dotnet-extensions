# Defining a service

A Bitwarden service is a class deriving from `Service`. The base class owns startup: the order of
dependency registration and of middleware is security-relevant, so it is fixed in `Service.Build()`
and your service only fills in the hooks it needs.

## What the package does

- **One base class.** `Service(name, title, args)` takes the service's identifier, its title and the
  command line. The constructor refuses an identifier that is not lowercase letters, digits and
  hyphens (`^[a-z0-9]+(-[a-z0-9]+)*$`), which catches the identifier and title passed the wrong way
  round.
- **The identifier** names the service's outbound credential (`internal.{name}`), the token audience
  it accepts, its telemetry resource and its rate-limit keys. The package supplies it as
  `Bitwarden:ServiceName`, the lowest-precedence setting.
- **The title** is the title of the OpenAPI document and the documentation UI.
- **A fixed startup sequence** with hooks at defined points (below). A derived type can extend it at
  the seams but can't reorder it.
- **Three kinds of service:** a domain service (the default), a `[ProductService]` and a
  `[ProductDomainService]`. The constructor refuses a type carrying both attributes.
- **A startup check.** Once every endpoint is mapped, `Build()` reads them all and throws, listing
  every problem at once, when an endpoint breaks a standard or contradicts its authorization.

## Example

In a project using `Microsoft.NET.Sdk.Web`, `Program.cs` is the whole entry point:

```csharp
using Bitwarden.Server.Sdk.Service;

new WidgetService(args).Run();

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override AuthorizationDefaults Authorization => new()
    {
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IWidgetStore, WidgetStore>();

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

Override the other hooks only when you need them:

```csharp
using Bitwarden.Server.Sdk.Service;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    // Sources added here are appended, so they win over appsettings.json and the environment.
    protected override void ConfigureConfiguration(IConfigurationManager configuration) =>
        configuration.AddJsonFile("widgets.json", optional: true);

    // Runs after the platform's registrations: registering a type the platform registered
    // replaces it.
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IWidgetStore, WidgetStore>();

    // Outside the platform's middleware: sees every request first, including ones the
    // platform refuses.
    protected override void OnConfiguringPipeline(WebApplication app) =>
        app.Use(async (http, next) =>
        {
            http.Response.Headers["X-Widget-Edge"] = "1";
            await next(http);
        });

    // Inside the platform's middleware: after authentication and authorization, and an
    // exception thrown here is answered in the standard error document.
    protected override void OnPipelineConfigured(WebApplication app) =>
        app.UseMiddleware<WidgetAuditMiddleware>();

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

`Build()` returns the configured `WebApplication` without starting it, for tests and hosting
harnesses. `Run()` builds and runs it; given `--emit-openapi <path>` it writes the OpenAPI document
instead and exits.

```csharp
await using var app = new WidgetService([]).Build();
await app.StartAsync();
```

### The hooks, in order

| Step | Hook or platform step      | What happens                                                                                        |
| ---- | -------------------------- | --------------------------------------------------------------------------------------------------- |
| 1    | `CreateBuilder()`          | `WebApplication.CreateBuilder(args)`. Override only to substitute a differently configured builder. |
| 2    | `ConfigureConfiguration()` | Your configuration sources, appended last, so they win over the standard sources.                   |
| 3    | Platform registrations     | Authentication, authorization, telemetry, feature flags, rate limits, CORS, OpenAPI and the rest.   |
| 4    | `ConfigureServices()`      | Your registrations. The last point at which the service adds to the container.                      |
| 5    | Service extensions         | Every `IServiceExtension` a referenced package declares.                                            |
| 6    | `builder.Build()`          | The container is fixed.                                                                             |
| 7    | `OnConfiguringPipeline()`  | Middleware that must wrap the platform's.                                                           |
| 8    | Platform middleware        | See below.                                                                                          |
| 9    | `OnPipelineConfigured()`   | Your own middleware, the usual place for it.                                                        |
| 10   | Platform endpoints         | `/api/openapi.json`, `/docs`, the health probes, `GET /`, and a product service's config resource.  |
| 11   | `MapEndpoints()`           | Your endpoints. Then the startup check runs.                                                        |

`AddPlatformServices` and `UsePlatformMiddleware` are virtual too, but overriding either replaces
the platform's baseline wholesale. Prefer the hooks on either side of them.

### Middleware order

The platform's middleware runs in this order, outermost first:

1. Finishing writes: a request other than `GET`, `HEAD`, `OPTIONS` or `TRACE` runs to the end even
   when its client disconnects. A safe request keeps the client's cancellation.
2. The forwarded client address, from a trusted proxy.
3. The log scope naming the organization, user and provider.
4. The exception handler, then status-code pages, both answering in the standard error document.
5. CORS, only when browser origins are configured.
6. Per-address rate limits.
7. The caller-context refusal (product services only).
8. The documentation UI at `/docs`.
9. Authentication, then the caller-context check (domain services only), then authorization.
10. Feature-flag checks.

### The three kinds of service

```csharp
using Bitwarden.Server.Sdk.Service;

// A domain service: called only by other services. The default.
internal sealed class WidgetJobs(string[] args) : Service("widget-jobs", "Widget Jobs", args);

// A product service: called by people, holding their own session.
[ProductService]
internal sealed class WidgetPortal(string[] args) : Service("widget-portal", "Widget Portal", args);

// A product domain service: a domain service that owns one product's data.
[ProductDomainService]
internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args);
```

- A **domain service** takes whom a request concerns from the caller context another service sends.
- A **product service** is where a person's session ends. It takes whom a request concerns from the
  validated token, refuses the caller-context headers, and serves `GET /api/v1/config`.
- A **product domain service** behaves exactly as a domain service. The attribute changes only how
  its documentation UI looks, so the three kinds can be told apart.

Both attributes are inherited, so a type deriving from a product service stays one.
`IServiceIdentity.IsProductService` (Bitwarden.Server.Sdk.Common) reports which kind is running. See
[Caller context](caller-context.md) for what each kind believes.

### `UseBitwardenSdk()`

The `Bitwarden.Server.Sdk` MSBuild SDK offers `UseBitwardenSdk()`, which layers in self-hosted
settings, sets up OpenTelemetry and can add authentication and feature flags. A service built on
`Service` gets all of that from `Service`, so don't call `UseBitwardenSdk()` as well: the two
register the bearer scheme twice, which fails with "Scheme already exists: Bearer". The settings
both read (`globalSettings:selfHosted`, `OpenTelemetry:*`, `DD_ENV`) mean the same thing in both.

## Configuration

| Setting                 | Default                      | What it does                                                                                                                            |
| ----------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `Bitwarden:ServiceName` | The name passed to `Service` | The name the client packages authenticate as (`internal.{name}`). Supplied as the lowest-precedence source, so a deployment can set it. |

## What you decide

- **The identifier.** It is unique across Bitwarden and appears in credentials, audiences,
  configuration and telemetry, so choose it once. Changing it later changes the service's audience.
- **The kind of service.** Mark a service `[ProductService]` only when people call it directly.
  Everything a domain service believes about a request comes from another service.
- **Which hooks to override.** Most services need only `Authorization`, `ConfigureServices` and
  `MapEndpoints`.

## See also

- [Authentication and authorization](authentication-and-authorization.md)
- [Caller context](caller-context.md)
- [Configuration and self-hosting](configuration-and-self-hosting.md)
- [Caching and extensions](caching-and-extensions.md)
- [API standards](https://contributing.bitwarden.com/architecture/server/apis)
