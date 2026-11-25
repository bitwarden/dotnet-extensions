namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Records a (topic, payload-family) pair and an optional subscription name associated with an
/// <c>AddPublisher</c> or <c>AddSubscriber</c> registration. Enumerated from DI by this package's test
/// harnesses; not consumed by the library at runtime.
/// Each backend maps these names to its own vocabulary (RabbitMQ: topic → topic exchange, family
/// → routing key, subscription → queue bound to that exchange with the family routing key; Azure
/// Service Bus: topic → topic, family → Correlation filter value, subscription → subscription).
/// </summary>
internal sealed record BrokerTopologyDeclaration(string TopicName, string FamilyWireName, string? SubscriptionName = null);

/// <summary>
/// Records a command family sent to, or consumed from, a command queue by an
/// <c>AddCommandSender</c> or <c>AddCommandConsumer</c> registration. Like
/// <see cref="BrokerTopologyDeclaration"/>, for this package's test harnesses; not consumed by the
/// library at runtime.
/// </summary>
internal sealed record CommandQueueDeclaration(string QueueName, string FamilyWireName, bool IsConsumer);
