// Azure Communication Services Email with an Azure-managed sender domain, data kept in Europe (ADR-010).
// The managed-identity role for sending is still open (iam.md §9), so none is assigned here yet.

param namePrefix string
param suffix string
param tags object

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'ecs-${namePrefix}-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Europe'
  }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${namePrefix}-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Europe'
    linkedDomains: [managedDomain.id]
  }
}

output senderDomain string = managedDomain.properties.mailFromSenderDomain
