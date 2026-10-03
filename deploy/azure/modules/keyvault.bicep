// Key Vault (RBAC authorization) + the RSA key that wraps the Data Protection key ring.
// API versions checked on 2026-10-03 against:
//   Microsoft.KeyVault/vaults@2024-11-01      https://learn.microsoft.com/en-us/azure/templates/microsoft.keyvault/2024-11-01/vaults
//   Microsoft.KeyVault/vaults/keys@2024-11-01 https://learn.microsoft.com/en-us/azure/templates/microsoft.keyvault/2024-11-01/vaults/keys

param name string
param location string
param tags object = {}

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    // RBAC instead of access policies: roles are assigned in roles.bicep.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // Purge protection is deliberately off: it can never be turned off again and would block re-creating the
    // vault name after teardown. A production system should enable it.
    publicNetworkAccess: 'Enabled'
  }
}

// The key never leaves Key Vault: the API asks it to wrap/unwrap the Data Protection master key (wrapKey/unwrapKey).
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

output name string = vault.name
output id string = vault.id
output vaultUri string = vault.properties.vaultUri
// Unversioned key identifier: Data Protection keeps working after a key rotation.
output dataProtectionKeyId string = dataProtectionKey.properties.keyUri
