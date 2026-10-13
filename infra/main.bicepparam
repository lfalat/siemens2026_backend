// Parameters for infra/main.bicep. Nothing here is specific to one person or one subscription: every such value
// is read from the environment, which scripts/azure/provision.sh loads from infra/azure.env (gitignored; copy
// infra/azure.env.example). A teammate's subscription therefore needs only their own azure.env.
using 'main.bicep'

param location = readEnvironmentVariable('HH_LOCATION', 'germanywestcentral')
param environmentName = readEnvironmentVariable('HH_ENVIRONMENT', 'prod')

param operatorObjectId = readEnvironmentVariable('HH_OPERATOR_OBJECT_ID')
param operatorPrincipalName = readEnvironmentVariable('HH_OPERATOR_UPN')
param operatorSshPublicKey = readEnvironmentVariable('HH_OPERATOR_SSH_PUBLIC_KEY')
param deploySshPublicKey = readEnvironmentVariable('HH_DEPLOY_SSH_PUBLIC_KEY')
param githubRepository = readEnvironmentVariable('HH_GITHUB_REPOSITORY')
param alertEmail = readEnvironmentVariable('HH_ALERT_EMAIL')
param dnsLabel = readEnvironmentVariable('HH_DNS_LABEL', 'healthyhires')

param vmSize = readEnvironmentVariable('HH_VM_SIZE', 'Standard_B2s')
param image = {
  publisher: 'almalinux'
  offer: readEnvironmentVariable('HH_IMAGE_OFFER', 'almalinux-x86_64')
  sku: readEnvironmentVariable('HH_IMAGE_SKU', '10-gen2')
}
param imagePlan = json(readEnvironmentVariable('HH_IMAGE_PLAN_JSON', '{}'))

param postgresVersion = readEnvironmentVariable('HH_POSTGRES_VERSION', '18')

param enableDeleteLocks = bool(readEnvironmentVariable('HH_ENABLE_LOCKS', 'true'))
param enablePurgeProtection = bool(readEnvironmentVariable('HH_ENABLE_PURGE_PROTECTION', 'true'))
param enableAllowedLocationsPolicy = bool(readEnvironmentVariable('HH_ENABLE_LOCATION_POLICY', 'true'))
