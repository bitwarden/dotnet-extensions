# Bitwarden.Server.Sdk.HealthChecks

## About

This package provides a flexible health reporting system that integrates with ASP.NET Core's health
checks infrastructure. It allows your application to dynamically report health status changes at
runtime, making it easy to signal temporary degradations or critical failures that should affect
your application's health endpoints.

## Usage

### Basic Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBitwardenHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
```

### Reporting Health Status

Inject `IHealthReporter` into your services to report health status changes:

```csharp
public class MyService(IHealthReporter healthReporter, IExternalApi externalApi)
{
    public async Task ProcessAsync()
    {
        try
        {
            await externalApi.CallAsync();
        }
        catch (ExternalApiTimeoutException)
        {
            // Report temporary degradation that auto-clears when disposed
            using var degradation = healthReporter.ReportDegradation("external-api", "Timeout encountered, will retry.");

            // Retry with fallback logic
            await RetryWithFallbackAsync();
        }
        catch (ExternalApiDownException)
        {
            // Report critical unhealthy status that persists
            healthReporter.ReportUnhealthy("external-api", "Appears to be unavailable.");
            throw;
        }
    }
}
```

### Early Access to Health Reporter

For scenarios where you need to report health status during service registration (before the service provider is built), use `GetHealthReporter()`:

```csharp
builder.Services.AddBitwardenHealthChecks();

// Validate required configuration settings
var apiKey = builder.Configuration["ExternalApi:ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    var healthReporter = builder.Services.GetHealthReporter();
    healthReporter.ReportUnhealthy("my-service", "API Key not found in configuration.");
}

// Configure services based on validated settings
builder.Services.AddSingleton<IExternalApiClient>(
    new ExternalApiClient(apiKey ?? "invalid")
);
```

## Key Concepts

### Degradation vs. Unhealthy

The package supports two types of health status reports:

- **Degradation** (`ReportDegradation`): Temporary, recoverable issues. The degradation automatically clears when the returned `IDisposable` is disposed. Use this for transient failures, slow responses, or partial functionality.

- **Unhealthy** (`ReportUnhealthy`): Persistent, critical issues. The unhealthy status remains until
the application restarts. Use this for unrecoverable failures, missing dependencies, or critical configuration errors.

### The "Reported" Health Check

When you call `AddBitwardenHealthChecks()`, a health check named "Ad-hoc" is automatically
registered. This health check:

- Returns `Healthy` when no issues are reported
- Returns `Degraded` when one or more degradations are active
- Returns `Unhealthy` when one or more unhealthy statuses are reported
- Prioritizes unhealthy over degraded status

## Advanced Usage

### Integration with Other Health Checks

The "Reported" health check works alongside standard ASP.NET Core health checks:

```csharp
builder.Services
    .AddBitwardenHealthChecks()
    .AddHealthChecks()
    .AddCheck("database", () => CheckDatabaseConnection())
    .AddCheck("redis", () => CheckRedisConnection());

// Both the "Ad-hoc" health check and custom checks are evaluated
app.MapHealthChecks("/healthz");
```

## Best Practices

1. **⚠️ Avoid self-inflicted DDOS with orchestrators**: If your application runs in an orchestrator
(like Kubernetes) configured to restart unhealthy pods, be extremely careful when reporting
unhealthy status for third-party service failures. If an external service (database, API, etc.) goes
down and all your instances report unhealthy and restart, they will immediately check the still-down
service, report unhealthy again, and restart in a loop. This creates a self-inflicted DDOS where
your entire fleet continuously restarts. Consider using `ReportDegradation` for external
dependencies instead, or only report unhealthy for issues with your own application that a restart
could actually fix.

2. **Use degradation for temporary issues**: If the issue might resolve itself or is being handled
with retry logic, use `ReportDegradation`.

3. **Use unhealthy for critical failures**: If the issue requires manual intervention or application
restart, use `ReportUnhealthy`.

4. **Identify issue**: Use a issue identifier that is unique to your system so that it can easily be
identified when issues are encountered.

5. **Dispose degradations properly**: Consider using `using` statements or ensure `Dispose()` is
called to clear degradations when the issue is resolved.
