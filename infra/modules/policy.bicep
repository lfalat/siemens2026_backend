// Keeps every resource in this resource group in an EU region (ADD §6.2). Assigned to the resource group, not
// the subscription: a student subscription often holds other coursework that this rule should not touch.

// The list is the EU part of what the Azure for Students subscription's own "Allowed resource deployment regions"
// policy permits; a region outside it would be denied anyway. switzerlandnorth is allowed there but is not in the EU.
@description('EU regions a resource may be created in.')
param allowedLocations array = [
  'germanywestcentral'
  'swedencentral'
  'polandcentral'
  'austriaeast'
]

resource allowedLocationsPolicy 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'healthyhires-allowed-locations'
  properties: {
    displayName: 'HealthyHires: EU regions only'
    // Built-in "Allowed locations". It ignores resources whose location is 'global' (DNS zones, ACS, alerts).
    policyDefinitionId: tenantResourceId('Microsoft.Authorization/policyDefinitions', 'e56962a6-4747-49cd-b67b-bf8b01975c4c')
    parameters: {
      listOfAllowedLocations: {
        value: allowedLocations
      }
    }
  }
}
