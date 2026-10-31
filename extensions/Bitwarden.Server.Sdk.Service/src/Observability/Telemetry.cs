using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bitwarden.Server.Sdk.Service.Observability;

/// <summary>
/// Every service's traces, metrics and logs, exported over OTLP.
/// </summary>
/// <remarks>
/// <para>
/// The switches are <c>OpenTelemetry:Enabled</c>, on in the cloud and off when self-hosted
/// (<c>globalSettings:selfHosted</c>), and <c>OpenTelemetry:Tracing:Enabled</c>,
/// <c>:Metrics:Enabled</c> and <c>:Logging:Enabled</c> under it. Where to send them is the
/// exporter's own <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </para>
/// <list type="bullet">
/// <item>Logs are exported as well as traces and metrics.</item>
/// <item>The resource is named by the service's identifier, such as <c>widget-service</c>, rather
/// than its assembly, so a trace names the service as the platform does everywhere else.</item>
/// <item>No Entity Framework instrumentation, and no StackExchange.Redis instrumentation: both are
/// prereleases this stable package can't depend on (NU5104). SqlClient's spans cover SQL Server's
/// commands; Redis commands get no span.</item>
/// <item>No FusionCache instrumentation: it would bring the cache library to every service, and
/// few use it, so their cache reads get no span or meter.</item>
/// </list>
/// <para>
/// The trace itself needs nothing here. ASP.NET Core continues the caller's W3C
/// <c>traceparent</c>, <see cref="HttpClient"/> sends the current one on every call, and the
/// messaging package carries it in each envelope's context and starts the consumer's span under it.
/// What this adds is a listener, without which none of those spans is ever created, and somewhere
/// to send them.
/// </para>
/// <para>
/// No query string is exported. The instrumentation redacts query values by default, and a query
/// here can carry a credential (a sign-in link's token, single sign-on's state and code), so that
/// default is relied on and never turned off.
/// </para>
/// </remarks>
internal static class Telemetry
{
    /// <summary>
    /// Turns every exporter off, or on when self-hosted.
    /// </summary>
    internal const string EnabledKey = "OpenTelemetry:Enabled";

    /// <summary>
    /// Turns the trace exporter on or off, whatever <see cref="EnabledKey"/> says.
    /// </summary>
    internal const string TracingKey = "OpenTelemetry:Tracing:Enabled";

    /// <summary>
    /// Turns the metric exporter on or off, whatever <see cref="EnabledKey"/> says.
    /// </summary>
    internal const string MetricsKey = "OpenTelemetry:Metrics:Enabled";

    /// <summary>
    /// Turns the log exporter on or off, whatever <see cref="EnabledKey"/> says.
    /// </summary>
    internal const string LoggingKey = "OpenTelemetry:Logging:Enabled";

    /// <summary>
    /// The spans of Bitwarden's own code, and the Azure SDK's.
    /// </summary>
    private static readonly string[] Sources = ["Bitwarden.*", "Azure.*"];

    /// <summary>
    /// The meters of Bitwarden's own code.
    /// </summary>
    private static readonly string[] Meters = ["Bitwarden.*", "Bit.*"];

    /// <summary>
    /// The platform's health, readiness and startup probes (SystemEndpoints).
    /// </summary>
    private static readonly string[] Probes = ["/api/v1/health", "/api/v1/readiness", "/api/v1/startup"];

    /// <summary>
    /// Registers the providers, and the exporters unless they are switched off.
    /// </summary>
    /// <param name="services">The service's registrations.</param>
    /// <param name="configuration">Its configuration.</param>
    /// <param name="serviceName">The service's identifier, for the resource.</param>
    /// <param name="export">
    /// False when the process only describes itself (<c>--emit-openapi</c>): it starts, but nothing
    /// it does is telemetry worth sending.
    /// </param>
    internal static void Add(IServiceCollection services, IConfiguration configuration, string serviceName, bool export)
    {
        var exports = Resolve(configuration, export);

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName: serviceName);
                resource.AddTelemetrySdk();
                resource.AddEnvironmentVariableDetector();

                // The Datadog environment, for trace-based metrics.
                if (System.Environment.GetEnvironmentVariable("DD_ENV") is { } environment)
                {
                    resource.AddAttributes([new KeyValuePair<string, object>("env", environment)]);
                }
            })
            .WithTracing(tracing =>
            {
                if (exports.Traces)
                {
                    tracing.AddOtlpExporter();
                }

                // Exceptions as span events, so a failed span says what failed.
                tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true);
                tracing.AddHttpClientInstrumentation();
                tracing.AddSqlClientInstrumentation(options => options.RecordException = true);
                tracing.AddSource(Sources);
            })
            .WithMetrics(metrics =>
            {
                if (exports.Metrics)
                {
                    metrics.AddOtlpExporter();
                }

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddSqlClientInstrumentation();
                metrics.AddMeter(Meters);
            })
            .WithLogging(
                logging =>
                {
                    if (exports.Logs)
                    {
                        logging.AddOtlpExporter();
                    }
                },
                options =>
                {
                    options.IncludeFormattedMessage = true;

                    // The organization, user and provider a request is about (ContextLogScope).
                    options.IncludeScopes = true;
                });

        // PostConfigure, so a filter a service sets itself still runs.
        services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            // An orchestrator asks these constantly; a span for each is noise and cost. Filtered
            // as the request starts, which drops its children too.
            var filter = options.Filter;
            options.Filter = http => !IsProbe(http.Request) && (filter is null || filter(http));
        });
    }

    /// <summary>
    /// Which exporters a configuration turns on: each signal's own switch, else
    /// <see cref="EnabledKey"/>, else on in the cloud and off when self-hosted. None while the process only describes itself, whatever the switches say.
    /// </summary>
    /// <param name="configuration">The service's configuration.</param>
    /// <param name="export">False when the process only describes itself (<c>--emit-openapi</c>).</param>
    internal static Exports Resolve(IConfiguration configuration, bool export)
    {
        if (!export)
        {
            return new Exports(Traces: false, Metrics: false, Logs: false);
        }

        var enabled = configuration.GetValue(EnabledKey, !configuration.GetValue(SelfHosting.Key, false));

        return new Exports(
            Traces: configuration.GetValue(TracingKey, enabled),
            Metrics: configuration.GetValue(MetricsKey, enabled),
            Logs: configuration.GetValue(LoggingKey, enabled));
    }

    /// <summary>
    /// Whether a request is one of the platform's probes.
    /// </summary>
    internal static bool IsProbe(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && Probes.Any(probe => request.Path.Equals(probe, StringComparison.OrdinalIgnoreCase));
}
