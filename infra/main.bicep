// HealthyHires on Azure: every resource in one resource group (ADR-023,
// docs/architecture/infrastructure-operations-architecture.md §3). Subscription scope, so a single
// `az deployment sub create` creates the resource group too and the same command works on any subscription.
//
// Per-person values (who operates it, which SSH keys, which GitHub repository) come from infra/main.bicepparam,
// which reads them from the environment that scripts/azure/provision.sh loads from infra/azure.env.
targetScope = 'subscription'

@description('Azure region for every resource (infrastructure-operations-architecture.md §2).')
param location string = 'germanywestcentral'

@description('Short environment name, used in resource names: prod, rehearsal, ...')
@maxLength(10)
param environmentName string = 'prod'

@description('Object id of the operator in the subscription\'s tenant: az ad signed-in-user show --query id -o tsv')
param operatorObjectId string

@description('Sign-in name (UPN) of the operator; becomes the PostgreSQL Entra admin.')
param operatorPrincipalName string

@description('The operator\'s SSH public key, for the azureops admin user (reachable over NetBird only).')
param operatorSshPublicKey string

@description('Public half of the CI deploy key; the private half is a GitHub Actions secret.')
param deploySshPublicKey string

@description('GitHub repository as owner/name; only images signed by its deploy workflow are deployed.')
param githubRepository string

@description('Where alerts are e-mailed.')
param alertEmail string

@description('DNS label of the public IP: the app is served at <label>.<location>.cloudapp.azure.com.')
param dnsLabel string

param vmSize string = 'Standard_B2s'

@description('AlmaLinux 10 Marketplace image (ADR-015). Check with: az vm image list -p almalinux --all -l <location>')
param image object = {
  publisher: 'almalinux'
  offer: 'almalinux-x86_64'
  sku: '10-gen2'
}

@description('Marketplace purchase plan of the image, if it has one; empty when it has none.')
param imagePlan object = {}

param postgresVersion string = '18'
param postgresSku string = 'Standard_B1ms'

@description('Blob containers to create, one per module (ADR-A010).')
param blobContainers array = []

@description('CanNotDelete locks on PostgreSQL, Storage and Key Vault. Off for rehearsal groups, which are deleted.')
param enableDeleteLocks bool = true

@description('Key Vault purge protection. It cannot be turned off again, so off for rehearsal groups.')
param enablePurgeProtection bool = true

@description('Region of Azure OpenAI; differs from location because only swedencentral has Standard quota.')
param openAiLocation string = 'swedencentral'

@description('Restrict the resource group to EU regions with Azure Policy (ADD §6.2).')
param enableAllowedLocationsPolicy bool = true

var namePrefix = 'healthyhires-${environmentName}'
var resourceGroupName = 'rg-${namePrefix}'
var tags = {
  application: 'healthyhires'
  environment: environmentName
}

// Globally unique names (storage account, Key Vault, PostgreSQL) need a suffix. Derived from the subscription
// and resource group, so it is stable across redeploys and different on a teammate's subscription.
var suffix = take(uniqueString(subscription().id, resourceGroupName), 6)
var fqdn = '${dnsLabel}.${location}.cloudapp.azure.com'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module policy 'modules/policy.bicep' = if (enableAllowedLocationsPolicy) {
  scope: rg
  name: 'policy'
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    alertEmail: alertEmail
    fqdn: fqdn
  }
}

module network 'modules/network.bicep' = {
  scope: rg
  name: 'network'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    dnsLabel: dnsLabel
    suffix: suffix
  }
}

module vm 'modules/vm.bicep' = {
  scope: rg
  name: 'vm'
  params: {
    location: location
    name: 'vm-${namePrefix}'
    tags: tags
    vmSize: vmSize
    image: image
    imagePlan: imagePlan
    subnetId: network.outputs.vmSubnetId
    publicIpId: network.outputs.publicIpId
    adminSshPublicKey: operatorSshPublicKey
    deploySshPublicKey: deploySshPublicKey
    githubRepository: githubRepository
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    location: location
    name: 'sthealthyhires${suffix}'
    tags: tags
    corsOrigin: 'https://${fqdn}'
    containers: blobContainers
    vmPrincipalId: vm.outputs.principalId
    operatorObjectId: operatorObjectId
    enableDeleteLock: enableDeleteLocks
  }
}

module keyVault 'modules/keyvault.bicep' = {
  scope: rg
  name: 'keyvault'
  params: {
    location: location
    name: 'kv-hh-${environmentName}-${suffix}'
    tags: tags
    vmPrincipalId: vm.outputs.principalId
    operatorObjectId: operatorObjectId
    workspaceId: monitoring.outputs.workspaceId
    appInsightsName: monitoring.outputs.appInsightsName
    enablePurgeProtection: enablePurgeProtection
    enableDeleteLock: enableDeleteLocks
  }
}

module postgres 'modules/postgres.bicep' = {
  scope: rg
  name: 'postgres'
  params: {
    location: location
    name: 'psql-${namePrefix}-${suffix}'
    tags: tags
    version: postgresVersion
    skuName: postgresSku
    subnetId: network.outputs.postgresSubnetId
    privateDnsZoneId: network.outputs.postgresDnsZoneId
    operatorObjectId: operatorObjectId
    operatorPrincipalName: operatorPrincipalName
    actionGroupId: monitoring.outputs.actionGroupId
    enableDeleteLock: enableDeleteLocks
  }
}

module email 'modules/email.bicep' = {
  scope: rg
  name: 'email'
  params: {
    namePrefix: namePrefix
    suffix: suffix
    tags: tags
  }
}

module openAi 'modules/openai.bicep' = {
  scope: rg
  name: 'openai'
  params: {
    location: openAiLocation
    name: 'oai-${namePrefix}-${suffix}'
    tags: tags
    vmPrincipalId: vm.outputs.principalId
    operatorObjectId: operatorObjectId
  }
}

// Read by scripts/azure/provision.sh, which copies the deploy-time values into the GitHub environment.
output resourceGroupName string = rg.name
output vmName string = vm.outputs.name
output vmPrincipalId string = vm.outputs.principalId
output appHost string = network.outputs.fqdn
output blobEndpoint string = storage.outputs.blobEndpoint
output storageAccountName string = storage.outputs.name
output postgresHost string = postgres.outputs.fqdn
output postgresServerName string = postgres.outputs.name
output keyVaultName string = keyVault.outputs.name
output keyVaultUri string = keyVault.outputs.uri
output appInsightsName string = monitoring.outputs.appInsightsName
output emailSenderDomain string = email.outputs.senderDomain
output openAiEndpoint string = openAi.outputs.endpoint
output openAiDeployment string = openAi.outputs.deploymentName
