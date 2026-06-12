param location string
param containerAppName string
param containerAppEnvironmentId string
param environmentType string
param containerImage string
param containerRegistryServer string
param containerCpu string
param containerMemory string
param targetPort int
param externalIngress bool
param minReplicas int
param maxReplicas int
param appInsightsConnectionString string
param keyVaultName string
param managedIdentityId string
param managedIdentityClientId string
param commonTags object

var keyVaultUri = 'https://${keyVaultName}.vault.azure.net/secrets'

var secrets = [
  {
    name: 'appinsights-connection-string'
    value: appInsightsConnectionString
  }
  {
    name: 'azure-openai-key'
    keyVaultUrl: '${keyVaultUri}/azure-openai-key'
    identity: managedIdentityId
  }
  {
    name: 'themoviedb-api-key'
    keyVaultUrl: '${keyVaultUri}/themoviedb-api-key'
    identity: managedIdentityId
  }
]

var envVars = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentType == 'prod' ? 'Production' : 'Development'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'appinsights-connection-string'
  }
  {
    name: 'AzureOpenAI__ApiKey'
    secretRef: 'azure-openai-key'
  }
  {
    name: 'TheMovieDb__Api-Key'
    secretRef: 'themoviedb-api-key'
  }
]

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
          allowCredentials: false
          maxAge: 3600
        }
      }
      registries: !empty(containerRegistryServer) ? [
        {
          server: containerRegistryServer
          identity: managedIdentityId
        }
      ] : []
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: containerImage
          resources: {
            cpu: json(containerCpu)
            memory: containerMemory
          }
          env: envVars
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: targetPort
              }
              initialDelaySeconds: 10
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: targetPort
              }
              initialDelaySeconds: 15
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output containerAppId string = containerApp.id
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output containerAppName string = containerApp.name
output containerAppPrincipalId string = managedIdentityClientId
