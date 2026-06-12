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
- Container Apps Environment: `movietracker`
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

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /ask` | Ask the movie assistant a question |
| `GET /health` | Liveness probe |
| `GET /health/ready` | Readiness probe |
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
