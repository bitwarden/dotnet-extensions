# Bitwarden.Server.Sdk.MessageBroker

## About

This package provides a transport-agnostic publish/subscribe API for sending and receiving messages
between Bitwarden services. A single programming model works across three backends — Azure Service
Bus, RabbitMQ, and an in-memory channel — selected at runtime through configuration. Messages are
modeled as versioned **payload families**: publishers send every known variant of a payload in a
single message, and each subscriber picks the highest variant it can decode.

## Getting started

the minimal end-to-end is a single-variant payload, a publisher, and a consumer:

```csharp
// 1. Define a payload family. ISole is a payload with only one variant.
public sealed record Ping(string Text) : PingPayload.ISole;

public sealed class PingPayload
    : Payload<PingPayload, Ping, Ping>, IPayloadVariants<PingPayload>
{
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(Ping), "v1")];
}

// AOT / trimmed builds only
[JsonSerializable(typeof(Ping))]
public partial class PingJsonContext : JsonSerializerContext;

// 2. Register at startup.
services.AddOptions<MessagingOptions>().BindConfiguration("");
// AOT / trimmed builds only
services.Configure<MessageBrokerSerializerOptions>("pings", o =>
    o.JsonSerializerOptions.TypeInfoResolverChain.Add(PingJsonContext.Default)); // AOT only
services.AddPublisher<PingPayload, Ping>("pings");
services.AddMessageConsumer<PingPayload, Ping, PingHandler>("pings", subscriptionName: "workers");

// 3. Publish and consume.
public class Pinger([FromKeyedServices("pings")] Publisher<PingPayload, Ping> publisher)
{
    public Task PingAsync(string text, CancellationToken ct) =>
        publisher.Publish(new Ping(text)).SendAsync(ct);
}

public class PingHandler : IMessageConsumer<PingPayload, Ping>
{
    public Task HandleAsync(Envelope<PingPayload, Ping> envelope, CancellationToken ct)
    {
        Console.WriteLine(envelope.Payload.Text);
        return Task.CompletedTask;
    }
}
```

## Setup

Bind the transport from configuration:

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

## Defining a payload

A payload is a family of variants forming a chain from the oldest known shape (**floor**) to the
newest (**ceiling**). Derive from `Payload<TSelf, TFloor, TCeiling>` and implement
`IPayloadVariants<TSelf>`, whose static `Variants` list pairs each variant type with a stable wire
name:

```csharp
public sealed class OrderPayload
    : Payload<OrderPayload, OrderPayload.V1, OrderPayload.V2>,
      IPayloadVariants<OrderPayload>
{
    public static IReadOnlyList<(Type Type, string WireName)> Variants =>
    [
        (typeof(V1), "v1"),
        (typeof(V2), "v2"),
    ];

    // Floor: the oldest shape. Declares a pure upcast to V2.
    public sealed record V1(Guid OrderId) : Payload<OrderPayload>.IFloor<V2>
    {
        public V2 Upcast() => new(OrderId, CustomerId: null);
    }

    // Ceiling: the current shape. Declares a pure downcast to V1.
    public sealed record V2(Guid OrderId, Guid? CustomerId)
        : Payload<OrderPayload>.IPureCeiling<V1>
    {
        public V1 Downcast() => new(OrderId);
    }
}
```

Wire names are decoupled from CLR type names so variants can be renamed or moved without breaking
messages already on the wire. Each topic is bound to exactly one payload family at DI registration,
so wire names are scoped by topic and only need to be unique within that payload's own `Variants` list.

## Publishing

Register a publisher with `AddPublisher<TPayload, TCeiling>`:

```csharp
services.AddPublisher<OrderPayload, OrderPayload.V2>("orders");
```

Inject `Publisher<TPayload, TCeiling>` as a keyed service using the topic name and call `Publish`
followed by `SendAsync`:

```csharp
public class OrderService(
    [FromKeyedServices("orders")] Publisher<OrderPayload, OrderPayload.V2> publisher)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        await publisher
            .Publish(new OrderPayload.V2(order.Id, order.CustomerId))
            .SendAsync(ct);
    }
}
```

If a downcast between the ceiling and the floor needs caller-supplied data (an "assisted"
downcast), chain a `.With(assist)` call for each such crossing before `SendAsync`. The compiler
refuses `SendAsync` until every assisted downcast has been crossed, so a publisher cannot omit data
an older subscriber needs. See the [package README][readme-payload-versioning] for the full interface
catalog and assisted-downcast walkthrough.

[readme-payload-versioning]: https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.MessageBroker/src/README.md#payload-versioning

## Consuming

### With `IMessageConsumer<TPayload, TCeiling>` (recommended)

Implement `IMessageConsumer<TPayload, TCeiling>` and register with `AddMessageConsumer`. This
registers both the subscriber and a hosted service in one call, and handles settlement
automatically — `CompleteAsync` on success, `RequeueAsync` when `HandleAsync` throws:

