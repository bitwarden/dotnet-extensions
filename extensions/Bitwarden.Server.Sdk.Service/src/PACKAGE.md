# Bitwarden.Server.Sdk.Service

## About

The hosting model for a Bitwarden .NET service. A service derives from `Service`, gives it an
identifier and a title, and maps its endpoints as minimal APIs. The base class owns the rest of
startup: the order of dependency registration and of middleware is security-relevant, so it is fixed
in `Service.Build()` rather than rediscovered by each service.

Every service gets, in a fixed order:

- authentication of every request by default, with token audiences always checked;
- authorization by scope and by the organization, user and provider a request concerns;
- the standard request and response documents, binding, validation and error documents;
- querying (filters, sorting, offset and cursor paging, sparse fieldsets) and bulk operations;
- an OpenAPI document and a documentation UI;
- traces, metrics and logs over OTLP;
- per-address request limits, CORS for named browser origins, and health probes;
- feature flags, and for a product service a `GET /api/v1/config` resource.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected Data,
encrypted on the client. Nothing in this package decrypts or inspects an encrypted value, and a
resource declares an encrypted member with `[Encrypted]` from Bitwarden.Server.Sdk.Common, which
checks its length only.

The package references Bitwarden.Server.Sdk.Common, Bitwarden.Server.Sdk.Features,
Bitwarden.Server.Sdk.HealthChecks and Bitwarden.Server.Sdk.Authentication.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service
```

## Bitwarden.Server.Sdk and `UseBitwardenSdk()`

The `Bitwarden.Server.Sdk` MSBuild SDK offers `UseBitwardenSdk()`, which layers in self-hosted
settings, sets up OpenTelemetry and, depending on what the project turns on, adds authentication and
feature flags. A service built on `Service` gets all of that from `Service`, so it does not call
`UseBitwardenSdk()` as well: the two would register the bearer scheme twice, which fails with
"Scheme already exists: Bearer".

The settings both read mean the same thing in both:

- `globalSettings:selfHosted` marks a self-hosted install and layers in
  `appsettings.SelfHosted.json` after `appsettings.{Environment}.json`;
- `OpenTelemetry:Enabled`, `OpenTelemetry:Tracing:Enabled` and `OpenTelemetry:Metrics:Enabled` turn
  the exporters on or off, on by default in the cloud and off when self-hosted
  (`OpenTelemetry:Logging:Enabled` is read by `Service` only);
- the `DD_ENV` environment variable is added to the telemetry resource as `env`.

## Configuration

Every setting is optional. A service with none of them runs in the cloud configuration, with no
CORS, no trusted proxies, no rate limits and telemetry exported to the OTLP exporter's default
endpoint.

| Setting                                                              | Default                                       | What it does                                                                                                                                                           |
| -------------------------------------------------------------------- | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Bitwarden:ServiceName`                                              | The name passed to `Service`                  | The name the client packages authenticate as (`internal.{name}`). Supplied by this package as the lowest-precedence source, so a deployment can still set it.          |
| `Logging:Console:IncludeScopes`                                      | `true`                                        | Writes each request's log scope (its organization, user and provider) on console log lines. Supplied as a default; a deployment can turn it off.                       |
| `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics`          | `Warning`                                     | Keeps ASP.NET Core's request lines, which carry the query string, out of the logs. Supplied as a default; set it lower only where a query never carries a credential.  |
| `globalSettings:selfHosted`                                          | `false`                                       | Marks a self-hosted install: layers in `appsettings.SelfHosted.json`, turns rate limits off and telemetry off by default, and sets `IBitwardenEnvironment.SelfHosted`. |
| `globalSettings:selfHostFlavor`                                      | `unknown`                                     | The self-hosted install's flavor, read only when `globalSettings:selfHosted` is true.                                                                                  |
| `Service:BrowserOrigins`                                             | None                                          | The browser origins admitted cross-origin, as an array or a single value. None means no CORS policy, middleware or headers.                                            |
| `Service:PublicIdentityUrl`                                          | None                                          | Where a browser reaches Identity, for the documentation UI's token request (`{value}/connect/token`). Unset, the token URL comes from the authority settings below.    |
| `Authentication:Schemes:Bearer:Authority`                            | None                                          | The bearer authority. Read here only to derive the documentation UI's token URL when `Service:PublicIdentityUrl` is unset.                                             |
| `globalSettings:baseServiceUri:internalIdentity`                     | None                                          | The token URL's source when neither setting above is set.                                                                                                              |
| `globalSettings:baseServiceUri:identity`                             | None                                          | The token URL's source when none of the settings above is set. With none, the OpenAPI document has no OAuth2 scheme.                                                   |
| `ForwardedHeaders:KnownProxies`                                      | None                                          | Comma-separated addresses or host names of proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed. Loopback is always trusted.                           |
| `ForwardedHeaders:KnownNetworks`                                     | None                                          | Comma-separated CIDR networks of such proxies.                                                                                                                         |
| `Tls:CertificateAuthorities`                                         | None                                          | PEM files of root certificate authorities trusted for outbound calls and the bearer handler's key fetches, besides the system's roots.                                 |
| `Tls:RequireHttps`                                                   | `false`                                       | When true, the bearer authority must be `https://`, or the service does not start.                                                                                     |
| `IpRateLimitOptions:Enabled`                                         | `true`                                        | Whether per-address limits apply. They are off when self-hosted, and when there are no rules, whatever this says.                                                      |
| `IpRateLimitOptions:GeneralRules`                                    | None                                          | The rules, each an `Endpoint` (default `*`), a `Period` (default `1m`), a `Limit` (default `0`, which refuses every request) and optional `Grants`.                    |
| `IpRateLimitOptions:EnableEndpointRateLimiting`                      | `true`                                        | Counts each method and path apart. When false, only `*` rules apply, counting all of an address's requests together.                                                   |
| `IpRateLimitOptions:IpWhitelist`                                     | None                                          | Addresses and CIDR networks never limited.                                                                                                                             |
| `IpRateLimitOptions:EndpointWhitelist`                               | None                                          | `verb:path` patterns never limited.                                                                                                                                    |
| `IpRateLimitOptions:HttpStatusCode`                                  | `429`                                         | The status a request over a limit is refused with.                                                                                                                     |
| `globalSettings:distributedIpRateLimiting:redisConnectionString`     | None                                          | A Redis where every instance of the service keeps one shared count. Unset, each process counts on its own.                                                             |
| `globalSettings:distributedIpRateLimiting:enabled`                   | `true`                                        | Whether a configured Redis is used.                                                                                                                                    |
| `globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold` | `10`                                          | How many Redis failures within the window set Redis aside, letting requests through unlimited until the window closes.                                                 |
| `globalSettings:distributedIpRateLimiting:slidingWindowSeconds`      | `120`                                         | The window, in seconds, those failures are counted in.                                                                                                                 |
| `OpenTelemetry:Enabled`                                              | `true` in the cloud, `false` when self-hosted | Turns the OTLP exporters on or off.                                                                                                                                    |
| `OpenTelemetry:Tracing:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the trace exporter on or off.                                                                                                                                    |
| `OpenTelemetry:Metrics:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the metric exporter on or off.                                                                                                                                   |
| `OpenTelemetry:Logging:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the log exporter on or off.                                                                                                                                      |
| `DD_ENV` (environment variable)                                      | None                                          | Added to the telemetry resource as `env`.                                                                                                                              |
| `Service:Environment:CloudRegion`                                    | None                                          | A product service's `GET /api/v1/config` `environment.cloudRegion`. Omitted when unset.                                                                                |
| `Service:Environment:Vault`                                          | None                                          | The config resource's `environment.vault`, the web vault's address. Omitted when unset.                                                                                |
| `Service:Environment:FillAssistRules`                                | None                                          | The config resource's `environment.fillAssistRules`. Omitted when unset.                                                                                               |
| `Service:Settings:DisableUserRegistration`                           | `false`                                       | The config resource's `settings.disableUserRegistration`.                                                                                                              |
| `Service:Settings:SuppressOnboardingInterstitials`                   | `false`                                       | The config resource's `settings.suppressOnboardingInterstitials`.                                                                                                      |

The packages this one registers read their own settings: `Features:*` for feature flags
(Bitwarden.Server.Sdk.Features) and `Authentication:*` for bearer tokens
(Bitwarden.Server.Sdk.Authentication). The exporter reads the standard `OTEL_*` environment
variables, such as `OTEL_EXPORTER_OTLP_ENDPOINT`.

The settings that carry a security trade-off are explained where they're used:
[trusted proxies, rate limits and browser origins](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/network-and-request-limits.md#security-trade-offs),
[internal TLS](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/outbound-http.md#security-trade-offs) and the
[documentation token URL](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/openapi.md).

## Getting started

Derive from `Service`, give it an identifier and a title, and map your endpoints, each resource's
handlers in a class of their own:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

new WidgetService(args).Run();

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOpenApi();
        services.AddSingleton<IWidgetStore, WidgetStore>();
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/widgets").WithStandards().MapGet("{id}", GetWidget);

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));
}
```

