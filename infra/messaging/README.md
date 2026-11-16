# Messaging on Azure Service Bus

**Audience:** engineers deploying the Azure Service Bus topology for services built on
`Bitwarden.Server.Sdk.MessageBroker` whose services don't create it themselves.

`message-broker.bicep` makes what MessageBroker's
[BROKER-TOPOLOGY.md](../../extensions/Bitwarden.Server.Sdk.MessageBroker/src/BROKER-TOPOLOGY.md) asks
for on Service Bus, with the settings MessageBroker gives the entities it creates itself: the
namespace (managed identities only, no shared access keys, TLS 1.2), a topic per topic with ten
minutes of duplicate detection, a subscription per subscription whose `$Default` rule admits its
payload family alone, a queue per command queue with ten minutes of duplicate detection, a
one-minute lock and `maxDeliveryCount` on each subscription and queue, and the roles. Data Sender on
a topic for its owner and on a queue for each service that sends to it; Data Receiver on a
subscription for its reader and on a queue for its owner.

Its `topics`, `subscriptions`, `commandQueues`, `commandSenders` and `maxDeliveryCount` come from
the services' registrations, written by `Bitwarden.Server.Sdk.MessageBroker.Topology`'s
`ServiceBusTopology.BicepParameters`. `message-broker.parameters.json` is an example: what it writes
for three services, `identity`, `vault` and `notification`. The deployment gives `namespaceName` and
`principalIds`, each service's managed identity by the name the topology gives it:

```bash
az deployment group create --resource-group <group> --template-file message-broker.bicep \
  --parameters @message-broker.parameters.json \
  --parameters namespaceName=<namespace> principalIds=@principal-ids.json
```

Services then set MessageBroker's `AzureServiceBusFullyQualifiedNamespace` to the
`fullyQualifiedNamespace` output; a self-hosted service also sets `AutoProvisionTopology` to `false`.

Check the template compiles after changing it:

```bash
az bicep build --file message-broker.bicep --stdout > /dev/null
```
