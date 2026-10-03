// Azure SQL logical server (Microsoft Entra-only authentication) + the PlantOps database on the free offer.
// API versions checked on 2026-10-03 against:
//   Microsoft.Sql/servers@2025-01-01            https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers
//   Microsoft.Sql/servers/databases@2025-01-01  https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases
// Free offer: https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql (max 4 vCores, 32 GB, auto-pause until next month).

param name string
param location string
param tags object = {}

@description('Object ID of the Entra user/group/service principal that becomes the SQL admin (the pipeline identity, so it can run migrations and the grant script).')
param adminObjectId string

@description('Display name (login) of that principal.')
param adminLogin string

@allowed(['User', 'Group', 'Application'])
param adminPrincipalType string = 'Application'

param databaseName string = 'PlantOps'

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    // No SQL logins at all: there is no password to leak.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminLogin
      sid: adminObjectId
      tenantId: subscription().tenantId
      principalType: adminPrincipalType
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// 0.0.0.0 is the documented marker for "allow Azure services" (the web and function apps connect from Azure addresses).
// Private endpoints are out of scope (design 09). The deploy workflow adds and removes a temporary rule for the runner.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    // AutoPause = never bill: when the monthly free vCore-seconds run out the database pauses until next month.
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
