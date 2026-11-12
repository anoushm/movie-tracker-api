# Phase 1 Data Model: Local Azure Config + Manual Eval Pipeline

**Branch**: `011-local-azure-config` | **Date**: 2026-11-12

This slice adds no persistent data entities. It introduces one runtime abstraction and refines a small number of configuration keys. The "entities" here are code-level values and configuration shapes.

---

## Runtime values

### `AzureCredentialFactory`

Small instance class that returns a single `Azure.Core.TokenCredential` for the process. Constructed once in `Program.cs` with the `IConfiguration` from the builder; it stays alive through the startup section so `IsInAzure` is readable by the Key Vault registration extension as well as the Cosmos wiring. Not registered in DI (only used at startup).

| Member | Type | Meaning |
|---|---|---|
| ctor | `AzureCredentialFactory(IConfiguration configuration)` | Captures the configuration reference. |
| `IsInAzure` | `bool` (instance getter) | `true` when `Environment.GetEnvironmentVariable("CONTAINER_APP_NAME")` is non-null and non-empty. |
| `Create()` | `TokenCredential` | Returns `ManagedIdentityCredential(managedIdentityClientId: AZURE_CLIENT_ID)` when `IsInAzure`; otherwise `AzureCliCredential(new AzureCliCredentialOptions { TenantId = configuration["Azure:TenantId"] ?? throw })`. |

Validation:
- If `IsInAzure` is `true` and `AZURE_CLIENT_ID` is missing or empty, the constructor-equivalent error surfaces at the first Cosmos call (unchanged from today). No extra check is added.
- If `IsInAzure` is `false` and `Azure:TenantId` is missing or empty, `Create` throws `InvalidOperationException("Missing configuration value: Azure:TenantId")`.

Lifecycle:
- The factory is constructed once in `Program.cs`; `Create()` is called once; the returned credential is passed to the Cosmos client, the Key Vault configuration provider, and any future identity-aware client. Thread-safe singletons per the Azure SDK docs.

### Key Vault secret → configuration key map

| Key Vault secret name | `IConfiguration` key |
|---|---|
| `azure-openai-key` | `AzureOpenAI:ApiKey` |
| `themoviedb-api-key` | `TheMovieDb:Api-Key` |
| `omdb-api-key` | `OpenMovieDb:Api-Key` |

Provider behavior:
- Secrets outside this list are NOT loaded. The custom `KeyVaultSecretManager` returns `Load=false` for any other secret name and maps each listed name via its `GetKey` override.
- The provider is registered only when `IsInAzure` is `false`.
- Reload: none (the default `ReloadInterval = null` suits a short-lived console run and a cold-started API).

### `TelemetryRegistration` behavior

| Input | Behavior |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` is null or whitespace | Register OpenTelemetry tracer, meter, and logger providers WITHOUT the three `AzureMonitor*Exporter` calls. |
| Non-empty | Unchanged from today — register the three exporters with the connection string. |

---

## Configuration keys (shape)

Only non-secret keys are listed here. Secret keys (`AzureOpenAI:ApiKey`, `TheMovieDb:Api-Key`, `OpenMovieDb:Api-Key`) continue to reach the process through Container App env vars in Azure and through the Key Vault provider locally.

| Key | Required when | Source |
|---|---|---|
| `Azure:TenantId` | `IsInAzure == false` | Local `appsettings.Development.json` or the env var `Azure__TenantId` supplied by the eval workflow. |
| `KeyVault:Uri` | `IsInAzure == false` | Same source as above (`KeyVault__Uri`). |
| `Cosmos:Endpoint` | always | Container App env var `Cosmos__Endpoint` in Azure (existing); local `appsettings.Development.json` or `Cosmos__Endpoint` env var locally. |
| `Cosmos:Database`, `Cosmos:Container` | always | Committed `appsettings.json` already supplies both values; Container App env vars override them in Azure. No additional local configuration is needed for these two keys. |
| `AzureOpenAI:Endpoint`, `AzureOpenAI:DeploymentName` | always | Committed `appsettings.json` (existing) — unchanged. |

---

## Infrastructure parameters

| Parameter | Type | Default | Where used |
|---|---|---|---|
| `githubOidcPrincipalId` | `string` | (none — required in `demo.parameters.json`) | `main.bicep` wires it to two role assignments (see below). |

Two new role assignments in Bicep (declared; no new module):

| Scope | Role | Role definition id |
|---|---|---|
| Key Vault (`movie-tracker-kv`) | Key Vault Secrets User | `4633458b-17de-408a-b874-0445c86b69e6` |
| Cosmos account (`movie-tracker-cosmos`) | Cosmos DB Built-in Data Contributor | `00000000-0000-0000-0000-000000000002` |

The Key Vault assignment is a `Microsoft.Authorization/roleAssignments` with `principalType: 'ServicePrincipal'`. The Cosmos assignment is a `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments` that follows the existing Cosmos pattern (no `principalType` field). Both use `principalId: githubOidcPrincipalId`.

---

## State transitions

None: the slice has no state machine, no workflow data, and no new database entity.
