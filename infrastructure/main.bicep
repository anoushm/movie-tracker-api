@description('The location for all resources')
param location string = resourceGroup().location

@description('The name of the Container App')
param containerAppName string = 'movie-tracker-api'

@description('The name of the Container Apps Environment')
param environmentName string = 'movie-tracker-env'

@description('The name of the Log Analytics Workspace')
param logAnalyticsName string = 'movie-tracker-law'

@description('The name of the Application Insights resource')
param appInsightsName string = 'movie-tracker-ai'

@description('The name of the Key Vault')
param keyVaultName string = 'movie-tracker-kv'

@description('The principal ID of the user to grant Key Vault Administrator access')
param userPrincipalId string = ''

@description('Environment type for tagging')
@allowed(['demo', 'dev', 'staging', 'prod'])
param environmentType string = 'demo'

@description('The name of the Azure Container Registry')
param acrName string = 'movietracker'

@description('Container image to deploy')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('CPU cores for the container')
param containerCpu string = '0.5'

@description('Memory for the container')
param containerMemory string = '1Gi'

@description('Target port for the container')
param targetPort int = 8080

@description('Enable external ingress')
param externalIngress bool = true

@description('Minimum number of replicas')
param minReplicas int = 0

@description('Maximum number of replicas')
param maxReplicas int = 5

@description('Azure OpenAI endpoint (optional)')
param azureOpenAIEndpoint string = ''

@description('Azure OpenAI deployment name (optional)')
param azureOpenAIDeployment string = ''

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

module keyVault 'key-vault.bicep' = {
  name: 'key-vault-deployment'
  params: {
    name: keyVaultName
    location: location
    tags: commonTags
    environmentType: environmentType
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
    containerRegistryServer: acr.outputs.loginServer
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

module rbac 'rbac.bicep' = {
  name: 'rbac-deployment'
  params: {
    appInsightsId: appInsights.id
    containerAppPrincipalId: containerAppModule.outputs.containerAppPrincipalId
    keyVaultId: keyVault.outputs.keyVaultId
    userPrincipalId: userPrincipalId
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
output keyVaultUri string = keyVault.outputs.keyVaultUri
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultId string = keyVault.outputs.keyVaultId
