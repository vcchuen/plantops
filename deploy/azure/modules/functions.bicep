// Function App on the Flex Consumption plan (Linux, .NET 9 isolated) with identity-based host storage.
// API versions checked on 2026-10-03 against:
//   Microsoft.Web/serverfarms@2025-03-01 (sku FC1 / tier FlexConsumption) https://learn.microsoft.com/en-us/azure/templates/microsoft.web/2025-03-01/serverfarms
//   Microsoft.Web/sites@2025-03-01 (functionAppConfig)                    https://learn.microsoft.com/en-us/azure/templates/microsoft.web/2025-03-01/sites
//   Reference template: https://github.com/Azure-Samples/azure-functions-flex-consumption-samples/blob/main/IaC/bicep/main.bicep
//   Identity-based AzureWebJobsStorage settings: https://learn.microsoft.com/en-us/azure/azure-functions/manage-connections
// FUNCTIONS_WORKER_RUNTIME / FUNCTIONS_EXTENSION_VERSION are not set: on Flex the runtime comes from functionAppConfig.runtime.

param name string
param location string
param tags object = {}

param storageAccountName string
param deploymentContainerUrl string
param sqlServerFqdn string
param databaseName string
param appInsightsName string
param serviceBusFullyQualifiedNamespace string
param serviceBusQueueName string
param factoryTimeZone string

@description('Flex Consumption allows 512, 2048 or 4096 MB per instance. 2048 is the default; memory is billed per GB-second.')
@allowed([512, 2048, 4096])
param instanceMemoryMB int = 2048

@description('Flex Consumption minimum is 40. The timers are single-instance anyway; the cap bounds cost.')
@minValue(40)
@maxValue(1000)
param maximumInstanceCount int = 40

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: '${name}-plan'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true // Linux only
  }
}

resource site 'Microsoft.Web/sites@2025-03-01' = {
  name: name
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: deploymentContainerUrl
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        // Host storage over the managed identity (no connection string, shared-key access is off on the account).
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'ServiceBus__fullyQualifiedNamespace'
          value: serviceBusFullyQualifiedNamespace
        }
        {
          // Read by the ServiceBusTrigger as %SlaBreachQueue%.
          name: 'SlaBreachQueue'
          value: serviceBusQueueName
        }
        {
          // Read by the WorkOrders module's publisher (config key ServiceBus:SlaBreachQueue). Both are needed (CLAUDE.md, M6).
          name: 'ServiceBus__SlaBreachQueue'
          value: serviceBusQueueName
        }
        {
          name: 'ConnectionStrings__PlantOps'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Default;Encrypt=True;'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Factory__TimeZone'
          value: factoryTimeZone
        }
      ]
    }
  }
}

output name string = site.name
output id string = site.id
output principalId string = site.identity.principalId
