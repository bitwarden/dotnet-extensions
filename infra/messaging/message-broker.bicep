// MessageBroker's topology on Azure Service Bus (Bitwarden.Server.Sdk.MessageBroker's BROKER-TOPOLOGY.md): a topic per
// topic, a subscription per subscription filtered to its payload family, a queue per command queue, and each service's
// rights. The topics, subscriptions, queues and senders come from the services' registrations
// (Bitwarden.Server.Sdk.MessageBroker.Topology's ServiceBusTopology.BicepParameters); the namespace and each service's
// identity come from the deployment.

@description('The Service Bus namespace.')
param namespaceName string

@description('Where the namespace lives.')
param location string = resourceGroup().location

@allowed([
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('The most times a message is delivered before it is dead-lettered.')
param maxDeliveryCount int = 10

@description('Each topic: { name, owner }. Its owner alone may send to it.')
param topics array

@description('Each subscription: { topic, name, family, reader }. Only the family reaches it; its reader alone may receive from it.')
param subscriptions array

@description('Each command queue: { name, owner }. Its owner alone may receive from it.')
param commandQueues array

@description('Each service that sends commands to a queue: { queue, service }.')
param commandSenders array

@description('Each service\'s managed identity\'s principal id, by service name, as the topology names them.')
param principalIds object

// Azure Service Bus Data Sender and Data Receiver.
var dataSender = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var dataReceiver = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    // Identities only: no shared access keys.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

// Duplicate detection: a sender's outbox sends a message again, under the same identifier, after a crash.
resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = [for t in topics: {
  parent: namespace
  name: t.name
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}]

resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [for s in subscriptions: {
  name: '${namespaceName}/${s.topic}/${s.name}'
  properties: {
    maxDeliveryCount: maxDeliveryCount
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
  }
  dependsOn: [
    topic
  ]
}]

// The subscription's $Default rule, which would admit every family, admits its own alone.
resource familyFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = [for (s, i) in subscriptions: {
  parent: subscription[i]
  name: '$Default'
  properties: {
    filterType: 'CorrelationFilter'
    correlationFilter: {
      properties: {
        'x-bw-payload-family': s.family
      }
    }
  }
}]

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for q in commandQueues: {
  parent: namespace
  name: q.name
  properties: {
    maxDeliveryCount: maxDeliveryCount
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}]

resource sentQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' existing = [for s in commandSenders: {
  parent: namespace
  name: s.queue
}]

resource topicSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (t, i) in topics: {
  scope: topic[i]
  name: guid(topic[i].id, principalIds[t.owner], dataSender)
  properties: {
    roleDefinitionId: dataSender
    principalId: principalIds[t.owner]
    principalType: 'ServicePrincipal'
  }
}]

resource subscriptionReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (s, i) in subscriptions: {
  scope: subscription[i]
  name: guid(subscription[i].id, principalIds[s.reader], dataReceiver)
  properties: {
    roleDefinitionId: dataReceiver
    principalId: principalIds[s.reader]
    principalType: 'ServicePrincipal'
  }
}]

resource queueReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (q, i) in commandQueues: {
  scope: queue[i]
  name: guid(queue[i].id, principalIds[q.owner], dataReceiver)
  properties: {
    roleDefinitionId: dataReceiver
    principalId: principalIds[q.owner]
    principalType: 'ServicePrincipal'
  }
}]

resource queueSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (s, i) in commandSenders: {
  scope: sentQueue[i]
  name: guid(sentQueue[i].id, principalIds[s.service], dataSender)
  properties: {
    roleDefinitionId: dataSender
    principalId: principalIds[s.service]
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    queue
  ]
}]

output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
