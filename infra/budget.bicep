// Monthly cost budget with e-mail alerts (ADD §6.2, infrastructure-operations-architecture.md §7). A separate
// deployment from main.bicep: not every student offer supports Cost Management budgets, and a refusal here must
// not fail the rest. scripts/azure/provision.sh deploys it after main.bicep and only warns if Azure says no.
targetScope = 'subscription'

@description('Monthly budget in the subscription\'s billing currency.')
param amount int

param alertEmail string
param environmentName string = 'prod'

@description('First day of the budget; must be the first of a month.')
param startDate string = '${utcNow('yyyy-MM')}-01'

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-healthyhires-${environmentName}'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [alertEmail]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [alertEmail]
      }
    }
  }
}
