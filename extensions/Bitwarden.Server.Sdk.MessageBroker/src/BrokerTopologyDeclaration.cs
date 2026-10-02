namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Records a topic (and optional subscription) name associated with an <c>AddPublisher</c> or
/// <c>AddSubscriber</c> registration. Enumerated from DI by test harnesses and external tooling
/// that need the expected topology; not consumed by the library at runtime. Each backend maps
/// these names to its own vocabulary (RabbitMQ: topic → exchange, subscription → queue bound to
/// that exchange; Azure Service Bus: topic → topic, subscription → subscription).
/// </summary>
internal sealed record BrokerTopologyDeclaration(string TopicName, string? SubscriptionName = null);
