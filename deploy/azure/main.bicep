// PlantOps on Azure (design 09). Scope: an existing resource group. Nothing here has been deployed yet.
// What it creates: App Service (web + SPA), Functions Flex Consumption, Azure SQL (Entra-only, free offer),
// Service Bus Standard, Key Vault, Storage, Log Analytics + Application Insights, three alert rules, and every role assignment.
// Each module's header lists the Microsoft Learn page its API version and properties were checked against.

targetScope = 'resourceGroup'

@description('Prefix for every resource name (lowercase letters, digits, hyphens).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'plantops'

param location string = resourceGroup().location

@description('B1 = Always On, about 13 USD/month at list price. F1 = free, but no Always On (the outbox dispatcher sleeps when the app idles).')
@allowed(['B1', 'F1'])
param appServiceSku string = 'B1'

@description('Object ID of the Entra principal that becomes the SQL admin: the identity GitHub Actions logs in as (or a group containing it).')
param sqlAdminObjectId string

@description('Display name of that principal (shown as the SQL admin login).')
param sqlAdminLogin string

@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'Application'

@description('OIDC authority of the identity provider, e.g. https://login.microsoftonline.com/<tenant-id>/v2.0')
param authAuthority string

@description('Client ID of the Entra app registration the web app signs users in with. The client SECRET is not a parameter: store it in Key Vault as the secret "Auth--ClientSecret".')
param authClientId string

param factoryTimeZone string = 'Asia/Kuala_Lumpur'

@description('Email for alert notifications. Empty = no action group.')
param alertEmail string = ''

var token = toLower(uniqueString(resourceGroup().id))
var prefix = toLower(namePrefix)
var tags = {
  app: 'plantops'
  managedBy: 'bicep'
}

var webName = '${prefix}-web-${take(token, 6)}'
var functionName = '${prefix}-func-${take(token, 6)}'
var sqlName = '${prefix}-sql-${take(token, 6)}'
var serviceBusName = '${prefix}-sb-${take(token, 6)}'
var keyVaultName = 'kv-${take(prefix, 8)}-${take(token, 10)}' // 3-24 chars
var storageName = 'st${take(replace(prefix, '-', ''), 8)}${take(token, 12)}' // 3-24 lowercase alphanumerics

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    namePrefix: prefix
    location: location
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: keyVaultName
    location: location
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: storageName
    location: location
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    name: sqlName
    location: location
    tags: tags
    adminObjectId: sqlAdminObjectId
    adminLogin: sqlAdminLogin
    adminPrincipalType: sqlAdminPrincipalType
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    name: serviceBusName
    location: location
    tags: tags
  }
}

module web 'modules/web.bicep' = {
  name: 'web'
  params: {
    name: webName
    location: location
    tags: tags
    appServiceSku: appServiceSku
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    keyVaultUri: keyVault.outputs.vaultUri
    dataProtectionBlobUri: storage.outputs.dataProtectionBlobUri
    dataProtectionKeyId: keyVault.outputs.dataProtectionKeyId
    appInsightsName: monitoring.outputs.appInsightsName
    serviceBusFullyQualifiedNamespace: serviceBus.outputs.fullyQualifiedNamespace
    serviceBusQueueName: serviceBus.outputs.queueName
    authAuthority: authAuthority
    authClientId: authClientId
    factoryTimeZone: factoryTimeZone
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    name: functionName
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    deploymentContainerUrl: storage.outputs.deploymentContainerUrl
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    appInsightsName: monitoring.outputs.appInsightsName
    serviceBusFullyQualifiedNamespace: serviceBus.outputs.fullyQualifiedNamespace
    serviceBusQueueName: serviceBus.outputs.queueName
    factoryTimeZone: factoryTimeZone
  }
}

module roles 'modules/roles.bicep' = {
  name: 'roles'
  params: {
    webPrincipalId: web.outputs.principalId
    functionPrincipalId: functions.outputs.principalId
    keyVaultName: keyVault.outputs.name
    storageAccountName: storage.outputs.name
    dataProtectionContainerName: storage.outputs.dataProtectionContainerName
    serviceBusNamespaceName: serviceBus.outputs.name
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    namePrefix: prefix
    location: location
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
    alertEmail: alertEmail
  }
}

// Names, URLs and principal IDs only: Bicep outputs are stored in the deployment history in clear text, so no secrets.
output webAppName string = web.outputs.name
output webAppUrl string = 'https://${web.outputs.defaultHostName}'
output functionAppName string = functions.outputs.name
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.name
output keyVaultUri string = keyVault.outputs.vaultUri
output serviceBusNamespace string = serviceBus.outputs.fullyQualifiedNamespace
output appInsightsId string = monitoring.outputs.appInsightsId
output webPrincipalId string = web.outputs.principalId
output functionPrincipalId string = functions.outputs.principalId
