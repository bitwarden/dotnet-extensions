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

## Broker setup

The library does not provision broker topology. For the RabbitMQ and Azure Service Bus backends,
the exchanges, queues, topics, subscriptions, and related policies must be declared on the broker
before the application starts — typically as part of the broker container's bootstrap or via
infrastructure-as-code (Terraform, Pulumi, broker-specific CLIs).

For each publisher and subscriber registered with `AddPublisher<T>(topic)` /
`AddSubscriber<T>(topic, subscription)`, declare the following on the broker:

### Azure Service Bus

For each unique `topic` across all publishers and subscribers:

- A topic named `{topic}`.

For each `AddSubscriber<T>(topic, subscription)`:

- A subscription named `{subscription}` on that topic. Set `MaxDeliveryCount` on the
  subscription to cap redelivery attempts; the subscription's built-in dead-letter queue
  handles exhausted or explicitly dead-lettered messages automatically.

### RabbitMQ

For each unique `topic` across all publishers and subscribers:

- A durable fanout exchange named `{topic}`.

For each `AddSubscriber<T>(topic, subscription)`:

- A durable quorum queue named `{topic}.{subscription}`, bound to the topic exchange with
  an empty routing key.
- A server-side policy applying `delivery-limit` to the queue, capping the number of
  redelivery attempts before a message is dead-lettered.
- A server-side policy applying `dead-letter-exchange` to the queue, plus the target
  dead-letter exchange and a queue bound to it. Without this policy, messages that call
  `Envelope<T>.DeadLetterAsync` or exceed `delivery-limit` are silently dropped by
  RabbitMQ.

Policies must be used rather than queue arguments because quorum queue arguments cannot be
changed on an existing queue. See the RabbitMQ documentation on
[quorum queue poison-message handling](https://www.rabbitmq.com/docs/quorum-queues#poison-message-handling)
and [policies](https://www.rabbitmq.com/docs/policies) for the full command reference, and
`management.load_definitions` for declarative bootstrap via a `definitions.json` file loaded
at broker startup.

#### Example

Using `rabbitmqadmin` and `rabbitmqctl` for an application that registers
`AddPublisher<OrderCreated>("orders")` and `AddSubscriber<OrderCreated>("orders", "workers")`:

```bash
# Topic exchange, subscription queue, binding.
rabbitmqadmin exchanges declare --name "orders" --type "fanout" --durable true
rabbitmqadmin queues declare --name "orders.workers" --type "quorum" --durable true
rabbitmqadmin bindings declare --source "orders" \
  --destination-type "queue" --destination "orders.workers"

# Dead-letter exchange and queue. The names must not match the policy pattern
# below, which would otherwise re-apply the dead-letter policy to the dead-letter
# queue and cycle its messages back through itself.
rabbitmqadmin exchanges declare --name "dead-letter.orders" --type "fanout" --durable true
rabbitmqadmin queues declare --name "dead-letter.orders" --type "quorum" --durable true
rabbitmqadmin bindings declare --source "dead-letter.orders" \
  --destination-type "queue" --destination "dead-letter.orders"

# Policy applying delivery-limit and dead-letter-exchange to the orders.*
# subscription queues.
rabbitmqctl set_policy orders-subscriptions "^orders\." \
  '{"delivery-limit":10,"dead-letter-exchange":"dead-letter.orders"}' \
  --apply-to quorum_queues
```

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

### With `IMessageConsumer<T>` (recommended)

Implement `IMessageConsumer<T>` and register with `AddMessageConsumer`. This registers both the
subscriber and a hosted service in one call, and handles settlement automatically — `CompleteAsync`
on success, `RequeueAsync` when `HandleAsync` throws:

```csharp
// Registration
services.AddMessageConsumer<OrderCreated, OrderNotificationService>("orders", subscriptionName: "notifications");

// Implementation
public class OrderNotificationService(IEmailService email) : IMessageConsumer<OrderCreated>
{
    public async Task HandleAsync(Envelope<OrderCreated> envelope, CancellationToken cancellationToken)
    {
        await email.SendAsync(envelope.Message, cancellationToken);
        // No need to call CompleteAsync/RequeueAsync — the framework settles the envelope.
    }
}
```

The consumer is registered as a singleton so it can be resolved by type in tests:

```csharp
var consumer = host.Services.GetRequiredService<OrderNotificationService>();
```

Additional constructor parameters are resolved from the container automatically.

### With `ISubscriber<T>` directly

For more control — custom retry logic, dead-lettering after N deliveries, or consuming outside a
`BackgroundService` — inject `ISubscriber<T>` as a keyed service and iterate it yourself. Each message
must be either completed or requeued before the next is requested:

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
            catch (Exception ex)
            {
                await envelope.RequeueAsync(ex.Message, stoppingToken);
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
