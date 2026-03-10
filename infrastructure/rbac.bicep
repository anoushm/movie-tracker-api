@description('The principal ID of the Container App managed identity')
param containerAppPrincipalId string

@description('The resource ID of the Key Vault')
param keyVaultId string = ''

@description('The principal ID of the user to grant Key Vault Administrator access')
param userPrincipalId string = ''

var keyVaultAdministratorRoleId = '00482a5a-887f-4fb3-b363-3b7fe8e74483'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (!empty(keyVaultId)) {
  name: last(split(keyVaultId, '/'))
}

resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(keyVaultId)) {
  scope: keyVault
  name: guid(keyVaultId, containerAppPrincipalId, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: containerAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultAdministratorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(keyVaultId) && !empty(userPrincipalId)) {
  scope: keyVault
  name: guid(keyVaultId, userPrincipalId, keyVaultAdministratorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultAdministratorRoleId)
    principalId: userPrincipalId
    principalType: 'User'
  }
}
