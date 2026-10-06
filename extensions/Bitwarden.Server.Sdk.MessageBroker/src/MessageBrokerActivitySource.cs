using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

internal static class MessageBrokerActivitySource
{
    internal const string Name = "Bitwarden.Server.Sdk.MessageBroker";
    // TODO: Replace with an injected ActivitySource obtained from IActivitySourceFactory once that
    // API ships (expected .NET 11). The static source means all tests share a single global listener
    // target, forcing snapshot-based isolation in the tracing tests to avoid cross-test pollution.
    internal static readonly ActivitySource Source = new(Name);

    internal static Activity? StartConsumerActivity(string topicName, string? traceId)
    {
        ActivityContext parentContext = default;
        if (traceId is not null)
            ActivityContext.TryParse(traceId, null, isRemote: true, out parentContext);
        return Source.StartActivity($"{topicName} receive", ActivityKind.Consumer, parentContext);
    }
}
