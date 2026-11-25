# Bitwarden.Server.Sdk.MessageBroker.Topology

**Audience:** engineers writing the provisioning step (a release tool, infrastructure-as-code or its
tests) that sets up the message broker for Bitwarden services built on
`Bitwarden.Server.Sdk.MessageBroker`.

## About

Turns the messaging requirements services declare with `Bitwarden.Server.Sdk.MessageBroker` into
broker setup: RabbitMQ definitions, and the parameters of an Azure Service Bus Bicep
template. It first checks every service's requirements together, as MessageBroker's
[BROKER-TOPOLOGY.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md)
asks of provisioning. It depends on `Bitwarden.Server.Sdk.MessageBroker`.

It is for installs whose services don't create their own topology: Bitwarden-managed cloud, and
installs whose operators set MessageBroker's `AutoProvisionTopology` to `false`. A self-hosted
service creates its own by default, as
[BROKER-TOPOLOGY.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md#creating-or-checking-the-topology-at-startup)
describes. Services don't reference this package.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.MessageBroker.Topology
```

## Key concepts

- **A manifest** is what one service declares, as `Bitwarden.Server.Sdk.Infrastructure.Testing`'s
  `InfrastructureManifest.ToJsonString` writes it, under the name the topology gives the service.
  That name owns the service's topics and queues and holds its rights. Only MessageBroker's kinds
  (`MessagingRequirements`) are read.
- **A topology** (`BrokerTopology`) is every service's manifest, checked together: each topic and
  command queue has one owner, each subscription one reader and one family, and every subscription
  and command matches what its owner publishes or consumes. It is the same for every broker.
- **A writer** turns a topology into one broker's setup and checks the names that broker allows.
  No writer outputs a user, password, key or connection string: each install creates its own
  credentials, so the output can sit in a repository or ship with a release.
- **Rights follow ownership.** Only a topic's owner may send to it, only a subscription's reader
  and a queue's owner may receive, and only the services that declare a command for a queue may send
  to it. The Azure Service Bus template grants exactly those when deployed as a deployment stack, as
  [its README](https://github.com/bitwarden/dotnet-extensions/blob/main/infra/messaging/README.md)
  says; a plain deployment leaves rights a service no longer needs. On RabbitMQ, an
  install grants them to each service's user, as BROKER-TOPOLOGY.md's "Users and permissions" says.

## Usage

### Collect and check every service's requirements

Collect each service's manifest with `InfrastructureCollector`, for the hosting mode being
provisioned, and save it. Then, in the provisioning tool's `Program.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker.Topology;

var topology = BrokerTopology.Collect(
[
    ServiceManifest.Parse("identity", File.ReadAllText("identity.json")),
    ServiceManifest.Parse("vault", File.ReadAllText("vault.json")),
]);
```

`Collect` throws `BrokerTopologyException` when the services don't fit together. Its `Problems`
lists every one, not just the first:

- a service with two manifests;
- a topic or command queue claimed by two services;
- a family published on a topic, or consumed from a command queue, by a service that doesn't own
  it;
- a subscription read by two services, or bound to two families;
- a subscription to a topic no service owns, or to a family its owner doesn't publish;
- a command sent to a queue no service owns, or that its owner doesn't consume.

A test that already runs the services' startup can skip the files:
`ServiceManifest.FromRequirements` takes the requirements as `InfrastructureOptions` holds them.

### Write RabbitMQ definitions

In `Program.cs`, after collecting:

```csharp
File.WriteAllText("definitions.json", RabbitDefinitions.Build(topology).ToJsonString());
```

RabbitMQ imports the file with `rabbitmqctl import_definitions` or loads it at start
(`management.load_definitions`). It holds the shapes BROKER-TOPOLOGY.md describes, under the names a service that creates its own topology
uses:

| For each                       | Definitions                                                                                      |
| ------------------------------ | ------------------------------------------------------------------------------------------------ |
| File                           | The virtual host, `RabbitDefinitionsOptions.VirtualHost` (`/` by default)                        |
| Topic                          | A durable topic exchange `{topic}`                                                               |
| Subscription                   | A durable quorum queue `{topic}.{subscription}`, bound with the family's wire name               |
| Command queue                  | A durable direct exchange and a durable quorum queue, both `{queue}`, bound with the queue name  |
| Subscription and command queue | A dead-letter fanout exchange and quorum queue `dead-letter.{queue}`, bound with an empty key    |
| Subscription and command queue | A policy matching its queue exactly: the delivery limit, its dead-letter exchange, at-least-once |

The policies' `delivery-limit` is one less than `RabbitDefinitionsOptions.MaxDeliveryCount` (10 by
default, as on Azure Service Bus), since the broker counts redeliveries.

`Build` throws `BrokerTopologyException` when two declarations would make the same exchange or
queue name, when a topic or command queue is named `dead-letter` or starts with `dead-letter.`, or
when a name is longer than 255 bytes.

### Write the Azure Service Bus template's parameters

In `Program.cs`, after collecting:

```csharp
File.WriteAllText("message-broker.parameters.json", ServiceBusTopology.BicepParameters(topology).ToJsonString());
```

The file holds the parameters of
[`infra/messaging/message-broker.bicep`](https://github.com/bitwarden/dotnet-extensions/blob/main/infra/messaging/message-broker.bicep)
that come from the services. The deployment adds `namespaceName` and `principalIds`, each service's
managed identity by the name the topology gives it:

| Parameter          | Each                                 | Makes                                                                                                                                      |
| ------------------ | ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `topics`           | `{ name, owner }`                    | A topic, with duplicate detection; Data Sender for its owner                                                                               |
| `subscriptions`    | `{ topic, name, family, reader }`    | A subscription whose `$Default` rule admits its family alone (a correlation filter on `x-bw-payload-family`); Data Receiver for its reader |
| `commandQueues`    | `{ name, owner }`                    | A queue, with duplicate detection; Data Receiver for its owner                                                                             |
| `commandSenders`   | `{ queue, service }`                 | Data Sender on the queue, once per service however many families it sends                                                                  |
| `maxDeliveryCount` | `ServiceBusOptions.MaxDeliveryCount` | Each subscription's and queue's delivery limit (10 by default)                                                                             |

The entities get the settings a service that creates its own topology gives them: duplicate
detection over ten minutes on topics and queues, a one-minute lock, and dead-lettering on expiry.

`BicepParameters` throws `BrokerTopologyException` for a name Service Bus doesn't allow (letters,
digits, `.`, `-` and `_`, starting and ending with a letter or digit; 260 characters, or 50 for a
subscription), for a topic and a queue whose names are the same without regard to case, and for two
subscriptions on one topic whose names are.

### Configure the Service Bus emulator

`ServiceBusTopology.EmulatorConfig` writes the same entities as the configuration of Microsoft's
Service Bus emulator (`/ServiceBus_Emulator/ConfigFiles/Config.json`), for tests and local runs. The
emulator grants no rights, and allows a duplicate-detection window of five minutes where the
template sets ten.

## Gotchas and constraints

- **Name each service the same everywhere.** The name given to `ServiceManifest.Parse` is the owner
  and reader in the output, and the key of `principalIds` in the deployment.
- **Collect for the hosting mode you provision.** A service can declare different requirements when
  self-hosted, so collect its manifest the way it will run.
- **Importing adds and updates.** RabbitMQ doesn't delete an exchange, queue or policy that a later
  definitions file leaves out; remove it by hand.
- **A deployment stack deletes what the parameters leave out.** On Azure Service Bus, a topic,
  subscription or queue that a later parameters file leaves out is deleted with its role
  assignments, and with it every message it holds, its dead-letter queue's included. Drain or move
  the messages of a subscription or queue before a release stops declaring it.
- **The emulator isn't Azure.** It checks entities, filters and delivery limits, not rights; only a
  deployment exercises the template's role assignments.
