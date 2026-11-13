namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Records a (topic, payload-family) pair and an optional subscription name associated with an
/// <c>AddPublisher</c> or <c>AddSubscriber</c> registration. Enumerated from DI by test harnesses
/// and external tooling that need the expected topology; not consumed by the library at runtime.
/// Each backend maps these names to its own vocabulary (RabbitMQ: topic → topic exchange, family
/// → routing key, subscription → queue bound to that exchange with the family routing key; Azure
/// Service Bus: topic → topic, family → Correlation filter value, subscription → subscription).
/// </summary>
internal sealed record BrokerTopologyDeclaration(string TopicName, string FamilyWireName, string? SubscriptionName = null);
