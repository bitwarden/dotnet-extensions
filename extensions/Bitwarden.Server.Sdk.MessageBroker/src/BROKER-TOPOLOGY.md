# Broker topology

**Audience:** Operators and platform engineers provisioning Azure Service Bus or RabbitMQ
infrastructure for services that use `Bitwarden.Server.Sdk.MessageBroker`. Application developers
can skip this document — the in-memory channel backend used during development needs no setup.

## Supported brokers

MessageBroker supports two brokers, chosen through configuration: Azure Service Bus and RabbitMQ.
Services don't depend on which one is in use.

| Deployment | Default broker    | Can instead use   |
| ---------- | ----------------- | ----------------- |
| Cloud      | Azure Service Bus | Nothing else      |
| Self-host  | RabbitMQ 4.3.6    | Azure Service Bus |

Topics, queues and subscriptions must exist before the services start. With a broker other than the
default, the operator sets them up from the release's broker topology before each upgrade.

## Provisioning

The library does not provision broker topology. The exchanges, queues, topics, subscriptions, and
related filters and policies must be declared on the broker before the application starts —
typically as part of the broker container's bootstrap or via infrastructure-as-code.

A broker topic carries messages from many payload families. Publishers stamp every outgoing message with
their family wire name (`IPayloadWireName<TPayload>.WireName`) so subscriptions can bind or filter
down to the family they consume.

The two shapes this document provisions, with generic services in place of real ones:

```mermaid
flowchart LR
    subgraph Topics["Topic: one service's events, many readers"]
        direction LR
        P[Publishing service] -->|"user.created<br/>user.deleted"| T(("Topic<br/>user-events"))
        T -->|"family = user.created"| S1[["Subscription<br/>vault"]]
        T -->|"family = user.deleted"| S2[["Subscription<br/>organizations"]]
        S1 --> R1[Subscribing service A]
        S2 --> R2[Subscribing service B]
    end
    subgraph Commands["Command queue: many senders, one receiving service"]
        direction LR
        C1[Sending service A] -->|send.mail| Q[["Command queue<br/>notification.commands"]]
        C2[Sending service B] -->|send.push| Q
        Q --> N[Receiving service]
    end
```

Every queue and subscription has a delivery limit. A message that reaches it, or that the
service sets aside, goes to a dead-letter queue rather than back to the service.

## Collecting a service's topology

Each registration declares what it needs through `Bitwarden.Server.Sdk.Infrastructure`, so the
topology can be collected from a service's startup (with `Bitwarden.Server.Sdk.Infrastructure.Testing`)
and provisioned from that list rather than written by hand. The kinds are constants on
`MessagingRequirements`:

| Kind                      | Declared by                | Properties                         | Provision                                                       |
| ------------------------- | -------------------------- | ---------------------------------- | --------------------------------------------------------------- |
| `messaging-topic`         | `AddPublisher`             | `topic`                            | The topic, owned by this service                                |
| `messaging-publish`       | `AddPublisher`             | `topic`, `family`                  | Send rights on the topic                                        |
| `messaging-subscription`  | `AddSubscriber` and the consumers built on it | `topic`, `subscription`, `family` | The subscription, filtered or bound to the family, and receive rights on it |
| `messaging-command-queue` | `AddCommandConsumer`       | `queue`                            | The queue, owned by this service, and receive rights on it      |
| `messaging-command-consume` | `AddCommandConsumer`     | `queue`, `family`                  | Nothing; lets provisioning check senders against what the queue accepts |
| `messaging-command-send`  | `AddCommandSender`         | `queue`, `family`                  | Send rights on the receiving service's queue (on RabbitMQ, write on the queue's own exchange) |

None of them carries a connection string or key.

Provisioning collects every service's list and checks it before granting anything:

- **One owner per topic and per queue.** Each topic appears under `messaging-topic`, and each queue
  under `messaging-command-queue`, for exactly one service. Fail if two services claim the same
  one: a non-owner could publish another service's events or read its commands.
- **One owner per subscription.** Each topic and subscription pair appears under
  `messaging-subscription` for exactly one service, and its topic has an owner under
  `messaging-topic`. Fail otherwise: two services on one subscription would take each other's
  messages.
- **No two declarations make the same broker object.** Fail if two declarations of any kinds produce
  the same broker name, dead-letter exchanges and queues included. On RabbitMQ everything shares one
  name space (a topic's exchange is `{topic}`, a subscription's queue `{topic}.{subscription}`, a
  command queue and its exchange both `{queue}`), so the subscription `vault` on the topic
  `notification` and the command queue `notification.vault` would be the same queue.
- **Send rights only where declared.** Grant send rights on a topic only to its owner, and on a
  queue only to the services that declare `messaging-command-send` for it.
