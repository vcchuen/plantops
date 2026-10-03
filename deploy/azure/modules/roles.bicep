// All role assignments in one place (managed identities: no keys anywhere).
// Role IDs checked on 2026-10-03 against the built-in role definitions in
//   https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/security  (Key Vault)
//   https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/storage   (Storage)
//   https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/integration (Service Bus)
// Host-storage roles for Functions: https://learn.microsoft.com/en-us/azure/azure-functions/manage-connections
// Microsoft.Authorization/roleAssignments@2022-04-01: https://learn.microsoft.com/en-us/azure/templates/microsoft.authorization/roleassignments

param webPrincipalId string
param functionPrincipalId string
param keyVaultName string
param storageAccountName string
param dataProtectionContainerName string
param serviceBusNamespaceName string

var roleIds = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultCryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
}

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' existing = {
  parent: storage
  name: 'default'
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' existing = {
  parent: blobService
  name: dataProtectionContainerName
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

// ---- Web identity ----
// Reads secrets (Auth--ClientSecret) through the configuration provider.
resource webSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, webPrincipalId, roleIds.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.keyVaultSecretsUser)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Wraps/unwraps the Data Protection key ring with the Key Vault key.
resource webCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, webPrincipalId, roleIds.keyVaultCryptoUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.keyVaultCryptoUser)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the one container, not the whole account.
resource webKeyRing 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionContainer.id, webPrincipalId, roleIds.storageBlobDataContributor)
  scope: dataProtectionContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageBlobDataContributor)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource webSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, webPrincipalId, roleIds.serviceBusDataSender)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataSender)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Function app identity ----
resource funcSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, functionPrincipalId, roleIds.serviceBusDataSender)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataSender)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource funcReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, functionPrincipalId, roleIds.serviceBusDataReceiver)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataReceiver)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Host storage (timer lock, diagnostics) and the Flex deployment package, all with the managed identity.
resource funcBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionPrincipalId, roleIds.storageBlobDataOwner)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageBlobDataOwner)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource funcQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionPrincipalId, roleIds.storageQueueDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageQueueDataContributor)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource funcTable 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionPrincipalId, roleIds.storageTableDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageTableDataContributor)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}
