// Scheduled-query alert rules for the M8 security/outbox EventIds, plus an optional email action group.
// API versions checked on 2026-10-03 against:
//   Microsoft.Insights/scheduledQueryRules@2023-12-01 https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2023-12-01/scheduledqueryrules
//   Microsoft.Insights/actionGroups@2023-01-01        https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2023-01-01/actiongroups
// UNVERIFIED until a real deploy: the exact AppTraces column that carries the ILogger EventId when exported by the Azure Monitor
// OpenTelemetry distro. The queries read Properties.EventId; adjust after looking at one real trace row in Log Analytics.

param namePrefix string
param location string
param tags object = {}
param workspaceId string

@description('Email address for alert notifications. Empty = rules are created without an action group (visible in the portal only).')
param alertEmail string = ''

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (!empty(alertEmail)) {
  name: '${namePrefix}-alerts'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: take(namePrefix, 12)
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

var actionGroupIds = empty(alertEmail) ? [] : [actionGroup.id]

var rules = [
  {
    key: 'csrf-rejected'
    displayName: 'PlantOps: CSRF rejected (EventId 1004)'
    description: 'More than 20 requests rejected by the CSRF guard in 5 minutes.'
    eventId: '1004'
    threshold: 20
    severity: 2
  }
  {
    key: 'rate-limited'
    displayName: 'PlantOps: rate limited (EventId 1005)'
    description: 'More than 100 requests rejected by the rate limiter in 5 minutes.'
    eventId: '1005'
    threshold: 100
    severity: 3
  }
  {
    key: 'outbox-parked'
    displayName: 'PlantOps: outbox message parked (EventId 1006)'
    description: 'At least one outbox message was parked after exhausting its retries.'
    eventId: '1006'
    threshold: 0 // "greater than 0" = at least one
    severity: 1
  }
]

resource alertRules 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = [for rule in rules: {
  name: '${namePrefix}-${rule.key}'
  location: location
  tags: tags
  properties: {
    displayName: rule.displayName
    description: rule.description
    enabled: true
    severity: rule.severity
    scopes: [
      workspaceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'AppTraces | where tostring(Properties.EventId) == "${rule.eventId}"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: rule.threshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    actions: {
      actionGroups: actionGroupIds
    }
  }
}]
