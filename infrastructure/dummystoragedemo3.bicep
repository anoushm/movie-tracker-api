param location string = 'westus3'
// param storageName string = 'dummystoragedemo3'

// resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' = {
//   name: storageName
//   location: location
//   sku: {
//     name: 'Standard_RAGRS'
//     tier: 'Standard'
//   }
//   kind: 'StorageV2'
//   properties: {
//     dnsEndpointType: 'Standard'
//     defaultToOAuthAuthentication: false
//     publicNetworkAccess: 'Enabled'
//     allowCrossTenantReplication: false
//     minimumTlsVersion: 'TLS1_2'
//     allowBlobPublicAccess: false
//     allowSharedKeyAccess: true
//     largeFileSharesState: 'Enabled'
//     networkAcls: {
//       bypass: 'AzureServices'
//       virtualNetworkRules: []
//       ipRules: []
//       defaultAction: 'Allow'
//     }
//     supportsHttpsTrafficOnly: true
//     encryption: {
//       requireInfrastructureEncryption: false
//       services: {
//         file: {
//           keyType: 'Account'
//           enabled: true
//         }
//         blob: {
//           keyType: 'Account'
//           enabled: true
//         }
//       }
//       keySource: 'Microsoft.Storage'
//     }
//     accessTier: 'Hot'
//   }
// }

// resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
//   parent: storageAccount
//   name: 'default'
//   properties: {
//     containerDeleteRetentionPolicy: {
//       enabled: true
//       days: 7
//     }
//     cors: {
//       corsRules: []
//     }
//     deleteRetentionPolicy: {
//       allowPermanentDelete: false
//       enabled: true
//       days: 7
//     }
//   }
// }

// resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2025-01-01' = {
//   parent: storageAccount
//   name: 'default'
//   sku: {
//     name: 'Standard_RAGRS'
//     tier: 'Standard'
//   }
//   properties: {
//     protocolSettings: {
//       smb: {}
//     }
//     cors: {
//       corsRules: []
//     }
//     shareDeleteRetentionPolicy: {
//       enabled: true
//       days: 7
//     }
//   }
// }

// resource queueServices 'Microsoft.Storage/storageAccounts/queueServices@2025-01-01' = {
//   parent: storageAccount
//   name: 'default'
//   properties: {
//     cors: {
//       corsRules: []
//     }
//   }
// }

// resource tableServices 'Microsoft.Storage/storageAccounts/tableServices@2025-01-01' = {
//   parent: storageAccount
//   name: 'default'
//   properties: {
//     cors: {
//       corsRules: []
//     }
//   }
// }

///////////////////////////////////////////////////////////////
param logAnalyticsName string = 'movie-tracker-law'
param appInsightsName string = 'movie-tracker-ai'
param environmentName string = 'movie-tracker-env'
param acrName string = 'movietracker'
param containerAppName string = 'movie-tracker-api'
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'
param containerRegistryServer string = ''
param containerCpu string = '0.5'
param containerMemory string = '1Gi'
param targetPort int = 8080
param externalIngress bool = true
param minReplicas int = 0
param maxReplicas int = 5
param azureOpenAIEndpoint string = ''
param azureOpenAIDeployment string = ''
param environmentType string = 'demo'
@secure()
@description('Azure OpenAI API Key (optional)')
param azureOpenAIKey string = ''

var commonTags = {
  domain: 'movie-tracker'
  env: environmentType
  project: 'movie-tracker-agent'
  framework: 'ms-agent-framework'
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: commonTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: environmentType == 'prod' ? -1 : 1
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    RetentionInDays: 30
  }
}

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    daprAIConnectionString: appInsights.properties.ConnectionString
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

module acr 'acr.bicep' = {
  name: 'acr-deployment'
  params: {
    name: acrName
    location: location
    tags: commonTags
  }
}

module containerAppModule 'container-app.bicep' = {
  name: 'container-app-deployment'
  params: {
    location: location
    containerAppName: containerAppName
    containerAppEnvironmentId: containerAppEnvironment.id
    environmentType: environmentType
    containerImage: containerImage
    containerRegistryServer: containerRegistryServer
    containerCpu: containerCpu
    containerMemory: containerMemory
    targetPort: targetPort
    externalIngress: externalIngress
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    appInsightsConnectionString: appInsights.properties.ConnectionString
    azureOpenAIEndpoint: azureOpenAIEndpoint
    azureOpenAIDeployment: azureOpenAIDeployment
    azureOpenAIKey: azureOpenAIKey
    commonTags: commonTags
  }
}

module acrPullRole 'acr-pull-role.bicep' = {
  name: 'acr-pull-role-deployment'
  params: {
    registryName: acr.outputs.registryName
    principalId: containerAppModule.outputs.containerAppPrincipalId
  }
}

output containerAppFqdn string = containerAppModule.outputs.containerAppFqdn
output containerAppUrl string = 'https://${containerAppModule.outputs.containerAppFqdn}'
output environmentId string = containerAppEnvironment.id
output logAnalyticsWorkspaceId string = logAnalytics.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output containerAppId string = containerAppModule.outputs.containerAppId
output containerAppName string = containerAppModule.outputs.containerAppName
output acrLoginServer string = acr.outputs.loginServer
output acrRegistryId string = acr.outputs.registryId
/////////////////////////////////////////////////////////////////
// output storageAccountName string = storageAccount.name
// output storageAccountId string = storageAccount.id
// output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
