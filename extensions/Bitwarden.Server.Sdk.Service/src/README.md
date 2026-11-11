# Bitwarden.Server.Sdk.Service

The `Service` base class and the fixed startup order every Bitwarden .NET service runs. See
[PACKAGE.md](PACKAGE.md) for using it.

## What belongs here

How a service is put together and hosted: `Service` and its startup order, self-hosting, the system
endpoints, telemetry and health publishing, the general OpenAPI setup and documentation UI, the
config resource and its feature-flag context, serving internal TLS, `FinishedWrites`, the guard on
operations whose writes span services, and `ResolvedOnce`.

A concern that stands on its own goes in its own package, which `Service` references and registers:
resources and the standard endpoints in Resources, authorization and context checks in
Authorization, per-address limits in RateLimiting, outbound HTTP and the `InternalTls` settings type
in Http, idempotency keys in Idempotency, the error document in Errors. Types a service and its
callers both need go in an abstractions package (Context, Data.Abstractions,
Resources.Caching.Abstractions, Idempotency.Abstractions, Service.Abstractions). Calling another
service belongs in Client.

## How it works

- **Registration order lives here.** The composed packages offer their own entry points
  (`AddCallerAuthorization`, `UseIpRateLimiting`, `UseIdempotencyKeys` and so on). `Service` is the
  one place that calls them, in the order `AddPlatformServices` and `UsePlatformMiddleware`
  document. Changing that order changes this package, and it is security-relevant: CORS sits inside
  the exception handler and ahead of every refusal; idempotency keys come after authorization and
  feature checks so a refusal is never recorded; `OperationWrites` sits inside idempotency so a
  replayed answer isn't counted.
- **Every overridable member** documents what the base implementation does and whether an override
  calls `base`. A derived service extends startup only at the hooks.
- **Defaults are a configuration source.** `Identify` inserts `Bitwarden:ServiceName`,
  `Logging:Console:IncludeScopes` and the request-line log level at position 0, so a deployment still
  wins. `SelfHosting.LayerSettings` runs before `ConfigureConfiguration`, so a service's own sources
  win over `appsettings.SelfHosted.json`.
- **Describing itself.** With `--emit-openapi`, `OpenApiExport.Isolate` drops the hosted services
  registered after the platform's (the service's own and its extensions'), binds an ephemeral
  loopback port, telemetry exports nothing, and the plain-listener refusal is skipped.
- **`ResolvedOnce` instead of Caching.** `GetOrResolveOnceAsync` caches the resolution task in
  `IMemoryCache` under one lock, so concurrent misses share it. It is here rather than in
  Bitwarden.Server.Sdk.Caching so a service needs no FusionCache dependency for it; for the same
  reason telemetry adds no FusionCache instrumentation (`Observability/Telemetry.cs`).
- **Extensions are declared, not scanned.** `ServiceExtensions` reads `[ServiceExtension]` from the
  service's assembly and its references, never the framework's, so an extension is something a
  package author states on purpose where a reviewer sees it.
- **Instrumentation.** No Entity Framework Core or StackExchange.Redis instrumentation: both are
  prereleases a stable package can't depend on (NU5104). Query values stay redacted, as the
  instrumentation does by default, because a query can carry a credential.

| File                                                    | What it holds                                                         |
| ------------------------------------------------------- | --------------------------------------------------------------------- |
| `Service.cs`                                            | The base class, `Build()`, the registrations and the middleware order |
| `Http/InternalTlsTrust.cs`, `Http/InternalTlsServer.cs` | `Tls:*` applied to outbound clients and to what the service serves    |
| `Http/FinishedWrites.cs`                                | Writes that run to the end when their client leaves                   |
| `BrowserOrigins.cs`                                     | `Service:BrowserOrigins`, its checks and the CORS policy              |
| `Observability/`                                        | Telemetry, the request log scope and the health status publisher      |
| `Features/`                                             | The config resource and the LaunchDarkly context builder              |
| `Sagas/`                                                | The guard on operations whose writes span services                    |
| `OpenApi/`                                              | Schema transformers, the documentation UI's assets and the export     |

## Known limitations

The developer guide gives the workaround for each.

- **Startup checks miss a role requirement on a domain service.** Contradictory authorization stops
  a service at startup, but a role requirement on a domain service doesn't, though `ICallerRole`
  always answers `None` there, so every call is refused `403`
  (`Bitwarden.Server.Sdk.Authorization/src/OperationAuthorization.cs`,
  `Bitwarden.Server.Sdk.Authorization/src/ContextRequirementsHandler.cs`).
- **`GetOrResolveOnceAsync` needs `IMemoryCache`, which the platform doesn't register**
  (`Caching/ResolvedOnce.cs`, `Service.AddPlatformServices`). A service calls `AddMemoryCache()`
  itself.

## Testing

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

The tests start the service in process and need no container. The packages `Service` composes have
test projects of their own, under each package's `tests/` folder. The main project compiles its
handlers with the request delegate generator, as a service does; the MinimalApi project compiles
the same files without it, covering the runtime request delegate factory. A change must pass both.

- **Golden files pin what a caller sees.** `MinimalApiParity.golden.txt` records the status, headers
  and body of every request in `MinimalApiParityTests`, and `MinimalApiParity.openapi.golden.json`
  the OpenAPI document. For a deliberate change, rerun with `RECORD_PARITY=1` and review the diff.
- **Security behaviour is pinned:** context refusal and belief (`ProductServiceAttributeTests`,
  `CallerDelegationTests`, `DomainServiceSessionTests`), an asserted context naming a person refused
  unless the operation accepts it (`AcceptsAssertedContextAttributeTests`), audiences (`ServiceAudienceTests`), CORS
  never admitting every origin or credentials (`BrowserOriginsTests`), and defaults `Build()`
  supplies, such as request lines kept out of the log (`ServiceTests`).
- **Composed packages pin their own:** only a trusted proxy's last forwarded address is believed
  (`ForwardedClientAddressTests`, Authorization), rate limits are chosen and counted as
  `IpRateLimitingTests` describes (RateLimiting), and a guarded client never connects to an internal
  address, redirects included (`SsrfProtectionHandlerTests`, `IPAddressExtensionsTests`, Http).
- **Zero knowledge.** Nothing here decrypts, logs or inspects an encrypted value, and no test
  fixture may hold plaintext Vault Data.

## Documentation

- **[`docs/`](../docs/README.md)** is the developer guide for Service and Resources together: `docs/README.md` is its
  table of contents, and each topic has a page of its own. List a new page there.
- **Each page** is code a service writes and what the developer needs to know about it. It doesn't
  restate or link the API standards, and it shows no example requests or responses.
- **Samples must compile.** A sample shows code as a service writes it. Before changing one, build
  it against the package in a scratch web project with `EnableRequestDelegateGenerator` on.
- **A behaviour change** that a page describes updates that page in the same pull request.
- **`PACKAGE.md`** is the readme shown on nuget.org. It stays short, says nothing the guide says, and
  links to the guide with absolute URLs, because nuget.org doesn't resolve relative links.
