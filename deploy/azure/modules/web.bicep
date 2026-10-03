// App Service plan (Linux) + the web app that serves the API and the Angular SPA (one app, same origin).
// API versions checked on 2026-10-03 against:
//   Microsoft.Web/serverfarms@2025-03-01 https://learn.microsoft.com/en-us/azure/templates/microsoft.web/2025-03-01/serverfarms
//   Microsoft.Web/sites@2025-03-01       https://learn.microsoft.com/en-us/azure/templates/microsoft.web/2025-03-01/sites

param name string
param location string
param tags object = {}

@allowed(['B1', 'F1'])
param appServiceSku string = 'B1'

param sqlServerFqdn string
param databaseName string
param keyVaultUri string
param dataProtectionBlobUri string
param dataProtectionKeyId string
param appInsightsName string
param serviceBusFullyQualifiedNamespace string
param serviceBusQueueName string
param authAuthority string
param authClientId string
param factoryTimeZone string

var isFree = appServiceSku == 'F1'

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: '${name}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
    tier: isFree ? 'Free' : 'Basic'
  }
  properties: {
    reserved: true // Linux
  }
}

resource site 'Microsoft.Web/sites@2025-03-01' = {
  name: name
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // Free (F1) has no Always On: the in-process outbox dispatcher then sleeps when the app idles (design 09).
      alwaysOn: !isFree
      http20Enabled: true
      // Anonymous by design; it checks every module's database (tag "ready").
      healthCheckPath: '/health/ready'
      appSettings: [
        // Entra auth, no password. "Active Directory Default" resolves to the app's managed identity in App Service.
        // The identity becomes a contained database user in deploy/azure/sql/grant-app-identities.sql.
        {
          name: 'ConnectionStrings__PlantOps'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Default;Encrypt=True;'
        }
        {
          // The Key Vault configuration provider reads every secret of this vault. The OIDC client secret is the secret named
          // "Auth--ClientSecret" (the provider maps "--" to ":"), created by the operator after the first deploy.
          // It is NOT an app setting and NOT a Key Vault reference, so it never appears in the portal's configuration blade.
          name: 'KeyVault__Uri'
          value: keyVaultUri
        }
        {
          name: 'DataProtection__BlobUri'
          value: dataProtectionBlobUri
        }
        {
          name: 'DataProtection__KeyId'
          value: dataProtectionKeyId
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ServiceBus__FullyQualifiedNamespace'
          value: serviceBusFullyQualifiedNamespace
        }
        {
          name: 'ServiceBus__SlaBreachQueue'
          value: serviceBusQueueName
        }
        {
          name: 'Auth__Authority'
          value: authAuthority
        }
        {
          name: 'Auth__ClientId'
          value: authClientId
        }
        {
          // App Service terminates TLS in its front end; without this the app sees http and the __Host- cookie breaks.
          name: 'ForwardedHeaders__TrustAll'
          value: 'true'
        }
        {
          name: 'Factory__TimeZone'
          value: factoryTimeZone
        }
        {
          // Migrations run from the pipeline's bundles; the app identity has no DDL rights (ADR-0004).
          name: 'Database__ApplyMigrationsOnStartup'
          value: 'false'
        }
        {
          name: 'Seed__Demo'
          value: 'false'
        }
      ]
    }
  }
}

output name string = site.name
output id string = site.id
output defaultHostName string = site.properties.defaultHostName
output principalId string = site.identity.principalId
