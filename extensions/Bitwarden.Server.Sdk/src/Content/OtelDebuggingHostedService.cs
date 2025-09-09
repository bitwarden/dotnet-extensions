#if BIT_INCLUDE_TELEMETRY

using System.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Internal;

internal sealed class OtelDebuggingHostedService(ILoggerFactory loggerFactory) : IHostedService
{
    // Start this on creation of the hosted service so we get events earlier than StartAsync
    private readonly OtelEventListener _listener = new(loggerFactory);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private class OtelEventListener(ILoggerFactory loggerFactory) : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "OpenTelemetry-Sdk")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
            else if (eventSource.Name == "OpenTelemetry-Exporter-OpenTelemetryProtocol")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
            else if (eventSource.Name == "OpenTelemetry-Extensions-Hosting")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            loggerFactory
                .CreateLogger(eventData.EventSource.Name)
                .LogInformation("Message: {Data}. Payload: {Payload}", eventData.Message, eventData.Payload);
        }
    }
}
#endif