- **Senders match consumers.** Each `messaging-command-send` should name a family the queue's owner
  declares under `messaging-command-consume`; a command nobody consumes is dead-lettered at runtime.

## Azure Service Bus

For each unique `topic` passed to `AddPublisher<TPayload, TCeiling>(topic)`:

- A topic named `{topic}`.

For each `AddSubscriber<TPayload, TCeiling>(topic, subscription)`:

- A subscription named `{subscription}` on that topic with a Correlation filter on
  `x-bw-payload-family = '{WireName}'` so only `TPayload`'s family reaches this subscription.
  Set `MaxDeliveryCount` on the subscription as a backstop on redelivery, at least the largest
  `ConsumerRetryOptions.MaxDeliveries` its consumer is configured with; the subscription's
  built-in dead-letter queue handles exhausted or explicitly dead-lettered messages automatically.

If the Correlation filter is missing or wrong, the subscriber still falls back safely. It reads
the `x-bw-payload-family` application property on every message and dead-letters any message whose
family isn't `TPayload.WireName` before deserializing it, with a reason that names both families.
The filter is still the right production configuration, because it moves routing off the
subscriber and into the broker.

The library creates and owns its own `ServiceBusClient` from the configured connection string or namespace
and does not consume any `ServiceBusClient` the app registers separately. An app that also uses
`ServiceBusClient` against the same namespace will have two AMQP connections to the broker.

### Command queues

For each unique `queue` passed to `AddCommandConsumer<TPayload, TCeiling, TConsumer>(queue)`:

- A queue named `{queue}`, owned by the receiving service, with no filter. Set `MaxDeliveryCount`
  as for a subscription.

Senders (`AddCommandSender`) need only send rights on the queue.

### Access with a managed identity

A service configured with `AzureServiceBusFullyQualifiedNamespace` authenticates as the credential it
registers under `MessageBrokerKeys.Credential`, typically its managed identity. Grant it Azure's data roles at the narrowest scope that works: **Azure Service Bus Data
Sender** on each topic it publishes to and each command queue it sends to, and **Azure Service Bus
Data Receiver** on each of its own subscriptions and on its own command queue. No other service
needs receive rights on them.

## RabbitMQ

For each unique `topic` passed to `AddPublisher<TPayload, TCeiling>(topic)`:

- A durable **topic** exchange named `{topic}`.

For each `AddSubscriber<TPayload, TCeiling>(topic, subscription)`:

- A durable quorum queue named `{topic}.{subscription}`, bound to the topic exchange with
  routing key `{WireName}` (the `IPayloadWireName<TPayload>.WireName` of the family the
  subscriber consumes).
- A server-side policy applying `delivery-limit` to the queue, capping the number of
  redelivery attempts before a message is dead-lettered.
- A server-side policy applying `dead-letter-exchange`, `dead-letter-strategy: at-least-once`,
  and `overflow: reject-publish` to the queue, plus the target dead-letter exchange and a
  quorum queue bound to it.

If the queue's routing-key binding is wrong, the subscriber falls back to the same defensive
check — it reads the `x-bw-payload-family` header and dead-letters mismatches. The routing-key
binding is still the right production configuration because it moves the filter into the broker.

A hosted consumer ends its own retries after `ConsumerRetryOptions.MaxDeliveries` (see PACKAGE.md).
The delivery limit is the backstop, so set it at least as high as the largest `MaxDeliveries`
configured for the queue. A consumer that returns a message for redelivery does so with `basic.reject`, which
RabbitMQ counts toward the limit and in the `x-delivery-count` header that becomes
`Envelope.DeliveryCount`.