The project needs the request delegate generator and XML documentation turned on, as the README
shows.

The [README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/src/README.md#getting-started)
walks through a complete first service: the project file, a resource, running it and calling it.

## Documentation

The [developer guide](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/README.md) covers how to write a service with this package, one topic per
page with code samples. It follows the structure of Bitwarden's
[API standards](https://contributing.bitwarden.com/architecture/server/apis):

- **Contracts:** [OpenAPI](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/openapi.md), [API paths](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/api-paths.md),
  [breaking changes and versioning](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/breaking-changes-and-versioning.md),
  [unrecognized fields, query parameters, and headers](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/unrecognized-fields-query-parameters-and-headers.md),
  [deprecation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/deprecation.md).
- **Payloads and responses:** [payload shape](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/payload-shape.md),
  [JSON](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/json.md),
  [naming conventions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/naming-conventions.md),
  [standard fields](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-fields.md),
  [content negotiation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/content-negotiation.md),
  [standard responses](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-responses.md),
  [request validation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/request-validation.md),
  [errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/errors.md).
- **Operations:** [verbs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/verbs.md),
  [creating](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/creating-resources.md), [reading](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/reading-resources.md),
  [updating](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/updating-resources.md), [partial updates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/partial-updates.md),
  [optimistic concurrency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/optimistic-concurrency.md),
  [deleting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/deleting-resources.md) and
  [acting upon](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/acting-upon-resources.md) resources.
- **Querying:** [filtering](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/filtering.md), [paging](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/paging.md),
  [sorting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sorting.md), [sparse fieldsets](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sparse-fieldsets.md),
  [advanced queries](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/advanced-queries.md).
- **Bulk and asynchronous work:** [bulk operations](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/index.md)
  and [jobs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/jobs.md).
- **Beyond the API standards:** [defining a service](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/defining-a-service.md),
  [authentication and authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/authentication-and-authorization.md),
  [caller context](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/caller-context.md),
  [configuration and self-hosting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/configuration-and-self-hosting.md),
  [network and request limits](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/network-and-request-limits.md),
  [outbound HTTP](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/outbound-http.md),
  [observability and health](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/observability-and-health.md),
  [caching and extensions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/beyond-the-standards/caching-and-extensions.md).
