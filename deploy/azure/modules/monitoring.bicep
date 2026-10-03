// Log Analytics workspace + workspace-based Application Insights.
// API versions checked on 2026-10-03 against:
//   Microsoft.OperationalInsights/workspaces@2025-02-01
//     https://learn.microsoft.com/en-us/azure/templates/microsoft.operationalinsights/2025-02-01/workspaces
//   Microsoft.Insights/components@2020-02-02 (still the current stable version for Application Insights)
//     https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/components

param namePrefix string
param location string
param tags object = {}

@description('Hard daily ingestion cap (GB) so a logging bug cannot create a surprise bill. Log Analytics stops ingesting for the rest of the day when reached.')
param dailyQuotaGb int = 1

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: '${namePrefix}-log'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: dailyQuotaGb
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    // Workspace-based: telemetry lands in the workspace tables (AppTraces, AppRequests, ...), which is what the alert rules query.
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceId string = workspace.id
output workspaceName string = workspace.name
output appInsightsName string = appInsights.name
output appInsightsId string = appInsights.id