Policies must be used rather than queue arguments because quorum queue arguments cannot be
changed on an existing queue. Declare the topology in a `definitions.json` file and import it when
the broker starts with `load_definitions` (see the RabbitMQ documentation on
[definitions](https://www.rabbitmq.com/docs/definitions) and
[quorum queue poison-message handling](https://www.rabbitmq.com/docs/quorum-queues#poison-message-handling)).

### What RabbitMQ needs for one topic

```mermaid
flowchart LR
    P[Publishing service] -->|"routing key = family"| X{{"Topic exchange<br/>user-events"}}
    X -->|"user.created"| Q1[["Quorum queue<br/>user-events.vault"]]
    X -->|"user.deleted"| Q2[["Quorum queue<br/>user-events.organizations"]]
    Q1 --> A[Subscribing service A]
    Q2 --> B[Subscribing service B]
    Q1 -.->|"past delivery-limit"| DX{{"Fanout exchange<br/>dead-letter.user-events"}}
    Q2 -.->|"past delivery-limit"| DX
    DX -.-> DQ[["Quorum queue<br/>dead-letter.user-events"]]
```

### Example: a user service

A user service registers:

```csharp
services.AddPublisher<UserCreatedPayload, UserCreated>("user-events");
services.AddPublisher<UserDeletedPayload, UserDeleted>("user-events");
services.AddSubscriber<UserCreatedPayload, UserCreated>("user-events", "vault");
services.AddSubscriber<UserDeletedPayload, UserDeleted>("user-events", "organizations");
```

where `UserCreatedPayload.WireName` is `"user.created"` and `UserDeletedPayload.WireName` is
`"user.deleted"`. Its topology, as `definitions.json`:

- one topic exchange carries both families;
- one quorum queue per subscription, bound with its family's wire name as routing key;
- a dead-letter exchange and queue, whose names must not match a subscription queue's policy, or the
  policy would apply to the dead-letter queue too and cycle its messages back through it;
- one policy per subscription queue, matching its name exactly. A `delivery-limit` of 9 dead-letters
  a message after its tenth delivery.

The file declares the virtual host it uses, because RabbitMQ creates neither its default virtual host
nor its default user when definitions are imported at startup. It declares no users or permissions:
see [Users and permissions](#users-and-permissions).

```json
{
  "vhosts": [
    { "name": "/" }
  ],
  "exchanges": [
    { "name": "user-events", "vhost": "/", "type": "topic", "durable": true, "auto_delete": false, "internal": false, "arguments": {} },
    { "name": "dead-letter.user-events", "vhost": "/", "type": "fanout", "durable": true, "auto_delete": false, "internal": false, "arguments": {} }
  ],
  "queues": [
    { "name": "user-events.vault", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
    { "name": "user-events.organizations", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
    { "name": "dead-letter.user-events", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } }
  ],
  "bindings": [
    { "source": "user-events", "vhost": "/", "destination": "user-events.vault", "destination_type": "queue", "routing_key": "user.created", "arguments": {} },
    { "source": "user-events", "vhost": "/", "destination": "user-events.organizations", "destination_type": "queue", "routing_key": "user.deleted", "arguments": {} },
    { "source": "dead-letter.user-events", "vhost": "/", "destination": "dead-letter.user-events", "destination_type": "queue", "routing_key": "", "arguments": {} }
  ],
  "policies": [
    {
      "vhost": "/", "name": "user-events.vault", "pattern": "^user-events\\.vault$", "apply-to": "queues", "priority": 0,
      "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.user-events", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
    },
    {
      "vhost": "/", "name": "user-events.organizations", "pattern": "^user-events\\.organizations$", "apply-to": "queues", "priority": 0,
      "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.user-events", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
    }
  ]
}
```

### Users and permissions

Give each service its own broker user, with only the rights its registrations declare (see
[Collecting a service's topology](#collecting-a-services-topology)):

- **configure:** none (`^$`). Services declare nothing; provisioning does.
- **write:** its own topic exchanges, and the exchanges of the command queues it sends to.
- **read:** its own subscription queues and its own command queue.

Both brokers check these as regular expressions over resource names, so anchor them and escape the
dots in names (`^user-events$`, `^notification\.commands$`).

Remove the default `guest` user, or at least change its password and limit it, on both brokers:
both official images accept `guest` from outside the container. A service sharing one administrator
login can publish to any queue and read any other service's messages, whatever the rules above say.

Keep users and their credentials out of the topology file that ships with a release or sits in a
repository: create them from a secret store, or through the management API when deploying. A
`password_hash` in a definitions file is a credential, and so is the `RabbitUri` a service is given.

### Command queues

For each unique `queue` passed to `AddCommandConsumer<TPayload, TCeiling, TConsumer>(queue)`:

- A durable **direct** exchange named `{queue}`.
- A durable quorum queue named `{queue}`, bound to that exchange with the queue's name as its
  routing key. Senders publish to the exchange with the same routing key.
- Apply the same `delivery-limit` and dead-letter policy as subscription queues.

```mermaid
flowchart LR
    S1[Sending service A] -->|"write on the exchange"| X{{"Direct exchange<br/>notification.commands"}}
    S2[Sending service B] -->|"write on the exchange"| X
    X -->|"routing key = queue name"| Q[["Quorum queue<br/>notification.commands"]]
    Q -->|"read on its own queue"| R[Receiving service]
    Q -.->|"past delivery-limit,<br/>or no consumer for the family"| DLQ[["Dead-letter queue"]]
    X -. "no queue bound: send returned,<br/>sender throws" .-> S1
```

Each command queue has its own exchange so that sending can be granted one queue at a time: a
sender's user needs `write` on `^{queue}$` for each queue it sends to, and the receiver's user
`read` on its own queue. The permissions are regular expressions, so escape the dots in a queue's
name (`^notification\.commands$`). Don't grant service users `write` on the default exchange
(`amq.default`): it reaches every queue in the virtual host.
