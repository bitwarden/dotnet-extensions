# Contributing to Bitwarden.Server.Sdk.Service

This page is for working on the package itself. To build a service with it, start at the
[README](../README.md) and the [developer guide](README.md).

## Running the tests

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

The tests start the service in process and need no container. The packages `Service` composes have
test projects of their own, under each package's `tests/` folder.

## Changing this package

- **What belongs here.** How a service is put together and hosted: the `Service` base class and its
  startup order, self-hosting, the system endpoints, telemetry and health publishing, the general
  OpenAPI setup and documentation UI, the config resource, serving internal TLS, `FinishedWrites`
  and `ResolvedOnce`. A concern that stands on its own goes in its own package, which `Service`
  references and registers: resources and the standard endpoints in Bitwarden.Server.Sdk.Resources,
  authorization in Bitwarden.Server.Sdk.Authorization, per-address limits in
  Bitwarden.Server.Sdk.RateLimiting, outbound HTTP in Bitwarden.Server.Sdk.Http, idempotency keys in
  Bitwarden.Server.Sdk.Idempotency, the error document in Bitwarden.Server.Sdk.Errors. Types a
  service and its callers both need belong in an abstractions package (Bitwarden.Server.Sdk.Context,
  Data.Abstractions, Caching.Abstractions, Idempotency.Abstractions, Service.Abstractions), and
  calling another service belongs in Bitwarden.Server.Sdk.Client.
- **Registration order lives here.** The packages `Service` composes offer their own entry points,
  such as `AddCallerAuthorization`, `UseIpRateLimiting` and `UseIdempotencyKeys`. `Service` is the
  one place that calls them, in the order `Build()` documents. A change to that order is a change to
  this package.
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
- **Context.** A product service takes whom a request concerns from the session and refuses
  every context header (`ProductServiceAttributeTests`). A domain service believes them only
  from an internal service token with a valid context token (`CallerDelegationTests`,
  `DomainServiceSessionTests`), and refuses an asserted context naming a person unless the
  operation accepts it. Token audiences are always checked (`ServiceAudienceTests`).
- **Request addresses.** Only a trusted proxy's last forwarded address is believed
  (`ForwardedClientAddressTests`, in Bitwarden.Server.Sdk.Authorization). Rate limits are chosen and
  counted as `IpRateLimitingTests`, in Bitwarden.Server.Sdk.RateLimiting, describes.
- **`ResolvedOnce` rather than the caching package.** `GetOrResolveOnceAsync` is a short
  in-process memo of one downstream answer per key, built on `IMemoryCache`, so that concurrent
  misses share one resolution. It is here, rather than in Bitwarden.Server.Sdk.Caching, so that a
  service needs no FusionCache dependency for it, which few services use; for the same reason
  telemetry adds no FusionCache instrumentation (`Observability/Telemetry.cs`).
- **CORS** never admits every origin or credentials (`BrowserOriginsTests`).
- **SSRF.** A guarded client never connects to an internal address, including on a redirect
  (`SsrfProtectionHandlerTests` and `IPAddressExtensionsTests`, in Bitwarden.Server.Sdk.Http).
- **Zero knowledge.** Services store and pass on Vault Data only as Protected Data. Nothing here
  decrypts, logs or inspects an encrypted value, and no test fixture may hold plaintext Vault Data.

## Changing the documentation

- **`README.md`**, at the package root, introduces the package to someone building a service: what it is, how to start
  and two short examples.
- **`docs/`** is the developer guide: how to write code with the package, one topic per page. Each
  page is code a service writes and what the developer needs to know about it. It doesn't restate
  or link the API standards, and it shows no example requests or responses.
- **`src/PACKAGE.md`** is the readme shown on nuget.org. It stays short and links to the guide with
  absolute URLs, because nuget.org doesn't resolve relative links.
- **Samples must compile.** A sample shows code as a service writes it. Before changing one, build
  it against the package in a scratch web project with `EnableRequestDelegateGenerator` on.
- A behaviour change that a page describes updates that page in the same pull request.
- **`OPEN-ISSUES.md`** lists the package's known gaps. A fix removes its
  entry and updates the guide page that described the workaround.
