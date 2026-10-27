# Bitwarden.Server.Sdk.Infrastructure.Testing

Collects the infrastructure a service declares by running its startup.

- `InfrastructureCollector` - `CollectInfrastructure<TEntryPoint>()` entry point
- `InfrastructureCollectionOptions` - Self-hosted, environment and configuration settings for the run

## Design

- A private `WebApplicationFactory<TEntryPoint>` subclass runs the service. Settings are added in
  `CreateHost` through `ConfigureHostConfiguration`, which minimal hosting passes to the entry point
  as command line arguments, so `Program` sees them while building the host. Settings applied
  through `ConfigureWebHost` would only arrive after `Program` has registered its services.
- `IHostedService` and `IStartupValidator` registrations are removed so startup has no side effects.
- Requirements are read by resolving `IOptions<InfrastructureOptions>` from the started host.
- `tests/InfrastructureSampleApp` is the entry point used by the tests. It declares different
  requirements depending on self-hosting, environment and configuration, and registers a hosted
  service and options validation that fail if they run.
