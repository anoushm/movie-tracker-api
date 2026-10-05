# Movie Tracker API

A .NET 10 movie-assistant API using Azure OpenAI and TheMovieDB. Deployed to Azure Container Apps via Bicep.

## Prerequisites

### 1. Key Vault Secrets (Required Before Deployment)

The Azure Container App pulls secrets from Key Vault at deployment time. **These secrets must exist in Key Vault BEFORE deploying the infrastructure**, otherwise the Container App deployment will fail.

| Secret Name | Description | How to Obtain |
|-------------|-------------|---------------|
| `azure-openai-key` | Azure OpenAI API key | Azure Portal → Cognitive Services → Keys |
| `themoviedb-api-key` | TheMovieDB API key | https://www.themoviedb.org/settings/api |

#### Setup Commands

```bash
# Set Azure OpenAI key
az keyvault secret set --vault-name movie-tracker-kv --name azure-openai-key --value "<your-azure-openai-key>"

# Set TheMovieDB key
az keyvault secret set --vault-name movie-tracker-kv --name themoviedb-api-key --value "<your-themoviedb-key>"
```

#### Verify Secrets Exist

```bash
az keyvault secret list --vault-name movie-tracker-kv --query "[].name" -o tsv
```

Expected output:
```
azure-openai-key
themoviedb-api-key
```

> ⚠️ **Important**: If you deploy the Container App before these secrets exist, the deployment will fail with a Key Vault reference error. Always run the setup commands above first.

### 2. Azure Resources

The following resources are provisioned by the Bicep templates:

- Resource Group: `RG-MovieTracker-Demo`
- Container Registry: `movietracker.azurecr.io`
- Container Apps Environment: `movie-tracker-env`
- Container App: `movie-tracker-api`
- Key Vault: `movie-tracker-kv`
- Azure OpenAI: `movie-tracker-openai` (in `Rg-Movie-Tracker`)

## Deployment

### First-Time Setup

1. **Add secrets to Key Vault** (see Prerequisites above)

2. **Deploy infrastructure:**
   ```bash
   cd infrastructure
   az deployment group create \
     --resource-group RG-MovieTracker-Demo \
     --template-file main.bicep \
     --parameters demo.parameters.json
   ```

3. **Build and push container image:**
   ```bash
   az acr build --registry movietracker --image movie-tracker-api:latest .
   ```

### Subsequent Deployments

CI/CD via GitHub Actions handles deployments automatically on push to `main`.

## Secret Rotation

To rotate secrets:

1. Update the secret in Key Vault:
   ```bash
   az keyvault secret set --vault-name movie-tracker-kv --name azure-openai-key --value "<new-key>"
   ```

2. Restart the Container App to pull new secret:
   ```bash
   az containerapp revision restart -n movie-tracker-api -g RG-MovieTracker-Demo --revision <revision-name>
   ```

   Or create a new revision:
   ```bash
   az containerapp update -n movie-tracker-api -g RG-MovieTracker-Demo
   ```

## Local Development

Use .NET User Secrets (secrets never in source):

```bash
cd MovieTracker.Api
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-key>"
dotnet user-secrets set "TheMovieDb:Api-Key" "<your-key>"
```

## Cosmos DB Local Development

The API talks to the deployed Cosmos account from your laptop over the public endpoint, authenticating as *you* via `az login`. No connection string is ever used; local and deployed runs read from the same account.

### Deployed account

- Account: `movie-tracker-cosmos`
- Resource group: `RG-MovieTracker-Demo`
- Region: `westus3`
- Database: `database`
- Container: `chat-sessions`

### Configuration keys

The API reads three non-secret keys:

| Key | Local value | Where it comes from in Azure |
|-----|-------------|------------------------------|
| `Cosmos:Endpoint` | `https://movie-tracker-cosmos.documents.azure.com:443/` | `Cosmos__Endpoint` env var on the Container App |
| `Cosmos:Database` | `database` | `appsettings.json` (also `Cosmos__Database` on the Container App) |
| `Cosmos:Container` | `chat-sessions` | `appsettings.json` (also `Cosmos__Container` on the Container App) |

Only `Cosmos:Endpoint` needs to be supplied locally — set it via user-secrets or an environment variable:

```powershell
cd MovieTracker.Api
dotnet user-secrets set "Cosmos:Endpoint" "https://movie-tracker-cosmos.documents.azure.com:443/"
```

Or in the current shell:

```powershell
$env:Cosmos__Endpoint = "https://movie-tracker-cosmos.documents.azure.com:443/"
```

Startup fails fast if any of the three keys is missing.

### `AZURE_CLIENT_ID` must be UNSET locally

The Container App sets `AZURE_CLIENT_ID` so `DefaultAzureCredential` picks the user-assigned managed identity. Locally that same variable makes the credential chain try to authenticate as the UAMI from your laptop and fail. Make sure it is unset in whatever shell you run `dotnet run` from so the chain falls through to `AzureCliCredential`:

```powershell
Remove-Item Env:AZURE_CLIENT_ID -ErrorAction SilentlyContinue
```

### One-time data-plane role grant (per developer)

The account has `disableLocalAuth: true`, so you need the built-in **Cosmos DB Built-in Data Contributor** role (`00000000-0000-0000-0000-000000000002`) on your own user principal before the probe will succeed. Run once:

```powershell
az cosmosdb sql role assignment create --account-name movie-tracker-cosmos --resource-group RG-MovieTracker-Demo --scope "/" --principal-id (az ad signed-in-user show --query id -o tsv) --role-definition-id 00000000-0000-0000-0000-000000000002
```

### Verify

```powershell
az login
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
# then, from a second terminal, against the port shown in the console (e.g. 5000):
curl http://localhost:5000/health/cosmos
```

Expected body:

```json
{"status":"ok","account":"movie-tracker-cosmos.documents.azure.com","database":"database","container":"chat-sessions"}
```

A 503 with `reason: "authorization"` immediately after granting the role is expected — Cosmos data-plane role propagation can take up to a minute. Retry.

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /ask` | Ask the movie assistant a question |
| `GET /health` | Liveness probe |
| `GET /health/ready` | Readiness probe |
| `GET /health/cosmos` | Cosmos DB connectivity probe (real read round trip via managed identity) |
| `GET /version` | Build info and environment |

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    Azure Container Apps                       │
│  ┌─────────────────┐    ┌─────────────────┐                  │
│  │ movie-tracker-  │───▶│   Key Vault     │                  │
│  │      api        │    │ (secrets pull)  │                  │
│  └────────┬────────┘    └─────────────────┘                  │
│           │                                                   │
└───────────┼───────────────────────────────────────────────────┘
            │
            ▼
   ┌────────────────┐         ┌─────────────────┐
   │  Azure OpenAI  │         │   TheMovieDB    │
   │   (gpt-4o)     │         │      API        │
   └────────────────┘         └─────────────────┘
```
