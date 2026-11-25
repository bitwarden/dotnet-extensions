# Bitwarden.Server.Sdk.MessageBroker.Topology

**Audience:** engineers changing this package.

Broker setup written from the messaging requirements services declare. See [PACKAGE.md](PACKAGE.md)
for how to use it.

## What belongs here

Reading services' manifests, checking them together, and writing each broker's setup from the
result. The shapes, names and settings it writes are MessageBroker's: they follow
[BROKER-TOPOLOGY.md](../../Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md) and what
MessageBroker's own provisioning creates (`TopologyStartupService`), so a change to either is a
change here too, and the other way round. Nothing here connects to a broker or runs in a service,
and nothing writes a credential: users, passwords, keys and connection strings are each install's.

## How it works

- **Collecting.** `BrokerTopology.Collect` implements BROKER-TOPOLOGY.md's checks that don't depend
  on a broker ("Provisioning collects every service's list and checks it"), plus one family and one
  reader per subscription, which every broker's names depend on. A publish or consume declared by a
  service other than the owner is a problem rather than a family the owner seems to have.
- **Send rights only where declared** holds by construction: the model records a topic's owner and
  a queue's senders from their own declarations alone, and the writers grant nothing else.
- **Broker names.** Each writer works out the names its broker gives every declaration and refuses
  two declarations that make one. `RabbitDefinitions` treats exchanges and queues as one name space,
  since RabbitMQ grants rights by patterns over names; a command queue's exchange and queue share
  their name as one declaration. It reserves the first name part `dead-letter` for the
  `dead-letter.{queue}` exchanges and queues. `ServiceBusTopology` compares names without regard to
  case, as Service Bus does.
- **Policies, not arguments.** `RabbitDefinitions` sets each queue's delivery limit and dead-lettering
  with a policy matching that queue alone, which an install can change on a queue that exists.
  MessageBroker's own provisioning uses queue arguments instead, because a service can't set policies
  over AMQP; the names are the same, so either can provision a queue the other then finds. Policies
  apply to `queues` rather than `quorum_queues`: every queue written is a quorum queue, and the
  provisioning tests show the limit taking effect that way on RabbitMQ.
- **Service Bus.** `ServiceBusTopology.BicepParameters` writes the template's parameters flat
  (topics, subscriptions, queues, senders, each naming its service) because Bicep loops over one
  array at a time. The template, [`infra/messaging/`](../../../infra/messaging/README.md), gives each
  entity the settings `TopologyStartupService.TopicOptions`, `SubscriptionOptions` and
  `QueueOptions` give it. `EmulatorConfig` writes the same entities so the provisioning tests
  exercise the filters and limits Azure gets.

## Known limitations

- **The emulator configuration isn't the template's.** Besides the limits
  [PACKAGE.md](PACKAGE.md#configure-the-service-bus-emulator) names, `ServiceBusTopology.EmulatorConfig`
  names the family rule `family` rather than replacing `$Default`, and doesn't set dead-lettering on
  filter evaluation exceptions.
- **Nothing here deploys the template.** The tests check that its parameters are the ones
  `BicepParameters` writes, that its example parameters file is current, and that its settings are
  MessageBroker's; CI compiles it (see [its README](../../../infra/messaging/README.md)).

## Testing

Tests are in `tests/Bitwarden.Server.Sdk.MessageBroker.Topology.Tests`, one class per source file,
from three sample services registered with MessageBroker (`Model.cs`). Two more classes need Docker:

- `RabbitProvisioningTests` imports the definitions into a real RabbitMQ 4.3.6 broker and sends
  MessageBroker's traffic through it.
- `ServiceBusProvisioningTests` does the same on the Service Bus emulator, configured from
  `EmulatorConfig`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.MessageBroker.Topology/tests/Bitwarden.Server.Sdk.MessageBroker.Topology.Tests
```

`ServiceBusTopologyTests` reads `infra/messaging/message-broker.bicep` and
`message-broker.parameters.json`: a parameter added to one needs adding to the other and to
`BicepParameters`.
