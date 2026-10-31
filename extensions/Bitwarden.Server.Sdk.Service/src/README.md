# Bitwarden.Server.Sdk.Service

The base class and fixed startup sequence for a Bitwarden .NET service. A service derives from
`Service`, maps its endpoints as minimal APIs under the platform's standards, and gets
authentication, authorization, the standard JSON:API documents, OpenAPI, telemetry, rate limiting
and health probes in a set order. The consumer guide is [PACKAGE.md](PACKAGE.md).

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service
```

The package references the Common, Features, HealthChecks and Authentication packages, so a
service gets them with it.

## Using it

In a project using `Microsoft.NET.Sdk.Web`, `Program.cs` runs the service, and the service maps one
resource:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.JsonApi;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

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

/// <summary>
/// A widget.
/// </summary>
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    public required string Name { get; init; }
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
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));
}
```

`IWidgetStore` and `WidgetStore` are the service's own. `Run()` builds and starts the service;
`Build()` returns the configured, unstarted `WebApplication` for tests. The service refuses to start
when a `Map` line disagrees with its handler's attribute, and lists every problem it finds.

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

### Settings with a security trade-off

- **Trusted proxies (`ForwardedHeaders:*`).** A trusted proxy chooses the address a request is
  recorded and rate-limited under, so name only the proxies in front of the service, and keep
  `KnownNetworks` as narrow as the network they live on. Each proxy must add the address it took the
  request from last in `X-Forwarded-For`, and must strip the caller-context headers
  (`BitwardenHeaders.All`) a client sends. A value that does not parse fails closed: an address that
  doesn't parse is looked up as a host name and trusts nobody if the lookup fails, and a network
  that doesn't parse is ignored. Behind a proxy that is not trusted, every request carries the
  proxy's address, so all clients share one rate-limit allowance.
- **Internal TLS (`Tls:*`).** Every service that calls another must trust the authority that issued
  the callee's certificate, so name the same authority files on every service. A server
  certificate is trusted when the system trusts it or it chains to a named root; a host name that
  doesn't match is never trusted. A file that can't be read, or holds no certificate, stops the
  service at startup. With `Tls:RequireHttps`, a plain `http://` bearer authority stops the service
  at startup too.
- **Browser origins (`Service:BrowserOrigins`).** CORS decides which web pages may read a service's
  answers. Name each origin exactly as a browser sends it. `*`, an empty entry, a path, a trailing
  slash, a written-out default port, user info, a scheme other than `http` or `https`, or a `*`
  anywhere but the first label of the host stops the service at startup. Credentials are never
  admitted.
- **Shared rate-limit counts (`globalSettings:distributedIpRateLimiting:*`).** Every instance of a
  service must name the same Redis to share one allowance; counts are kept apart per service name.
  A key holds an unsalted SHA-256 of the rule, endpoint and address, which expires with its window.
  The IPv4 address space is small, so anyone who can read the Redis can recover the addresses:
  protect it as you would the logs. The connection string usually carries a password; keep it in
  secrets, not in `appsettings*.json`. When Redis fails the service lets requests through rather
  than refusing them.
- **Rate-limit rules (`IpRateLimitOptions:*`).** A `Period` that is not a number followed by `s`,
  `m`, `h` or `d`, or an `IpWhitelist` entry that is neither an address nor a CIDR network, stops the
  service when it starts. List the deployment's own service network in `IpWhitelist`, since services
  call each other and fetch their own tokens.
- **Documentation token URL (`Service:PublicIdentityUrl`).** A value that is not an absolute `http`
  or `https` URL stops the service at startup.

## Running the tests

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

The shared rate-limit counter tests (`RateLimitCountersTests`) start a Redis container through
Testcontainers, so they need Docker. The MinimalApi project leaves them out.

## Changing this package

- **What belongs here.** What every service needs to be hosted the same way: startup order,
  authentication and authorization, the standard request and response shapes, OpenAPI, telemetry
  and request limits. Types a service and its callers both need belong in
  Bitwarden.Server.Sdk.Common; calling another service belongs in the client package.
- **Startup order is fixed.** `Service.Build()` registers and orders the middleware; a derived
  service extends it only through the documented hooks. Every overridable member documents what the
  base implementation does and whether an override calls `base`. `ServiceTests` pins defaults
  `Build()` supplies, such as keeping request lines, and so query strings, out of the log.
- **Handlers behave the same with and without the request delegate generator.** The main test
  project compiles its handlers with the generator, as a service does. The MinimalApi project
  compiles the same test files without it, so the framework's runtime request delegate factory is
  covered too. A change must pass both.
- **The golden files pin what a caller sees.** `MinimalApiParity.golden.txt` records the status,
  headers and body of every request in `MinimalApiParityTests`' matrix, and
  `MinimalApiParity.openapi.golden.json` the published OpenAPI document. A change that alters
  either is a change every caller sees. When it is deliberate, rerun with `RECORD_PARITY=1` and
  review the diff.
- **Caller context.** A product service takes whom a request concerns from the session and refuses
  every caller-context header (`ProductServiceAttributeTests`). A domain service believes them only
  from an internal service token with a valid context token (`CallerDelegationTests`,
  `DomainServiceSessionTests`), and refuses an asserted context naming a person unless the
  operation accepts it. Token audiences are always checked (`ServiceAudienceTests`).
- **Request addresses.** Only a trusted proxy's last forwarded address is believed
  (`ForwardedClientAddressTests`). Rate limits are chosen and counted as `IpRateLimitingTests`
  describes.
- **CORS** never admits every origin or credentials (`BrowserOriginsTests`).
- **SSRF.** A guarded client never connects to an internal address, including on a redirect
  (`SsrfProtectionHandlerTests`, `IPAddressExtensionsTests`).
- **Zero knowledge.** Services store and pass on Vault Data only as Protected Data. Nothing here
  decrypts, logs or inspects an encrypted value, and no test fixture may hold plaintext Vault Data.
