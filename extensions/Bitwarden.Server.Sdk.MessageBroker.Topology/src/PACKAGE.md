# Bitwarden.Server.Sdk.MessageBroker.Topology

**Audience:** engineers writing the provisioning step (a release tool, infrastructure-as-code or its
tests) that sets up the message broker for Bitwarden services built on
`Bitwarden.Server.Sdk.MessageBroker`.

## About

Turns the messaging requirements services declare with `Bitwarden.Server.Sdk.MessageBroker` into
broker setup: RabbitMQ and LavinMQ definitions, and the parameters of an Azure Service Bus Bicep
template. It first checks every service's requirements together, as MessageBroker's
[BROKER-TOPOLOGY.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md)
asks of provisioning. It depends on `Bitwarden.Server.Sdk.MessageBroker`.

It is for installs whose services don't create their own topology: Bitwarden-managed cloud, and
installs whose operators set MessageBroker's `AutoProvisionTopology` to `false`. A self-hosted
service creates its own by default, as
[BROKER-TOPOLOGY.md](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md#creating-or-checking-the-topology-at-startup)
describes. Services don't reference this package.
