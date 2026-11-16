namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Records a (topic, payload-family) pair and an optional subscription name associated with an
/// <c>AddPublisher</c> or <c>AddSubscriber</c> registration. Enumerated from DI by this package's test
/// harnesses, which declare the broker topology a test needs; not consumed by the library at runtime.
/// Tooling outside the package reads the same facts from the infrastructure requirements each
/// registration declares (<see cref="MessagingRequirements"/>); a test keeps the two in step.
/// Each backend maps these names to its own vocabulary (RabbitMQ: topic → topic exchange, family
/// → routing key, subscription → queue bound to that exchange with the family routing key; Azure
/// Service Bus: topic → topic, family → Correlation filter value, subscription → subscription).
/// </summary>
internal sealed record BrokerTopologyDeclaration(string TopicName, string FamilyWireName, string? SubscriptionName = null);

/// <summary>
/// Records a command family sent to, or consumed from, a command queue by an
/// <c>AddCommandSender</c> or <c>AddCommandConsumer</c> registration. Like
/// <see cref="BrokerTopologyDeclaration"/>, for this package's test harnesses; not consumed by the
/// library at runtime. A command queue is one queue, owned by its receiver, carrying every
/// command family the receiver accepts (RabbitMQ: a queue reached through an exchange of the same
/// name bound only to it; Azure Service Bus: a queue).
/// </summary>
internal sealed record CommandQueueDeclaration(string QueueName, string FamilyWireName, bool IsConsumer);
