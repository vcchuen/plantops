// One storage account: Data Protection key ring (blob), Functions host storage (blob/queue/table) and the Flex Consumption deployment package.
// API versions checked on 2026-10-03 against:
//   Microsoft.Storage/storageAccounts@2025-01-01 https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2025-01-01/storageaccounts
//   .../blobServices@2025-01-01 and .../blobServices/containers@2025-01-01
//     https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2025-01-01/storageaccounts/blobservices/containers
// Shared-key access is off (as in the Flex Consumption sample, https://github.com/Azure-Samples/azure-functions-flex-consumption-samples/tree/main/IaC/bicep):
// everything authenticates with managed identities.

param name string
param location string
param tags object = {}

resource account 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: account
  name: 'default'
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: {
    publicAccess: 'None'
  }
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'function-deployments'
  properties: {
    publicAccess: 'None'
  }
}

output name string = account.name
output id string = account.id
output blobEndpoint string = account.properties.primaryEndpoints.blob
output dataProtectionBlobUri string = '${account.properties.primaryEndpoints.blob}${dataProtectionContainer.name}/keys.xml'
output dataProtectionContainerName string = dataProtectionContainer.name
output deploymentContainerUrl string = '${account.properties.primaryEndpoints.blob}${deploymentContainer.name}'
