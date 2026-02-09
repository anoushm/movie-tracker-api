param location string
param environmentName string
param logAnalyticsCustomerId string
param logAnalyticsPrimarySharedKey string
param appInsightsConnectionString string
param commonTags object

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsPrimarySharedKey
      }
    }
    daprAIConnectionString: appInsightsConnectionString
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output containerAppEnvironmentId string = containerAppEnvironment.id
