// Key Vault for the few remaining secrets (ADR-012, iam.md §5). RBAC only, no access policies.

param location string
param name string
param tags object
param vmPrincipalId string
param operatorObjectId string
param workspaceId string
param appInsightsName string
param enablePurgeProtection bool
param enableDeleteLock bool

var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // Purge protection can only be switched on, never off, so it is null rather than false when disabled.
    enablePurgeProtection: enablePurgeProtection ? true : null
  }
}

// The Collector's export credential (observability-architecture.md §2.4). Read on the VM at Collector start
// by deploy/host/hh-podman-secret and handed to the container as a Podman secret.
resource appInsightsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'appinsights-connection-string'
  properties: {
    value: appInsights.properties.ConnectionString
  }
}

// Per secret, not per vault (iam.md §4.2). New secrets get their own assignment when they are added.
resource vmReadsAppInsightsSecret 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appInsightsSecret
  name: guid(appInsightsSecret.id, vmPrincipalId, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: vmPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// The operator sets secret values by hand (infrastructure-operations-architecture.md §4.1).
resource operatorManagesSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, operatorObjectId, keyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficer)
    principalId: operatorObjectId
    principalType: 'User'
  }
}

// Every secret and key access, as security evidence (observability-architecture.md §4.1).
resource auditLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: vault
  name: 'audit-to-workspace'
  properties: {
    workspaceId: workspaceId
    logs: [
      {
        category: 'AuditEvent'
        enabled: true
      }
    ]
  }
}

resource lock 'Microsoft.Authorization/locks@2020-05-01' = if (enableDeleteLock) {
  scope: vault
  name: 'do-not-delete'
  properties: {
    level: 'CanNotDelete'
    notes: 'Holds the application secrets (infrastructure-operations-architecture.md §3.3).'
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
