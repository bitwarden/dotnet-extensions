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

| File                                                           | Purpose                                                                          |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `MessageBrokerServiceCollectionExtensions.cs`                  | DI registration; backend selection logic                                         |
| `Envelope.cs`                                                  | Abstract base class for received messages                                        |
| `IMessageConsumer.cs`                                          | Public interface for consumers registered via `AddMessageConsumer`               |
| `ConsumerBackgroundService.cs`                                 | Hosted service that drives the consumer loop; auto-settles envelopes             |
| `IMessageEscrowStore.cs`                                       | Public interface for durable message escrow on shutdown                          |
| `ChannelPublisher.cs` / `ChannelSubscriber.cs`                 | In-memory backend                                                                |
| `ChannelEnvelope.cs`                                           | In-memory envelope; handles `RequeueAsync` requeue logic                         |
| `ChannelTopic.cs`                                              | Fan-out channel; one `Channel<T>` per subscription; drains to escrow on shutdown |
| `ChannelEscrowRegistration.cs`                                 | Registers startup recovery and shutdown drain callbacks with `ChannelTopic`      |
| `RabbitPublisher.cs` / `RabbitSubscriber.cs`                   | RabbitMQ backend                                                                 |
| `RabbitConnection.cs`                                          | Manages the shared RabbitMQ connection; declares exchanges and queues at startup |
| `AzureServiceBusPublisher.cs` / `AzureServiceBusSubscriber.cs` | Azure Service Bus backend                                                        |
| `MessageBrokerActivitySource.cs`                               | Shared `ActivitySource` for publish and consume spans                            |
| `MessageBrokerMetrics.cs`                                      | Shared metrics (publish counter, consume counter, channel depth gauge)           |
| `SystemTextJsonMessageSerializer.cs`                           | Default JSON serializer                                                          |

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

## Payload versioning

A payload is a family of data structure variants forming a chain from floor (oldest known shape)
to ceiling (newest). Publishers send the complete ceiling-to-floor sequence in a single message;
each subscriber picks the highest variant it can decode and, if that variant is below its declared
ceiling, walks pure upcasts back up to reach the highest variant. Old subscribers keep working
against new publishers as long as the old variant is still on the wire. New subscribers keep
working against old publishers as long as the older ceiling can upcast to the newer one.

Concrete payloads derive from `Payload<TSelf, TFloor, TCeiling>` and implement
`IPayloadVariants<TSelf>`. The `IPayloadVariants<TSelf>.Variants` static list pairs each
variant type with a stable wire name. Wire names are decoupled from CLR type names so variants
can be renamed or moved without breaking messages already on the wire. The wire format is a JSON
array of variants with a `$type` discriminator per element; unknown discriminators are skipped so
a subscriber survives a publisher that has variants it was not built against.

Every non-ceiling variant declares an upcast to its ceiling-side neighbor via `IPureUp<TUp>`.
Downcasts come in two kinds: **pure** (`IPure<TDown, TUp>`, `IPureCeiling<TDown>`) when the
floor-side neighbor can be produced from the current variant alone, and **assisted**
(`IAssisted<...>`, `IAssistedCeiling<...>`) when the downcast needs caller-supplied data. Assist
data is injected at publish time through a fluent chain: `publisher.Publish(ceiling).With(assist1).With(assist2).SendAsync()`. The compiler refuses `SendAsync` until every assisted downcast between the current
position and the floor has been crossed, so a publisher cannot omit data an older subscriber needs.

`ChainValidator<TPayload, TCeiling>` runs once per payload family at DI registration and throws
an aggregate exception if the declared variants are not complete, sparse, and linear: duplicate
types or wire names, missing floor or ceiling, forks, cycles, or Next/Previous references that
disagree with the variant list.

### Key source files

| File                                 | Purpose                                                                                                     |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| `Payload.cs`                         | Variant-side interfaces (`IFloor`, `ICeiling`, `IPure`, `IAssisted`, `IAssist`, …) and payload base classes |
| `ChainWalk.cs`                       | Reflects `TDown`/`TUp` off a variant type to walk the declared chain                                        |
| `ChainValidator.cs`                  | DI-time chain-shape validation                                                                              |
| `Publisher.cs`                       | `Publisher<TPayload, TCeiling>` base + `PublishBuilder` fluent chain                                        |
| `Envelope.cs`                        | Selects the received variant, upcasts it to the consumer's ceiling                                          |
| `SystemTextJsonMessageSerializer.cs` | Polymorphic array wire format with `$type` discriminator                                                    |

## TODO

- Consumed-variant metric for tracking constellation variant-dependency state.
- Subscriber/publisher negotiation to ensure variant overlap.
- Publisher shared-cache requirement for ensuring variant overlap with existing subscribers.
- Required payload-specific health check definition to surface eventual-consistency health.
- Assisted upcasts performed subscriber-side

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
