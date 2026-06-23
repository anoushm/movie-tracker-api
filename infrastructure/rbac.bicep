@description('The resource ID of the Key Vault')
param keyVaultId string = ''

@description('The principal ID of the user to grant Key Vault Administrator access')
param userPrincipalId string = ''

var keyVaultAdministratorRoleId = '00482a5a-887f-4fb3-b363-3b7fe8e74483'

resource keyVault 'Microsoft.KeyVault/vaults@2026-02-01' existing = if (!empty(keyVaultId)) {
  name: last(split(keyVaultId, '/'))
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
