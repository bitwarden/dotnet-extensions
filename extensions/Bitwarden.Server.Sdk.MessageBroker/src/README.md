# Bitwarden.Server.Sdk.MessageBroker

Transport-agnostic publish/subscribe messaging for Bitwarden services. Three backends share one API:
Azure Service Bus, RabbitMQ, and an in-memory `System.Threading.Channels` fallback.

## Architecture

### Core abstractions

- `IPublisher<T>` — publishes messages to a named topic
- `ISubscriber<T>` — receives messages from a named topic as `IAsyncEnumerable<Envelope<T>>`
- `Envelope<T>` — wraps a received message with broker metadata (`MessageId`, `TraceId`,
  `DeliveryCount`) and settlement methods (`CompleteAsync`, `RequeueAsync`, `DeadLetterAsync`)

Both interfaces are registered as keyed singletons. The service key is the topic name, or
`topic/subscription` when a subscription name is provided.

### Backend selection

`MessageBrokerServiceCollectionExtensions` (`AddPublisher<T>` / `AddSubscriber<T>`) registers the
appropriate backend at DI resolution time based on `MessagingOptions`:

1. `AzureServiceBusConnectionString` set → Azure Service Bus
2. `RabbitUri` set → RabbitMQ
3. Neither set → in-memory `System.Threading.Channels`

Setting both raises `OptionsValidationException` at startup via `MessagingOptionsValidator`.

### Key source files

| File | Purpose |
|---|---|
| `MessageBrokerServiceCollectionExtensions.cs` | DI registration; backend selection logic |
| `Envelope.cs` | Abstract base class for received messages |
| `IMessageConsumer.cs` | Public interface for consumers registered via `AddMessageConsumer` |
| `ConsumerBackgroundService.cs` | Hosted service that drives the consumer loop; auto-settles envelopes |
| `IMessageEscrowStore.cs` | Public interface for durable message escrow on shutdown |
| `ChannelPublisher.cs` / `ChannelSubscriber.cs` | In-memory backend |
| `ChannelEnvelope.cs` | In-memory envelope; handles `RequeueAsync` requeue logic |
| `ChannelTopic.cs` | Fan-out channel; one `Channel<T>` per subscription; drains to escrow on shutdown |
| `ChannelEscrowRegistration.cs` | Registers startup recovery and shutdown drain callbacks with `ChannelTopic` |
| `RabbitPublisher.cs` / `RabbitSubscriber.cs` | RabbitMQ backend |
| `RabbitConnection.cs` | Manages the shared RabbitMQ connection; declares exchanges and queues at startup |
| `AzureServiceBusPublisher.cs` / `AzureServiceBusSubscriber.cs` | Azure Service Bus backend |
| `MessageBrokerActivitySource.cs` | Shared `ActivitySource` for publish and consume spans |
| `MessageBrokerMetrics.cs` | Shared metrics (publish counter, consume counter, channel depth gauge) |
| `SystemTextJsonMessageSerializer.cs` | Default JSON serializer |

## RabbitMQ topology

Queues are declared as **quorum queues** (`x-queue-type: quorum`) for accurate `DeliveryCount`
tracking via the `x-delivery-count` header (RabbitMQ 3.12+). Classic queues only expose a boolean
`redelivered` flag.

Exchanges are declared as `fanout` and `durable`. Each subscription gets its own durable queue bound
to the exchange, giving pub/sub fan-out with per-group competing consumers.

## Shutdown and escrow

`ChannelTopic<T>` is registered as an `IHostedService` first, so it stops last under LIFO shutdown
order — after all `ConsumerBackgroundService` instances have exited. Its `StopAsync` calls the
`ShutdownDrain` callbacks registered by `ChannelEscrowRegistration<T>`, which serialize any
messages still in the channel and write them to `IMessageEscrowStore` before sealing the writers.

`ChannelSubscriber` is a simple pass-through: it reads from the channel reader until `WaitToReadAsync`
returns `false` (channel sealed) or the caller's `CancellationToken` fires.

The stop sequence when using `AddMessageConsumer` is:

1. `ConsumerBackgroundService.StopAsync` → stoppingToken cancelled → consumer exits (messages remain in channel)
2. `ChannelTopic.StopAsync` → drain callbacks serialize remaining messages → writes to `IMessageEscrowStore` → seals writers

At the next startup, `ChannelTopic.StartAsync` calls the recovery callbacks registered by
`ChannelEscrowRegistration<T>`, which deserialize the stored messages and re-inject them into the
channel writer before consumers begin processing.

When no `IMessageEscrowStore` is registered, undelivered messages are logged as errors (message ID
only) and discarded. Register an implementation with the DI container to enable durable recovery:

```csharp
services.AddSingleton<IMessageEscrowStore, MyDatabaseEscrowStore>();
```

The escrow key is `{topicName}/{subscriptionName}` (the same as the DI service key). Each entry in the
store is a JSON-encoded blob containing `MessageId`, `TraceId`, `DeliveryCount`, and the serialized
message payload. Consumers should be idempotent — at-most-once delivery becomes at-least-once when
escrow is enabled.

## Observability

All three backends emit the same `ActivitySource` spans and `IMeterFactory` metrics. The publish
span's `Activity.Id` (W3C traceparent) is propagated as a message property — `traceparent` header
in RabbitMQ, `ApplicationProperties["traceparent"]` in Azure Service Bus, `TraceId` field in
`ChannelEnvelope`. The consumer span is started with this context as the parent so publish and
consume spans share a trace.

## TODO: Message payload versioning

When a message type `T` changes shape (renamed field, changed type, restructured payload), messages
already in a queue were serialized with the old schema. There is currently no framework support for
evolving the payload across versions.

`System.Text.Json` handles non-breaking changes (adding a nullable field, removing a field) silently,
but breaking changes require explicit transformation. Key open questions before implementing:

- Where does the version signal live — in the payload itself, the topic name, or a metadata header?
- Is upcasting chained (v1→v2→v3) or direct (any old version → current)?
- Is an explicit version field on the message type required, or is it optional convention?

## Running tests

```
cd tests/Bitwarden.Server.Sdk.MessageBroker.Tests
dotnet run
```

Tests require Docker (for Testcontainers). The RabbitMQ and Azure Service Bus emulator containers
start automatically. Use `dotnet run` rather than `dotnet test`; this is an xUnit v3 project with
`OutputType=Exe`.

The test suite covers all three backends via the shared `BehaviorTests` base class.
Channel-specific tests (drain on shutdown, queue depth gauge) live in `ChannelBehaviorTests`.
