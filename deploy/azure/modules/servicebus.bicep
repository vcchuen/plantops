// Service Bus namespace (Standard) + the sla-breaches queue with duplicate detection.
// API versions checked on 2026-10-03 against:
//   Microsoft.ServiceBus/namespaces@2024-01-01        https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2024-01-01/namespaces
//   Microsoft.ServiceBus/namespaces/queues@2024-01-01 https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2024-01-01/namespaces/queues

param name string
param location string
param tags object = {}
param queueName string = 'sla-breaches'

resource sbNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    // Duplicate detection is not available on Basic (design 09).
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // Managed identities only: SAS keys are switched off.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: sbNamespace
  name: queueName
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
  }
}

output name string = sbNamespace.name
output id string = sbNamespace.id
output fullyQualifiedNamespace string = '${sbNamespace.name}.servicebus.windows.net'
output queueName string = queue.name
