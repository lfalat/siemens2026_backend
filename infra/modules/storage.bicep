// Blob Storage for uploads (ADR-005, ADR-A010). Entra-only: shared-key access is off, so every access goes
// through a managed identity or a user-delegation SAS.

param location string
param name string
param tags object
param corsOrigin string
param containers array
param vmPrincipalId string
param operatorObjectId string
param enableDeleteLock bool

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // Browsers upload and download directly with SAS URLs, from the application origin only.
    cors: {
      corsRules: [
        {
          allowedOrigins: [corsOrigin]
          allowedMethods: ['GET', 'HEAD', 'PUT', 'OPTIONS']
          allowedHeaders: ['*']
          exposedHeaders: ['*']
          maxAgeInSeconds: 3600
        }
      ]
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for container in containers: {
    parent: blobService
    name: container
    properties: {
      publicAccess: 'None'
    }
  }
]

// Read and write module containers and create user-delegation SAS URLs (iam.md §4.2).
resource vmBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, vmPrincipalId, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: vmPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// The operator copies blobs when moving to another subscription (scripts/azure/migrate.sh). Shared-key access is
// off, so even the subscription owner needs a data-plane role.
resource operatorBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, operatorObjectId, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: operatorObjectId
    principalType: 'User'
  }
}

resource lock 'Microsoft.Authorization/locks@2020-05-01' = if (enableDeleteLock) {
  scope: account
  name: 'do-not-delete'
  properties: {
    level: 'CanNotDelete'
    notes: 'Holds uploaded CVs and cover letters (infrastructure-operations-architecture.md §3.3).'
  }
}

output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
