using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

internal static class MessageBrokerActivitySource
{
    internal const string Name = "Bitwarden.Server.Sdk.MessageBroker";
    // TODO: Replace with an injected ActivitySource obtained from IActivitySourceFactory once that
    // API ships (expected .NET 11). The static source means all tests share a single global listener
    // target, forcing snapshot-based isolation in the tracing tests to avoid cross-test pollution.
    internal static readonly ActivitySource Source = new(Name);

    internal static Activity? StartProducerActivity(string topicName, string familyWireName)
    {
        var activity = Source.StartActivity($"{topicName} publish", ActivityKind.Producer);
        activity?.SetTag(MessageBrokerMetrics.DestinationTag, topicName);
        activity?.SetTag(MessageBrokerMetrics.FamilyTag, familyWireName);
        return activity;
    }

    internal static Activity? StartConsumerActivity(string topicName, string familyWireName, string? traceId)
    {
        ActivityContext parentContext = default;
        if (traceId is not null)
            ActivityContext.TryParse(traceId, null, isRemote: true, out parentContext);
        var activity = Source.StartActivity($"{topicName} receive", ActivityKind.Consumer, parentContext);
        activity?.SetTag(MessageBrokerMetrics.DestinationTag, topicName);
        activity?.SetTag(MessageBrokerMetrics.FamilyTag, familyWireName);
        return activity;
    }
}
