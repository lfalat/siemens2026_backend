// Azure Monitor: workspace, Application Insights, the external availability test and the alert set
// (ADR-007, ADR-016, observability-architecture.md §4, §8.3, §10).

param location string
param namePrefix string
param tags object
param alertEmail string
param fqdn string

@description('Daily billable ingestion that raises a Review alert; 0.15 GB/day stays inside the free 5 GB/month.')
param dailyIngestionAlertGb string = '0.15'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 90
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${namePrefix}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'hh-ops'
    enabled: true
    emailReceivers: [
      {
        name: 'operator'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Outside-in: the only check that still works when the VM is down (observability-architecture.md §8.3).
resource healthTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: 'webtest-health-${namePrefix}'
  location: location
  tags: union(tags, {
    'hidden-link:${appInsights.id}': 'Resource'
  })
  kind: 'standard'
  properties: {
    SyntheticMonitorId: 'webtest-health-${namePrefix}'
    Name: 'Public /health'
    Kind: 'standard'
    Enabled: true
    // Standard tests are billed per run, and 15 minutes is the longest interval Azure allows. One location keeps it
    // to about 2 900 runs a month; the retry absorbs most blips on the probe's side (ADR-016).
    Frequency: 900
    Timeout: 30
    RetryEnabled: true
    // West Europe (Amsterdam), an EU location close to Germany West Central.
    Locations: [
      { Id: 'emea-nl-ams-azr' }
    ]
    Request: {
      RequestUrl: 'https://${fqdn}/health'
      HttpVerb: 'GET'
      ParseDependentRequests: false
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      SSLCheck: true
      SSLCertRemainingLifetimeCheck: 7
    }
  }
}

resource availabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${namePrefix}-availability'
  location: 'global'
  tags: tags
  properties: {
    description: 'Public /health failing (after one retry) from the probe location.'
    severity: 1
    enabled: true
    scopes: [healthTest.id, appInsights.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: healthTest.id
      componentId: appInsights.id
      failedLocationCount: 1
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Log search alerts (observability-architecture.md §10.3). `act` maps to Azure Sev 1, `review` to Sev 3.
var logAlerts = [
  {
    name: 'api-failures'
    description: 'More than 5 % of API requests failed with a 5xx in 15 minutes.'
    severity: 1
    frequency: 'PT15M'
    window: 'PT15M'
    query: 'AppRequests | where AppRoleName == "healthyhires-api" | summarize total = count(), failed = countif(toint(ResultCode) >= 500) | extend failedPercent = iff(total == 0, 0.0, 100.0 * failed / total)'
    aggregation: 'Maximum'
    measure: 'failedPercent'
    operator: 'GreaterThan'
    threshold: 5
  }
  {
    name: 'falco-detection'
    description: 'Falco raised a detection of priority Warning or higher.'
    severity: 1
    frequency: 'PT15M'
    window: 'PT15M'
    query: 'AppTraces | where AppRoleName == "falco" | extend f = parse_json(Message) | where tostring(f.rule) != "Falco internal: metrics snapshot" | where tostring(f.priority) in~ ("warning", "error", "critical", "alert", "emergency")'
    aggregation: 'Count'
    measure: ''
    operator: 'GreaterThan'
    threshold: 0
  }
  {
    name: 'collector-silent'
    description: 'No host metrics for 15 minutes: the Collector or the whole VM is down.'
    severity: 1
    frequency: 'PT15M'
    window: 'PT15M'
    query: 'AppMetrics | where AppRoleName == "host"'
    aggregation: 'Count'
    measure: ''
    operator: 'LessThan'
    threshold: 1
  }
  {
    name: 'falco-heartbeat'
    description: 'No Falco metrics snapshot for 45 minutes: Falco is not running.'
    severity: 1
    frequency: 'PT15M'
    window: 'PT45M'
    query: 'AppTraces | where AppRoleName == "falco" | where Message has "Falco internal: metrics snapshot"'
    aggregation: 'Count'
    measure: ''
    operator: 'LessThan'
    threshold: 1
  }
  {
    name: 'host-report-findings'
    description: 'The latest OpenSCAP report has failures, or AIDE found changes.'
    severity: 3
    frequency: 'PT6H'
    window: 'P1D'
    query: 'AppTraces | where AppRoleName == "host" | where Message startswith "{\\"tool\\":" | extend r = parse_json(Message) | where (tostring(r.tool) == "openscap" and (toint(r.fail) > 0 or toint(r.error) > 0 or toint(r.notchecked) > 0)) or (tostring(r.tool) == "aide" and toint(r.exit) != 0)'
    aggregation: 'Count'
    measure: ''
    operator: 'GreaterThan'
    threshold: 0
  }
  {
    name: 'host-report-missing'
    description: 'No OpenSCAP or AIDE report for two days.'
    severity: 3
    frequency: 'PT6H'
    window: 'P2D'
    query: 'datatable(tool: string)["openscap", "aide"] | join kind=leftanti (AppTraces | where AppRoleName == "host" | where Message startswith "{\\"tool\\":" | extend tool = tostring(parse_json(Message).tool) | distinct tool) on tool'
    aggregation: 'Count'
    measure: ''
    operator: 'GreaterThan'
    threshold: 0
  }
  {
    name: 'disk-usage'
    description: 'A VM filesystem is more than 85 % full.'
    severity: 3
    frequency: 'PT1H'
    window: 'PT1H'
    query: 'AppMetrics | where AppRoleName == "host" and Name == "system.filesystem.utilization" | summarize utilization = max(Max)'
    aggregation: 'Maximum'
    measure: 'utilization'
    operator: 'GreaterThan'
    threshold: json('0.85')
  }
  {
    name: 'ingestion'
    description: 'Billable ingestion today is above the daily threshold (observability-architecture.md §14).'
    severity: 3
    frequency: 'PT6H'
    window: 'P1D'
    query: 'Usage | where IsBillable | summarize gb = sum(Quantity) / 1000.0'
    aggregation: 'Maximum'
    measure: 'gb'
    operator: 'GreaterThan'
    threshold: json(dailyIngestionAlertGb)
  }
]

resource logAlertRules 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = [
  for alert in logAlerts: {
    name: 'alert-${namePrefix}-${alert.name}'
    location: location
    tags: tags
    properties: {
      displayName: 'HealthyHires: ${alert.name}'
      description: alert.description
      severity: alert.severity
      enabled: true
      scopes: [workspace.id]
      evaluationFrequency: alert.frequency
      windowSize: alert.window
      criteria: {
        allOf: [
          union(
            {
              query: alert.query
              timeAggregation: alert.aggregation
              operator: alert.operator
              threshold: alert.threshold
              failingPeriods: {
                numberOfEvaluationPeriods: 1
                minFailingPeriodsToAlert: 1
              }
            },
            empty(alert.measure) ? {} : { metricMeasureColumn: alert.measure }
          )
        ]
      }
      autoMitigate: true
      actions: {
        actionGroups: [actionGroup.id]
      }
    }
  }
]

// Control-plane changes worth knowing about between maintenance windows (infrastructure-operations-architecture.md
// §6.2). Every Bicep deployment that touches these fires it too, which is expected.
resource changeAlert 'Microsoft.Insights/activityLogAlerts@2020-10-01' = {
  name: 'alert-${namePrefix}-changes'
  location: 'global'
  tags: tags
  properties: {
    description: 'Role assignment, NSG rule or resource lock changed.'
    enabled: true
    scopes: [resourceGroup().id]
    condition: {
      allOf: [
        {
          field: 'category'
          equals: 'Administrative'
        }
        {
          anyOf: [
            {
              field: 'operationName'
              equals: 'Microsoft.Authorization/roleAssignments/write'
            }
            {
              field: 'operationName'
              equals: 'Microsoft.Network/networkSecurityGroups/securityRules/write'
            }
            {
              field: 'operationName'
              equals: 'Microsoft.Authorization/locks/delete'
            }
          ]
        }
      ]
    }
    actions: {
      actionGroups: [
        {
          actionGroupId: actionGroup.id
        }
      ]
    }
  }
}

output workspaceId string = workspace.id
output appInsightsName string = appInsights.name
output actionGroupId string = actionGroup.id
