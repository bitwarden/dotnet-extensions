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

## Observing the variant constellation

Each consume emits a `messaging.client.consumed.messages` counter tick tagged with both
`messaging.destination.name` and `messaging.variant.name`. The variant tag is the wire name of the
highest received variant at or below the subscriber's ceiling — i.e. the variant the subscriber
actually deserialized from, before any upcast to reach `TCeiling`. Aggregated across every service in
the deployment, these tags describe the current constellation of variants actually being acted upon, so an
operator can see when a variant has fallen out of use and is safe to remove from the chain.

## Cross-process cache dependency

The Azure Service Bus and Rabbit backends require a shared `IFusionCache` that the negotiation
listener uses to hold the fleet-state snapshot. The library resolves it with an internal keyed
lookup that `services.AddBitwardenCaching()`'s `AnyKey` registration transparently satisfies —
callers just make that one call. `PublisherCacheValidator` runs at host start via
`ValidateOnStart` and fails with a clear message if a distributed backend is configured and this
call is missing. The cache is only read and written by the active `NegotiationListener` on the
SAC-locked instance for each topic; booting publishers and subscribers reach it only indirectly
via the admission handshake — see [Version negotiation](#version-negotiation) below.

The in-memory channel backend is single-process, needs no cross-process negotiation, and requires
no distributed cache.

## Version negotiation

Cross-process backends run a deploy-time admission handshake before any process starts publishing
or consuming. The invariant: at any moment, every live publisher and every live subscriber for a
given data-topic share at least one wire-name in their variant chains. If a booting instance would
break that invariant, it exits non-zero and the deployment fails.

Admission fires at host startup and re-fires every `HeartbeatInterval` to refresh the fleet
cache entry.

### Publish-after-admission guarantee

The guarantee lives in `IHost` startup ordering. `AddPublisher` registers
`NegotiationListenerCoordinator` and `PublisherJoinRequester` as `IHostedService`s before any
consumer-side hosted service (`AddMessageConsumer`'s `ConsumerBackgroundService`, for example).
`Host.StartAsync` runs hosted services in registration order and does not return until each one's
`StartAsync` completes. A failed admission
(`NegotiationRejectedException`/`NegotiationTimeoutException`/`BrokerUnavailableException`) fails
the host, so application code that would `Publish` or iterate a subscriber never runs.

Two opt-outs:

- **`proceedOnAdmissionTimeout: true`** on `AddSubscriber` softens the acknowledgement-timeout path — the
  subscriber starts and iterates without confirmed publisher compatibility. Explicit no-go and
  broker-outage failures still hard-fail startup.
- **Direct construction outside an `IHost`** (unit tests, custom hosting) skips the requester
  entirely. Use the in-memory channel backend for those scenarios; it does not run negotiation.

### Subscriber-first boot

A tolerant subscriber (`proceedOnAdmissionTimeout: true`) that starts before any publisher
queues its `Capability` on the control topic (session id = data-topic on Azure Service Bus,
request queue on RabbitMQ) and continues past its own `AdmissionTimeout`. When a publisher
eventually boots, its coordinator starts the listener before its own `PublisherJoin` requester
runs; the listener drains the queued Capability, upserts the subscriber into the fleet cache,
then processes the publisher's Join against it. The pre-existing subscriber is the baseline
the incoming publisher must comply with — if wire-names don't overlap, the publisher fails
startup.

### Coordination

Both backends use broker-native mutual exclusion. Only one instance of a given publisher-service
holds the "active consumer" role for a given data-topic at a time; that instance processes every
admission decision for the topic, writes the cache, and replies to the requester. Failover on
shutdown or crash is transparent — the broker hands the role to the next candidate.

- **Azure Service Bus** — session-enabled subscription with session id equal to the data-topic
  name. The broker's session lock is per `(subscription × session)`, giving one active consumer
  per `(publisher-service × data-topic)`.
- **RabbitMQ** — queue declared with `x-single-active-consumer=true`. Scope is per-queue (per
  publisher-service), so one instance of the service handles admissions for every topic that
  service publishes.

### Topology per backend

One namespace-wide control topic `ctrl` (or the configured `NegotiationOptions.ControlTopicName`)
carries every negotiation message. Data-topic routing rides on per-message metadata — an ASB
message property, a Rabbit routing key.

- **Azure Service Bus** — one session-enabled subscription per publisher-service named
  `request-{ServiceName}` receives inbound `Capability` / `PublisherJoin` / leave messages
  filtered by data-topic; one session-enabled `reply-{ServiceName}` subscription receives
  admission acknowledgements routed by instance identifier. Both subscriptions must exist before the process
  starts. `AzureServiceBusRuleReconciler` maintains the SQL filter rules on
  `request-{ServiceName}` at startup so the rule set matches the topics this service publishes.
- **RabbitMQ** — the library declares a `{ControlTopicName}` direct exchange and a per-service
  `request-{ServiceName}` classic queue (with `x-single-active-consumer=true`) on first use.
  Replies use the connection-scoped `amq.rabbitmq.reply-to` pseudo-queue, so no reply
  subscription is needed.

### Wire messages

| Message           | Direction              | Reply-expected | Purpose                                                                                        |
| ----------------- | ---------------------- | -------------- | ---------------------------------------------------------------------------------------------- |
| `Capability`      | subscriber → publisher | Yes            | "Here are the wire-names I can decode for topic X — admit me?"                                 |
| `PublisherJoin`   | new publisher → active | Yes            | "Here are the wire-names I can produce for topic X — admit me?"                                |
| `NegotiationAck`  | active → requester     | —              | Go / no-go, with an offender list on no-go.                                                    |
| `PublisherLeave`  | publisher → active     | No             | Clean-shutdown withdrawal; the active listener removes the instance's cache entry immediately. |
| `SubscriberLeave` | subscriber → active    | No             | Same, for a subscriber.                                                                        |

The two leave messages are fire-and-forget — the sender returns as soon as the outbound publish
completes, does not await an acknowledgement, and disposes the transport. Crash-path removal falls back to
TTL expiration.

### Key source files

| File                                                                       | Purpose                                                                                                      |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `NegotiationOptions.cs`                                                    | Configuration surface — `ServiceName`, `ProcessDisplayName`, heartbeat/TTL/timeout tuning.                   |
| `Capability.cs` / `PublisherJoin.cs`                                       | Admission-request wire types.                                                                                |
| `PublisherLeave.cs` / `SubscriberLeave.cs`                                 | Clean-shutdown-withdrawal wire types.                                                                        |
| `NegotiationAck.cs`                                                        | Admission reply carrying go/no-go and offender list.                                                         |
| `INegotiationTransport.cs`                                                 | Per-backend send + receive contract.                                                                         |
| `AzureServiceBusNegotiationTransport.cs` / `RabbitNegotiationTransport.cs` | Backend implementations.                                                                                     |
| `NegotiationListener.cs`                                                   | Runs on the single-active-consumer instance; performs admission checks and cache updates.                    |
| `NegotiationListenerCoordinator.cs`                                        | Hosted service that owns listener lifecycle and reconciles ASB filter rules at startup.                      |
| `PublisherJoinRequester.cs` / `SubscriberJoinRequester.cs`                 | `BackgroundService`s that send join/capability at startup, run heartbeat loops, and fire leaves at shutdown. |
| `FusionCacheNegotiationState.cs`                                           | Fleet-state cache backing, per-instance keys under `negotiation/{topic}/{role}/{instanceId}`.                |
| `NegotiationMetrics.cs`                                                    | Admissions counter and topics-served gauge (see [Observability](#observability)).                            |

## TODO

- Required payload-specific health check definition to surface eventual-consistency health.
- Assisted upcasts performed subscriber-side
- Managed-identity auth for Azure Service Bus. `AzureServiceBusPublisher`, `AzureServiceBusSubscriber`,
  `AzureServiceBusNegotiationTransport`, and `NegotiationListenerCoordinator` all call
  `new ServiceBusClient(connectionString)` / `new ServiceBusAdministrationClient(connectionString)`,
  which in `Azure.Messaging.ServiceBus` 7.x only accepts SAS-based strings. Enabling MI requires
  a `TokenCredential`-based construction path (fully-qualified namespace + credential) and would
  restore the finer-grained per-subscription authorization RBAC allows.

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
