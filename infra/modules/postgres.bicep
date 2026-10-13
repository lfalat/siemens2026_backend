// Azure Database for PostgreSQL Flexible Server (ADR-006): private VNet access, Entra authentication only.
// The VM's managed identity gets its database role from scripts/azure/db-grant-identity.sh, because roles
// are created with SQL, not through Azure Resource Manager.

param location string
param name string
param tags object
param version string
param skuName string
param subnetId string
param privateDnsZoneId string
param operatorObjectId string
param operatorPrincipalName string
param actionGroupId string
param enableDeleteLock bool

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: 'Burstable'
  }
  properties: {
    version: version
    storage: {
      storageSizeGB: 32
      autoGrow: 'Enabled'
    }
    backup: {
      // Point-in-time restore inside this window covers the RPO ≤ 1 h target (ADD §6.2).
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: subnetId
      privateDnsZoneArmResourceId: privateDnsZoneId
      publicNetworkAccess: 'Disabled'
    }
    // No password exists to leak or rotate: the operator and the API both sign in with Entra tokens.
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenant().tenantId
    }
    // Sunday 18:00 UTC, the start of the weekly window (infrastructure-operations-architecture.md §5.1).
    maintenanceWindow: {
      customWindow: 'Enabled'
      dayOfWeek: 0
      startHour: 18
      startMinute: 0
    }
  }
}

resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: server
  name: operatorObjectId
  properties: {
    principalType: 'User'
    principalName: operatorPrincipalName
    tenantId: tenant().tenantId
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'healthyhires'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [entraAdmin]
}

resource storageAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${name}-storage'
  location: 'global'
  tags: tags
  properties: {
    description: 'PostgreSQL storage above 80 % (observability-architecture.md §10.3).'
    severity: 3
    enabled: true
    scopes: [server.id]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'storage'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'storage_percent'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Maximum'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
  }
}

resource lock 'Microsoft.Authorization/locks@2020-05-01' = if (enableDeleteLock) {
  scope: server
  name: 'do-not-delete'
  properties: {
    level: 'CanNotDelete'
    notes: 'Holds the application database (infrastructure-operations-architecture.md §3.3).'
  }
}

output name string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName
