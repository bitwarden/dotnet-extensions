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

A self-hosted service creates the topics, queues and subscriptions it needs as it starts, so a
self-hosted install's broker needs no setup. In the cloud, infrastructure-as-code creates them before
release. An operator can switch either way with
`AutoProvisionTopology`; a service that doesn't create its topology checks it at startup, and stops,
naming what is missing.

## Provisioning

A self-hosted service creates its own topology as it starts (see
[Creating or checking the topology at startup](#creating-or-checking-the-topology-at-startup)). Otherwise
the exchanges, queues, topics, subscriptions, and related filters and policies must be declared on the
broker before the application starts, through infrastructure-as-code or the broker's definitions.

A broker topic carries messages from many payload families. Publishers stamp every outgoing message with
their family wire name (`IPayloadWireName<TPayload>.WireName`) so subscriptions can bind or filter
down to the family they consume.

## Creating or checking the topology at startup

Once a service first reaches its broker, it either creates the topology its registrations need or
checks that it exists, from the same requirements listed under
[Collecting a service's topology](#collecting-a-services-topology). Which one follows
`AutoProvisionTopology`: unset, a self-hosted service (`IBitwardenEnvironment.SelfHosted`) creates its
topology and any other checks it. If an object is missing, or the service can't create it, it logs one
critical message naming each and stops with exit code 1. A broker that can't be reached doesn't stop
it: it logs a warning and tries again until the broker answers.

### Creating it

The service creates what is missing and leaves what exists alone, whoever made it, so topology an
operator provisioned keeps its own settings, dead-lettering included:

- its own topics;
- its subscriptions, and on RabbitMQ and LavinMQ their bindings; on Azure Service Bus, a rule for
  each family no rule of the subscription admits yet;
- its command queues, and on RabbitMQ and LavinMQ their exchanges.

It doesn't create the topics it subscribes to or the queues it sends to; their owners do, and a
subscription whose topic doesn't exist yet waits for it. Its hosted consumers wait until it is done.
Bindings and rules are made again on every start, so a family a release adds reaches an existing
subscription; one the service may not make on an object it didn't create is left, with a warning.
`MaxDeliveryCount` (default 10) is the delivery limit of what it creates.

- **RabbitMQ and LavinMQ:** the shapes below, with one difference: each queue's delivery limit and
  dead-lettering are queue arguments (`x-delivery-limit`, `x-dead-letter-exchange`,
  `x-dead-letter-strategy`, `x-overflow`), not policies, because a service can't set policies over
  AMQP. Each queue it creates dead-letters to an exchange and queue of its own, `dead-letter.{queue}`,
  the names the example below uses. Arguments can't be changed on an existing queue; to change a limit
  later, delete the queue or provision it yourself. The rights it needs are under
  [Users and permissions](#users-and-permissions).
- **Azure Service Bus:** the entities and settings described under
  [Azure Service Bus](#azure-service-bus), with duplicate detection of ten minutes on topics and
  command queues and a one-minute lock. It needs the Manage right: a connection string with a Manage
  policy, or the Azure Service Bus Data Owner role. That right covers the whole namespace, and the
  service holds it for as long as it runs (see [What provisioning costs](#what-provisioning-costs)).

### What provisioning costs

A service that creates its own topology holds the rights to create it for as long as it runs, as a
self-hosted service holds the right to change its database schema. If it is compromised, so are those
rights:

- **RabbitMQ and LavinMQ:** `configure` lets it delete and recreate the objects it is granted it on,
  losing their messages. Anchor the grants to its own names, as below, and it can harm only its own
  objects. On LavinMQ it also needs `read` on every name (see
  [Where RabbitMQ and LavinMQ differ](#where-rabbitmq-and-lavinmq-differ)), which lets it read every
  queue.
- **Azure Service Bus:** the Manage right lets it read, send to and delete every entity in the
  namespace.

Where that matters more than setting up nothing by hand, provision once, from the release's topology or
by letting one start create it, then set `AutoProvisionTopology` to `false` and give each service only
the rights under [Users and permissions](#users-and-permissions) and
[Access with a managed identity](#access-with-a-managed-identity).

### Checking it

Set `VerifyTopologyAtStartup` to `false` to turn the check off. The check needs no rights beyond
those the service already has (see
[Users and permissions](#users-and-permissions) and
[Access with a managed identity](#access-with-a-managed-identity)):

- **RabbitMQ:** a passive declare of each of its topic exchanges, its subscription queues
  (`{topic}.{subscription}`), its command queue and the exchange of each command queue it sends to.
  A passive declare needs only the read or write right on the object, not configure. RabbitMQ
  refuses one on an object the user has no rights to, whether or not it exists, so the check reports
  such an object as missing or not permitted. It doesn't check bindings or policies.
- **Azure Service Bus:** a peek at each of its subscriptions and its command queue, which needs only
  the receive rights it has. Its topics and the queues it only sends to can't be checked without the
  Manage right, which a service that doesn't provision shouldn't have; a missing one shows up as sends
  that fail with `BrokerUnavailableException`.

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
  `messaging-topic`. Fail otherwise: two services on one subscription would each get receive rights
  on it and take each other's messages.
- **No two declarations make the same broker object.** Work out every name each declaration becomes
  on the broker, and fail if two declarations of any kinds produce the same one. On RabbitMQ
  everything shares one name space: a topic's exchange is `{topic}`, a subscription's queue is
  `{topic}.{subscription}`, and a command queue and its exchange are both `{queue}`, so the
  subscription `vault` on the topic `notification` and the command queue `notification.vault` would
  be the same queue, and so would the subscription `b.c` on the topic `a` and the subscription `c`
  on the topic `a.b`. The dead-letter exchange and queue of each subscription queue and command queue,
  `dead-letter.{queue}`, count too: a command queue `x` and a subscription `x` on the topic
  `dead-letter` would share `dead-letter.x`, so treat `dead-letter` as a reserved first name part.
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

If the Correlation filter is missing or wrong, the subscriber still falls back safely —
`AzureServiceBusSubscriber` reads the `x-bw-payload-family` application property on every message
and dead-letters anything that does not match `TPayload.WireName` before attempting
deserialization. The filter is still the right production configuration because it moves routing
off the subscriber and into the broker.

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

If the queue's routing-key binding is wrong, `RabbitSubscriber` falls back to the same defensive
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
- a dead-letter exchange and queue for each subscription queue, `dead-letter.{queue}`, the names a
  service that creates its own topology uses. Their names must not match a subscription queue's
  policy, or the policy would apply to the dead-letter queue too and cycle its messages back through it;
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
    { "name": "dead-letter.user-events.vault", "vhost": "/", "type": "fanout", "durable": true, "auto_delete": false, "internal": false, "arguments": {} },
    { "name": "dead-letter.user-events.organizations", "vhost": "/", "type": "fanout", "durable": true, "auto_delete": false, "internal": false, "arguments": {} }
  ],
  "queues": [
    { "name": "user-events.vault", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
    { "name": "user-events.organizations", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
    { "name": "dead-letter.user-events.vault", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
    { "name": "dead-letter.user-events.organizations", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } }
  ],
  "bindings": [
    { "source": "user-events", "vhost": "/", "destination": "user-events.vault", "destination_type": "queue", "routing_key": "user.created", "arguments": {} },
    { "source": "user-events", "vhost": "/", "destination": "user-events.organizations", "destination_type": "queue", "routing_key": "user.deleted", "arguments": {} },
    { "source": "dead-letter.user-events.vault", "vhost": "/", "destination": "dead-letter.user-events.vault", "destination_type": "queue", "routing_key": "", "arguments": {} },
    { "source": "dead-letter.user-events.organizations", "vhost": "/", "destination": "dead-letter.user-events.organizations", "destination_type": "queue", "routing_key": "", "arguments": {} }
  ],
  "policies": [
    {
      "vhost": "/", "name": "user-events.vault", "pattern": "^user-events\\.vault$", "apply-to": "queues", "priority": 0,
      "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.user-events.vault", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
    },
    {
      "vhost": "/", "name": "user-events.organizations", "pattern": "^user-events\\.organizations$", "apply-to": "queues", "priority": 0,
      "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.user-events.organizations", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
    }
  ]
}
```

### Users and permissions

Give each service its own broker user, with only the rights its registrations declare (see
[Collecting a service's topology](#collecting-a-services-topology)):

When the topology is provisioned for it:

- **configure:** none (`^$`). It declares nothing.
- **write:** its own topic exchanges, and the exchanges of the command queues it sends to.
- **read:** its own subscription queues and its own command queue.

When it creates its own ([Creating it](#creating-it)), where `T` is each of its own topics, `S` each of
its subscription queues (`{topic}.{subscription}`), `F` each topic it subscribes to, `Q` its command
queue and `R` each queue it sends to:

- **configure:** `T`, `S`, `dead-letter.S`, `Q`, `dead-letter.Q`.
- **write:** `T`, `R`, `S`, `dead-letter.S`, `Q`, `dead-letter.Q`. A queue declared with a dead-letter
  exchange needs `write` on that exchange, and binding a queue needs `write` on the queue.
- **read:** `F`, `S`, `dead-letter.S`, `Q`, `dead-letter.Q`. Binding a queue needs `read` on the
  exchange it binds to. On LavinMQ, `.*` instead (see
  [Where RabbitMQ and LavinMQ differ](#where-rabbitmq-and-lavinmq-differ)).

Both brokers check these as regular expressions over resource names, so anchor each name and escape
its dots (`^user-events$`, `^notification\.commands$`,
`^(user-events\.vault|dead-letter\.user-events\.vault)$`). A pattern such as `^dead-letter\.` would
let a service delete every other service's dead-lettered messages.

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

Each command queue has its own exchange so that sending can be granted one queue at a time: a
sender's user needs `write` on `^{queue}$` for each queue it sends to, and the receiver's user
`read` on its own queue. The permissions are regular expressions, so escape the dots in a queue's
name (`^notification\.commands$`). Don't grant service users `write` on the default exchange
(`amq.default`): it reaches every queue in the virtual host.
