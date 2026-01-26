# Bitwarden.Server.Sdk.HealthChecks

Dynamic health status reporting for ASP.NET Core health checks.

- `IHealthReporter` - Service for reporting health status changes at runtime
- `InMemoryHealthReporter` - In-memory implementation of `IHealthReporter`
- `ReportedHealthCheck` - Health check that evaluates reported health events
- `HealthCheckServiceCollectionExtensions` - DI setup with `AddBitwardenHealthChecks()` and `GetHealthReporter()`
- `ConfigureHealthCheckServiceOptions` - Automatically registers the "Reported" health check

## Key Features

- **Temporary degradations**: Report recoverable issues that auto-clear when disposed
- **Persistent unhealthy status**: Report critical failures that require intervention
- **Tag-based categorization**: Organize health reports by component or subsystem
- **Early access pattern**: Report health status during service registration
- **Integration with ASP.NET Core**: Works alongside standard health checks
