param location string = resourceGroup().location
param accountName string
param principalId string

var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    minimalTlsVersion: 'Tls12'
    enableFreeTier: false
    disableLocalAuth: true
    capabilities: [
      {
        name: 'EnableServerless'
      }
      {
        name: 'EnableNoSQLVectorSearch'
      }
    ]
    capacity: {
      totalThroughputLimit: 4000
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    isVirtualNetworkFilterEnabled: false
    virtualNetworkRules: []
    ipRules: []
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: 'database'
  properties: {
    resource: {
      id: 'database'
    }
  }
}

resource chatSessionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: cosmosDatabase
  name: 'chat-sessions'
  properties: {
    resource: {
      id: 'chat-sessions'
      partitionKey: {
        kind: 'Hash'
        paths: [
          '/PartitionKey'
        ]
      }
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
  }
}

resource dataContributorAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, dataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    principalId: principalId
    scope: cosmosAccount.id
  }
}

output accountName string = accountName
output endpoint string = cosmosAccount.properties.documentEndpoint
output databaseName string = 'database'
output containerName string = 'chat-sessions'
