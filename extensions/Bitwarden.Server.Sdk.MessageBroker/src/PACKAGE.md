# Bitwarden.Server.Sdk.MessageBroker

## About

This package provides a transport-agnostic publish/subscribe API for sending and receiving messages
between Bitwarden services. A single programming model works across three backends — Azure Service
Bus, RabbitMQ, and an in-memory channel — selected at runtime through configuration.

## Setup

Register publishers and subscribers in `Program.cs` or your `IServiceCollection` setup:

```csharp
// Publish to a topic
services.AddPublisher<OrderCreated>("orders");

// Subscribe to a topic (work-queue: all "workers" instances compete for each message)
services.AddSubscriber<OrderCreated>("orders", subscriptionName: "workers");

// Subscribe with a unique subscription name (pub/sub: each group receives every message independently)
services.AddSubscriber<OrderCreated>("orders", subscriptionName: "notifications");
services.AddSubscriber<OrderCreated>("orders", subscriptionName: "analytics");
```

Then bind the transport from configuration:

```csharp
services.AddOptions<MessagingOptions>().BindConfiguration("");
```

```json
{
  "AzureServiceBusConnectionString": "Endpoint=sb://...",
  "RabbitUri": "amqp://guest:guest@localhost/"
}
```

If neither connection string is set the package falls back to an in-memory channel, which is useful
for local development and testing but loses all messages on process restart. Only one backend may be
configured at a time; setting both raises a validation error at startup.

## Publishing

Inject `IPublisher<T>` as a keyed service using the topic name:

```csharp
public class OrderService(
    [FromKeyedServices("orders")] IPublisher<OrderCreated> publisher)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        // ... create order ...
        await publisher.PublishAsync(new OrderCreated(order.Id), ct);
    }
}
```

To publish multiple messages efficiently use `PublishBatchAsync`:

```csharp
await publisher.PublishBatchAsync(events, ct);
```

## Consuming

### With `MessageConsumer<T>` (recommended)

Extend `MessageConsumer<T>` and register with `AddMessageConsumer`. This registers both the subscriber
and a hosted service in one call, and handles settlement automatically — `CompleteAsync` on success,
`AbandonAsync` when `HandleAsync` throws:

```csharp
// Registration
services.AddMessageConsumer<OrderCreated, OrderNotificationService>("orders", subscriptionName: "notifications");

// Implementation
public class OrderNotificationService(ISubscriber<OrderCreated> subscriber) : MessageConsumer<OrderCreated>(subscriber)
{
    protected override async Task HandleAsync(Envelope<OrderCreated> envelope, CancellationToken cancellationToken)
    {
        await SendEmailAsync(envelope.Message, cancellationToken);
        // No need to call CompleteAsync/AbandonAsync — the base class does it.
    }
}
```

The consumer is registered as a singleton so it can be resolved by type in tests:

```csharp
var consumer = host.Services.GetRequiredService<OrderNotificationService>();
```

If the constructor needs additional services they are resolved from the container:

```csharp
public class OrderNotificationService(ISubscriber<OrderCreated> subscriber, IEmailService email)
    : MessageConsumer<OrderCreated>(subscriber)
```

### With `ISubscriber<T>` directly

For more control — custom retry logic, dead-lettering after N deliveries, or consuming outside a
`BackgroundService` — inject `ISubscriber<T>` as a keyed service and iterate it yourself. Each message
must be either completed or abandoned before the next is requested:

```csharp
public class OrderNotificationService(
    [FromKeyedServices("orders/notifications")] ISubscriber<OrderCreated> subscriber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in subscriber.SubscribeAsync(stoppingToken))
        {
            try
            {
                await SendEmailAsync(envelope.Message, stoppingToken);
                await envelope.CompleteAsync(stoppingToken);
            }
            catch (Exception)
            {
                await envelope.AbandonAsync(stoppingToken);
            }
        }
    }
}
```

### Envelope properties

Each message is wrapped in an `Envelope<T>` that carries broker metadata:

| Property | Description |
|---|---|
| `Message` | The deserialized message. |
| `MessageId` | Unique identifier assigned by the publisher. |
| `TraceId` | W3C traceparent of the publish span, for linking consumer traces to producer traces. |
| `DeliveryCount` | Number of times this message has been delivered. `1` on the first attempt, incrementing with each redelivery. |

### Stopping gracefully

For Azure Service Bus and RabbitMQ, unacknowledged messages are automatically requeued by the
broker when the host shuts down.

For the in-memory channel backend, consumers registered with `AddMessageConsumer` are protected by
an escrow mechanism: any messages left in the channel when the host stops are written to an
`IMessageEscrowStore` and replayed into the channel the next time the host starts. Register an
implementation to enable durable recovery:

```csharp
services.AddSingleton<IMessageEscrowStore, MyDatabaseEscrowStore>();
```

Without a registered `IMessageEscrowStore`, undelivered messages are logged as errors and
discarded. Consumers should be idempotent — escrow provides at-least-once delivery rather than
exactly-once.

## Observability

The package emits `System.Diagnostics.Activity` spans and `System.Diagnostics.Metrics` counters that
integrate with any OpenTelemetry-compatible pipeline.

**Traces** — source name `Bitwarden.Server.Sdk.MessageBroker`:

| Operation | Kind | Description |
|---|---|---|
| `{topic} publish` | Producer | One span per publish call, or one per batch. |
| `{topic} receive` | Consumer | One span per message delivered, linked to the producer span via `TraceId`. |

**Metrics** — meter name `Bitwarden.Server.Sdk.MessageBroker`:

| Instrument | Type | Description |
|---|---|---|
| `messaging.client.published.messages` | Counter | Messages published, tagged with `messaging.destination.name`. |
| `messaging.client.consumed.messages` | Counter | Messages delivered to a consumer, tagged with `messaging.destination.name`. |
| `messaging.channel.queued.messages` | Gauge | Current number of messages buffered in the in-memory channel, tagged with `messaging.destination.name`. |

## Serialization

Messages are serialized as JSON using `System.Text.Json`. Customize serializer options per topic:

```csharp
services.Configure<MessageBrokerSerializerOptions>("orders", options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});
```
