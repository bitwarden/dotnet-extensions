# Broker topology

**Audience:** Operators and platform engineers provisioning Azure Service Bus or RabbitMQ
infrastructure for services that use `Bitwarden.Server.Sdk.MessageBroker`. Application developers
can skip this document — the in-memory channel backend used during development needs no setup.

The library does not provision broker topology. The exchanges, queues, topics, subscriptions, and
related filters and policies must be declared on the broker before the application starts —
typically as part of the broker container's bootstrap or via infrastructure-as-code (Terraform,
Pulumi, broker-specific CLIs).

A broker topic carries messages from many payload families. Publishers stamp every outgoing message with
their family wire name (`IPayloadWireName<TPayload>.WireName`) so subscriptions can bind or filter
down to the family they consume.

## Azure Service Bus

For each unique `topic` passed to `AddPublisher<TPayload, TCeiling>(topic)`:

- A topic named `{topic}`.

For each `AddSubscriber<TPayload, TCeiling>(topic, subscription)`:

- A subscription named `{subscription}` on that topic with a Correlation filter on
  `x-bw-payload-family = '{WireName}'` so only `TPayload`'s family reaches this subscription.
  Set `MaxDeliveryCount` on the subscription to cap redelivery attempts; the subscription's
  built-in dead-letter queue handles exhausted or explicitly dead-lettered messages automatically.

If the Correlation filter is missing or wrong, the subscriber still falls back safely —
`AzureServiceBusSubscriber` reads the `x-bw-payload-family` application property on every message
and dead-letters anything that does not match `TPayload.WireName` before attempting
deserialization. The filter is still the right production configuration because it moves routing
off the subscriber and into the broker.

The library creates and owns its own `ServiceBusClient` from the configured connection string
and does not consume any `ServiceBusClient` the app registers separately. An app that also uses
`ServiceBusClient` against the same namespace will have two AMQP connections to the broker.

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

Policies must be used rather than queue arguments because quorum queue arguments cannot be
changed on an existing queue. See the RabbitMQ documentation on
[quorum queue poison-message handling](https://www.rabbitmq.com/docs/quorum-queues#poison-message-handling)
and [policies](https://www.rabbitmq.com/docs/policies) for the full command reference, and
`management.load_definitions` for declarative bootstrap via a `definitions.json` file loaded
at broker startup.

### Example: a user service

A user service registers:

```csharp
services.AddPublisher<UserCreatedPayload, UserCreated>("user-events");
services.AddPublisher<UserDeletedPayload, UserDeleted>("user-events");
services.AddSubscriber<UserCreatedPayload, UserCreated>("user-events", "vault");
services.AddSubscriber<UserDeletedPayload, UserDeleted>("user-events", "organizations");
```

where `UserCreatedPayload.WireName` is `"user.created"` and `UserDeletedPayload.WireName` is
`"user.deleted"`. Declare the topology with `rabbitmqadmin` and `rabbitmqctl`:

```bash
# One topic exchange carries both families.
rabbitmqadmin exchanges declare --name "user-events" --type "topic" --durable true

# One queue per subscription, each bound with its family's wire name as routing key.
rabbitmqadmin queues declare --name "user-events.vault" --type "quorum" --durable true
rabbitmqadmin bindings declare --source "user-events" \
  --destination-type "queue" --destination "user-events.vault" --routing-key "user.created"

rabbitmqadmin queues declare --name "user-events.organizations" --type "quorum" --durable true
rabbitmqadmin bindings declare --source "user-events" \
  --destination-type "queue" --destination "user-events.organizations" --routing-key "user.deleted"

# Dead-letter exchange and queue. The names must not match the policy pattern
# below, which would otherwise re-apply the dead-letter policy to the dead-letter
# queue and cycle its messages back through itself.
rabbitmqadmin exchanges declare --name "dead-letter.user-events" --type "fanout" --durable true
rabbitmqadmin queues declare --name "dead-letter.user-events" --type "quorum" --durable true
rabbitmqadmin bindings declare --source "dead-letter.user-events" \
  --destination-type "queue" --destination "dead-letter.user-events"

# Policy applying delivery-limit, dead-letter-exchange, dead-letter-strategy, and
# overflow to the user-events.* subscription queues.
rabbitmqctl set_policy user-events-subscriptions "^user-events\." \
  '{"delivery-limit":10,"dead-letter-exchange":"dead-letter.user-events","dead-letter-strategy":"at-least-once","overflow":"reject-publish"}' \
  --apply-to quorum_queues
```