```csharp
// Registration
services.AddMessageConsumer<OrderPayload, OrderPayload.V2, OrderNotificationService>(
    "orders", subscriptionName: "notifications");

// Implementation
public class OrderNotificationService(IEmailService email)
    : IMessageConsumer<OrderPayload, OrderPayload.V2>
{
    public async Task HandleAsync(
        Envelope<OrderPayload, OrderPayload.V2> envelope,
        CancellationToken cancellationToken)
    {
        await email.SendAsync(envelope.Payload, cancellationToken);
        // No need to call CompleteAsync/RequeueAsync — the framework settles the envelope.
    }
}
```

`envelope.Payload` is always the consumer's declared ceiling: a direct match if the publisher sent
that variant, otherwise upcast from the highest lower variant that arrived on the wire.

The consumer is registered as a singleton so it can be resolved by type in tests:

```csharp
var consumer = host.Services.GetRequiredService<OrderNotificationService>();
```

Additional constructor parameters are resolved from the container automatically.

### With `ISubscriber<TPayload, TCeiling>` directly

For more control — custom retry logic, dead-lettering after N deliveries, or consuming outside a
`BackgroundService` — inject `ISubscriber<TPayload, TCeiling>` as a keyed service and iterate it
yourself. Each message must be either completed or requeued before the next is requested:

```csharp
public class OrderNotificationService(
    [FromKeyedServices("orders/notifications")]
    ISubscriber<OrderPayload, OrderPayload.V2> subscriber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in subscriber.SubscribeAsync(stoppingToken))
        {
            try
            {
                await SendEmailAsync(envelope.Payload, stoppingToken);
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

Register the subscription with `AddSubscriber<TPayload, TCeiling>("orders", subscriptionName: "notifications")`.

### Envelope properties

Each message is wrapped in an `Envelope<TPayload, TCeiling>` that carries the resolved payload and
broker metadata:

| Property        | Description                                                                                                   |
| --------------- | ------------------------------------------------------------------------------------------------------------- |
| `Payload`       | The resolved variant at `TCeiling` (see above).                                                               |
| `MessageId`     | Unique identifier assigned by the publisher.                                                                  |
| `TraceId`       | W3C traceparent of the publish span, for linking consumer traces to producer traces.                          |
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

| Operation         | Kind     | Description                                                                |
| ----------------- | -------- | -------------------------------------------------------------------------- |
| `{topic} publish` | Producer | One span per publish call.                                                 |
| `{topic} receive` | Consumer | One span per message delivered, linked to the producer span via `TraceId`. |

**Metrics** — meter name `Bitwarden.Server.Sdk.MessageBroker`:

| Instrument                            | Type    | Description                                                                                             |
| ------------------------------------- | ------- | ------------------------------------------------------------------------------------------------------- |
| `messaging.client.published.messages` | Counter | Messages published, tagged with `messaging.destination.name`.                                           |
| `messaging.client.consumed.messages`  | Counter | Messages delivered to a consumer, tagged with `messaging.destination.name`.                             |
| `messaging.channel.queued.messages`   | Gauge   | Current number of messages buffered in the in-memory channel, tagged with `messaging.destination.name`. |

## Serialization

Messages are serialized as JSON using `System.Text.Json`. Each variant is written as one element of
a JSON array, tagged with a `$type` discriminator holding the wire name from
`IPayloadVariants.Variants`. Unknown discriminators are skipped on read so a subscriber survives a
publisher with variants it was not built against.

JIT builds with reflection enabled work out of the box — `MessageBrokerSerializerOptions` seeds a
`DefaultJsonTypeInfoResolver` by default, so `System.Text.Json` picks up types via reflection with
no further configuration. AOT and trimmed builds must attach a source-generated
`JsonSerializerContext` instead; the serializer throws at startup if no resolver is present when
reflection is disabled.

```csharp
// AOT / trimmed builds: declare one JsonSerializable per variant type on a partial context.
[JsonSerializable(typeof(OrderPayload.V1))]
[JsonSerializable(typeof(OrderPayload.V2))]
public partial class OrderJsonContext : JsonSerializerContext;

// AOT / trimmed builds: attach the context to the topic's serializer options.
services.Configure<MessageBrokerSerializerOptions>("orders", options =>
{
    options.JsonSerializerOptions.TypeInfoResolverChain.Add(OrderJsonContext.Default);
});
```

Other `JsonSerializerOptions` (naming policy, converters, etc.) can be set on the same options
instance and apply in both JIT and AOT modes:

```csharp
services.Configure<MessageBrokerSerializerOptions>("orders", options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.TypeInfoResolverChain.Add(OrderJsonContext.Default); // AOT only
});
```
