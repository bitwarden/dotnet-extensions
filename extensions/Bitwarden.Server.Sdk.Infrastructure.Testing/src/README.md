# Bitwarden.Server.Sdk.Infrastructure.Testing

Collects the infrastructure a service declares by running its startup.

- `InfrastructureCollector` - `CollectInfrastructure<TEntryPoint>()` entry point
- `InfrastructureCollectionOptions` - Self-hosted, flavor, environment and configuration settings for the run
- `InfrastructureManifest` - The collected requirements plus schema version and hosting mode, with `ToJsonString()` and `VerifySnapshot()`
- `InfrastructureSnapshotMismatchException` - Thrown when a snapshot was missing or out of date and has been rewritten

## Design

- The hosting mode is applied as services, not configuration: `AddBitwardenEnvironment` (a no-op if
  already registered) plus `PostConfigure<SelfHostDetails>`, which runs after the service's own
  `SelfHostDetails` setup, and is added through `ConfigureTestServices`.
- A private `WebApplicationFactory<TEntryPoint>` subclass runs the service. The environment and
  configuration are added in `CreateHost` through `ConfigureHostConfiguration`, which minimal
  hosting passes to the entry point as command line arguments, so `Program` sees them while
  building the host. Settings applied through `ConfigureWebHost` would only arrive after `Program`
  has registered its services.
- Only web services are supported. `WebApplicationFactory` adds a web host and starts it, which fails
  for a host without one.
- Nothing is removed from the service: hosted services and startup validation run like they would
  in any `WebApplicationFactory` test. If that becomes a problem, removing `IHostedService` and
  `IStartupValidator` registrations in `ConfigureTestServices` lets services be collected without
  configuration. Stopping at `Build()` entirely would need a hosting-event capture in place of
  `WebApplicationFactory`, since it needs `Main` to keep running.
- Requirements are read by resolving `IOptions<InfrastructureOptions>` from the started host and
  returned in an `InfrastructureManifest`. The manifest records the requested hosting mode (an unset
  self-host flavor is recorded as `unknown`, matching what `IBitwardenEnvironment` reports) but not
  `Configuration`, which may hold secrets. `ToJsonString()` builds a `JsonObject` by hand so the
  property order is fixed.
- `tests/InfrastructureSampleApp` is the entry point used by the tests. It declares different
  requirements depending on self-hosting, flavor, environment and configuration.
- `VerifySnapshot` writes the snapshot and throws rather than only failing, so updating snapshots is
  "run the tests, review the diff, commit". Relative paths use `[CallerFilePath]`, which relies on
  builds not remapping source paths (no `ContinuousIntegrationBuild` or `PathMap`); absolute paths
  work regardless. `ToJsonString` uses `\n` line endings and the comparison ignores line endings in
  the existing file, so snapshots committed on Windows and Linux agree.
