// Azure OpenAI for CV scoring against role requirements. Entra-only: key access is off, so the API calls it with
// the VM's managed identity. A Standard deployment processes data only in the resource's region; the student
// subscription has Standard quota for gpt-4o in swedencentral only (EU data zone quota is 0). gpt-4o-mini and older
// gpt-4o versions no longer accept new Standard deployments; 2024-11-20 does until 2027-04-14.

param location string
param name string
param tags object
param vmPrincipalId string
param operatorObjectId string

@description('Model and version; check with: az cognitiveservices model list -l <location>')
param modelName string = 'gpt-4o'
param modelVersion string = '2024-11-20'

@description('Throughput in thousands of tokens per minute; counts against the regional quota (50 for gpt-4o).')
param capacity int = 20

var cognitiveServicesOpenAiUser = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    // A custom subdomain is required for Entra authentication.
    customSubDomainName: name
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: modelName
  sku: {
    name: 'Standard'
    capacity: capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

// Call the deployed model; no rights to change deployments or read keys.
resource vmOpenAiAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, vmPrincipalId, cognitiveServicesOpenAiUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUser)
    principalId: vmPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// The operator tries prompts from a workstation. Owner has no data-plane rights, so it needs the role too.
resource operatorOpenAiAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, operatorObjectId, cognitiveServicesOpenAiUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUser)
    principalId: operatorObjectId
    principalType: 'User'
  }
}

output endpoint string = account.properties.endpoint
output deploymentName string = deployment.name
