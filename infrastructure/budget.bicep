@description('Monthly cost budget amount for RG-MovieTracker-Demo, denominated in the subscription currency')
param budgetAmount int

resource movieTrackerBudget 'Microsoft.Consumption/budgets@2026-06-01' = {
  name: 'movie-tracker-budget'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-11-01'
    }
    notifications: {
      alertActual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: []
        contactRoles: [
          'Owner'
        ]
      }
      alertForecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: []
        contactRoles: [
          'Owner'
        ]
      }
    }
  }
}
