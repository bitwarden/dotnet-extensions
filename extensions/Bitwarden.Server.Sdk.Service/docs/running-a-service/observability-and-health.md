# Observability and health

Every service exports traces, metrics and logs over OTLP, tags them with whom a request concerns,
and answers the health probes an orchestrator asks. Name your own sources `Bitwarden.*` and register
health checks the usual way; the package does the rest.

## Add traces, metrics and logs

Name your own activity sources and meters `Bitwarden.*`, and the platform exports them without
further registration. Register health checks the usual way, and readiness asks them:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.Service;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<WidgetPolisher>();

        // Asked by GET /api/v1/readiness; unhealthy answers 503.
        services.AddHealthChecks().AddCheck<WidgetStoreHealthCheck>("widget-store");
    }
}

internal sealed class WidgetPolisher(ILogger<WidgetPolisher> logger)
{
    // Named "Bitwarden.*", so the platform exports them without further registration.
    private static readonly ActivitySource Source = new("Bitwarden.Widgets");
    private static readonly Meter Meter = new("Bitwarden.Widgets");
    private static readonly Counter<long> Polished = Meter.CreateCounter<long>("widgets.polished");

    public void Polish(string widgetId)
    {
        using var activity = Source.StartActivity("Polish widget");
        activity?.SetTag("widget.id", widgetId);

        // The request's OrganizationId, UserId and ProviderId travel with this record as its
        // scope. Log identifiers only: never an encrypted member's value.
        logger.LogInformation("Polished widget {WidgetId}.", widgetId);
        Polished.Add(1);
    }
}

internal sealed class WidgetStoreHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        // The description is what readiness reports; exception text never is.
        Task.FromResult(HealthCheckResult.Unhealthy("The widget store is not reachable."));
}
```

What the package exports for you:

- **The resource** is named by the service's identifier, with `env` from the `DD_ENV` environment
  variable. The exporter reads the standard `OTEL_*` variables, such as
  `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **Traces** from ASP.NET Core (exceptions recorded as span events), `HttpClient`, SqlClient, and
  every `Bitwarden.*` and `Azure.*` activity source. A caller's W3C `traceparent` continues in the
  service, and every outgoing `HttpClient` call carries it.
- **Metrics** from ASP.NET Core, `HttpClient`, SqlClient, the runtime, and every `Bitwarden.*` and
  `Bit.*` meter.
- **Logs** with their formatted message and scopes.
- **Nothing** while the process only writes its OpenAPI document (`--emit-openapi`).

## Know who a record concerns

Each request gets a log scope naming `OrganizationId`, `UserId` and `ProviderId`, only those
present. The same values are set on the request's span as `bitwarden.organization_id`,
`bitwarden.user_id` and `bitwarden.provider_id`. You don't add them yourself.

The scope reads `IContext` as each message is written, so a message logged before authentication
carries nothing.

## Answer the health probes

The package maps three probes. They are anonymous, tagged `System` in the OpenAPI document, and
never traced.

| Probe                   | Answers                                                                                       |
| ----------------------- | --------------------------------------------------------------------------------------------- |
| `GET /api/v1/health`    | `200` whenever the process is serving. Asks no health check.                                  |
| `GET /api/v1/startup`   | `200` once the process is serving. Asks no health check.                                      |
| `GET /api/v1/readiness` | `200` when every registered health check is healthy or degraded, `503` when any is unhealthy. |

- The package doesn't check your dependencies. Add a check for each dependency the service can't
  serve without.
- When readiness answers `503`, its detail joins the descriptions of the unhealthy checks. Write a
  description an operator can act on.
- A check that failed with an exception, or has no description, is reported as "A dependency is not
  ready.", so exception text never reaches the answer.

## Settings

| Setting                                                     | Default                                       | What it does                                                                        |
| ----------------------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------------------- |
| `OpenTelemetry:Enabled`                                     | `true` in the cloud, `false` when self-hosted | Turns the OTLP exporters on or off.                                                 |
| `OpenTelemetry:Tracing:Enabled`                             | `OpenTelemetry:Enabled`                       | Turns the trace exporter on or off.                                                 |
| `OpenTelemetry:Metrics:Enabled`                             | `OpenTelemetry:Enabled`                       | Turns the metric exporter on or off.                                                |
| `OpenTelemetry:Logging:Enabled`                             | `OpenTelemetry:Enabled`                       | Turns the log exporter on or off.                                                   |
| `DD_ENV` (environment variable)                             | None                                          | Added to the telemetry resource as `env`.                                           |
| `Logging:Console:IncludeScopes`                             | `true`                                        | Writes each request's log scope on console log lines. A deployment can turn it off. |
| `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics` | `Warning`                                     | Keeps ASP.NET Core's request lines, which carry the query string, out of the logs.  |

The switches turn exporters on or off only. Spans are created either way, so a trace continues
through a service that exports nothing.

## Security trade-offs

- **Request lines.** ASP.NET Core's "Request starting" and "Request finished" lines carry the query
  string, which can carry a credential (a sign-in link's token, a download link's token). The
  package defaults their category to `Warning`. Set it lower only where no query string ever
  carries a credential.
- **Log scopes** carry organization, user and provider identifiers into every log record and span.
  They are identifiers, not Vault Data, but they say who did what: protect the log and trace
  backends accordingly.

## Things to know

- **What you log is up to you.** The package adds identifiers; your messages add the rest. Never log
  a token, a key or the value of an `[Encrypted]` member. Vault Data reaches a service only as
  Protected Data, and must not leave it in a log.
- **Some libraries aren't instrumented.** Entity Framework Core and StackExchange.Redis are not.
  SqlClient's spans cover SQL Server. To add other instrumentation, use OpenTelemetry's own
  registration in `ConfigureServices`.

## See also

- [Defining a service](defining-a-service.md)
- [Caller context](caller-context.md)
- [Configuration and self-hosting](configuration-and-self-hosting.md)
