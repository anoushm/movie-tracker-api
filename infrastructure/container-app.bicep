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
param azureOpenAIEndpoint string
param azureOpenAIDeployment string
@secure()
param azureOpenAIKey string
param commonTags object

var baseSecrets = [
  {
    name: 'appinsights-connection-string'
    value: appInsightsConnectionString
  }
]

var openAISecret = !empty(azureOpenAIKey) ? [
  {
    name: 'azure-openai-key'
    value: azureOpenAIKey
  }
] : []

var allSecrets = concat(baseSecrets, openAISecret)

var baseEnvVars = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentType == 'prod' ? 'Production' : 'Development'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'appinsights-connection-string'
  }
  {
    name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
    value: ''
  }
]

var openAIEnvVars = !empty(azureOpenAIEndpoint) ? [
  {
    name: 'AZURE_OPENAI_ENDPOINT'
    value: azureOpenAIEndpoint
  }
  {
    name: 'AZURE_OPENAI_DEPLOYMENT'
    value: azureOpenAIDeployment
  }
  {
    name: 'AZURE_OPENAI_API_KEY'
    secretRef: 'azure-openai-key'
  }
] : []

var allEnvVars = concat(baseEnvVars, openAIEnvVars)

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: commonTags
  identity: {
    type: 'SystemAssigned'
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
          identity: 'system'
        }
      ] : []
      secrets: allSecrets
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
          env: allEnvVars
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
output containerAppPrincipalId string = containerApp.identity.principalId
