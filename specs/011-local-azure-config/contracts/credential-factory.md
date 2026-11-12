# Contract: Credential Factory + Key Vault Provider

**Branch**: `011-local-azure-config`

Behavioral contract exposed by the new `MovieTracker.Api.Azure.AzureCredentialFactory` and the Key Vault configuration registration. Consumers: `Program.cs`, `ChatSessionRegistration`, and the eval harness through `WebApplicationFactory<Program>`.

`AzureCredentialFactory` is a small instance class: it takes `IConfiguration` through its constructor (no static members holding state) and is created once in the composition root. It stays alive through the startup section. `Create()` is called exactly once; the returned `TokenCredential` is kept in a local variable in `Program.cs` and passed by reference to both the Cosmos client and the Key Vault registration extension.

---

## `AzureCredentialFactory.IsInAzure`

| Pre-condition | Observable outcome |
|---|---|
| `Environment.GetEnvironmentVariable("CONTAINER_APP_NAME")` is non-null and non-empty | returns `true` |
| otherwise | returns `false` |

## `AzureCredentialFactory.Create()`

| Pre-condition | Observable outcome |
|---|---|
| `IsInAzure == true` and `AZURE_CLIENT_ID` is non-null | returns a `ManagedIdentityCredential` bound to that client id |
| `IsInAzure == true` and `AZURE_CLIENT_ID` is null | returns a `ManagedIdentityCredential` with no client id (deferred failure matches today's behavior) |
| `IsInAzure == false` and the injected `IConfiguration["Azure:TenantId"]` is non-empty | returns an `AzureCliCredential` pinned to that tenant |
| `IsInAzure == false` and the injected `IConfiguration["Azure:TenantId"]` is null or empty | throws `InvalidOperationException("Missing configuration value: Azure:TenantId")` |

Call rate: once per process. The returned credential is shared by the Cosmos client and the Key Vault provider.

## Key Vault configuration provider registration

Extension: `WebApplicationBuilder.AddKeyVaultWhenLocal(AzureCredentialFactory factory, TokenCredential credential)`.

The extension reads `factory.IsInAzure` directly and uses the pre-created `credential` instance. `Create()` is NOT called again inside the extension; the credential is the same one already handed to the Cosmos client.

| Pre-condition | Observable outcome |
|---|---|
| `factory.IsInAzure == true` | no-op |
| `factory.IsInAzure == false` and `builder.Configuration["KeyVault:Uri"]` is non-empty | registers the Azure Key Vault configuration provider against that URI using the supplied `credential`, with a custom `KeyVaultSecretManager` that loads only the three mapped secrets |
| `factory.IsInAzure == false` and `builder.Configuration["KeyVault:Uri"]` is null or empty | throws `InvalidOperationException("Missing configuration value: KeyVault:Uri")` |

Allow-list and mapping (exact):

| Secret name | `IConfiguration` key |
|---|---|
| `azure-openai-key` | `AzureOpenAI:ApiKey` |
| `themoviedb-api-key` | `TheMovieDb:Api-Key` |
| `omdb-api-key` | `OpenMovieDb:Api-Key` |

Any other secret name: `Load` returns `false`; the secret is not fetched.

## Cosmos client registration

| Pre-condition | Observable outcome |
|---|---|
| `AddChatSessions` called with the credential from the factory | `CosmosClient` is constructed with that credential; `DefaultAzureCredential` is no longer used anywhere. |

## Telemetry registration

| Pre-condition | Observable outcome |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` is null or whitespace | `AddTelemetry` registers OpenTelemetry tracing, metrics, and logging without any `AzureMonitor*Exporter` call. |
| Non-empty | Current behavior — all three exporters registered with the connection string. |
