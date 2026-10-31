# Bitwarden.Server.Sdk.Service

The base class and fixed startup sequence for a Bitwarden .NET service. A service derives from
`Service`, maps its endpoints as minimal APIs under the platform's standards, and gets
authentication, authorization, the standard JSON:API documents, OpenAPI, telemetry, rate limiting
and health probes in a set order. See [PACKAGE.md](PACKAGE.md) for installing, configuring and using
the package; this file is for working on it.

## Running the tests

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

The shared rate-limit counter tests (`RedisRateLimitCountersTests`) start a Redis container through
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
- **`ResolvedOnce` rather than the caching package.** `GetOrResolveOnceAsync` is a short
  in-process memo of one downstream answer per key, built on `IMemoryCache`, so that concurrent
  misses share one resolution. It is here, rather than in Bitwarden.Server.Sdk.Caching, so that a
  service needs no FusionCache dependency for it, which few services use; for the same reason
  telemetry adds no FusionCache instrumentation (`Observability/Telemetry.cs`).
- **CORS** never admits every origin or credentials (`BrowserOriginsTests`).
- **SSRF.** A guarded client never connects to an internal address, including on a redirect
  (`SsrfProtectionHandlerTests`, `IPAddressExtensionsTests`).
- **Zero knowledge.** Services store and pass on Vault Data only as Protected Data. Nothing here
  decrypts, logs or inspects an encrypted value, and no test fixture may hold plaintext Vault Data.
