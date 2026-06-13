# Movie Tracker API - Container App Infrastructure

Infrastructure as Code (IaC) for deploying the Movie Tracker API using Azure Container Apps. This project is migrating from Microsoft Semantic Kernel to **Microsoft Agent Framework**.

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                    Azure Resource Group                         │
│                   RG-MovieTracker-Demo                          │
│  ┌────────────────────────────────────────────────────────────┐│
│  │              Log Analytics Workspace                        ││
│  │              (movie-tracker-law)                            ││
│  └────────────────────────────────────────────────────────────┘│
│                   │                    │                        │
│                   ▼                    ▼                        │
│  ┌─────────────────────────┐  ┌──────────────────────────────┐│
│  │  Application Insights   │  │ Container Apps Environment   ││
│  │ (movie-tracker-ai)      │  │   (movie-tracker-env)        ││
│  └─────────────────────────┘  │  ┌──────────────────────────┐││
│                                │  │   Container App          │││
│  ┌─────────────────────────┐  │  │  (movie-tracker-api)     │││
│  │    Azure Key Vault      │  │  │  System-Assigned Identity│││
│  │  (movie-tracker-kv)     │  │  │  - CPU: 0.5 cores        │││
│  │  - RBAC Enabled         │◄─┼──┤  - Memory: 1 Gi          │││
│  │  - Secrets User Access  │  │  │  - Port: 8080            │││
│  └─────────────────────────┘  │  └──────────────────────────┘││
│  ┌────────────────────────────────────────────────────────────┐│
│  │       Azure Container Registry (ACR)                        ││
│  │       (movietracker)                                        ││
│  │       - AcrPull Role for Container App                      ││
│  └────────────────────────────────────────────────────────────┘│
│  ┌────────────────────────────────────────────────────────────┐│
│  │ RBAC: Container App Identity →                             │
│  │  - Application Insights: Metrics Publisher, Reader         │
│  │  - Key Vault: Secrets User                                 │
│  │  - ACR: AcrPull                                            │
│  │                                                            │
│  │ RBAC: User Identity → Key Vault Administrator              │
│  └────────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────┘
```

## 📁 File Structure

```
movie-tracker-infra/
├── main.bicep                          # Main Bicep template with full App Insights integration
├── log-analytics.bicep                 # Log Analytics Workspace module
├── app-insights.bicep                  # Application Insights module
├── container-app-environment.bicep     # Container Apps Environment module
├── container-app.bicep                 # Container App module with system-assigned identity
├── acr.bicep                           # Azure Container Registry module
├── acr-pull-role.bicep                 # ACR pull role assignment for container app
├── key-vault.bicep                     # Azure Key Vault module
├── rbac.bicep                          # RBAC role assignments for managed identity
├── demo.bicepparam                     # Native Bicep parameters
├── demo.parameters.json                # JSON parameters for demo
└── README.md
```

## 🚀 Quick Start

### Prerequisites

- Azure CLI installed and logged in (`az login`)
- Azure subscription with Container Apps enabled
- Bicep CLI (comes with Azure CLI)

### Deploy via Azure CLI

```bash
# Create resource group
az group create --name RG-MovieTracker-Demo --location westus3

# Deploy infrastructure
az deployment group create --name local-provision --resource-group RG-MovieTracker-Demo --template-file main.bicep --parameters demo.parameters.json

# Depploy to temp rg (for now)
az deployment group create --name local-provision --resource-group RG-MovieTracker-Demo2 --template-file dummystoragedemo3.bice
```

### Using Native Bicep Parameters

```bash
az deployment group create \
  --resource-group RG-MovieTracker-Demo \
  --template-file main.bicep \
  --parameters parameters/demo.bicepparam
```

## ⚙️ Configuration

### Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `location` | Azure region | `resourceGroup().location` |
| `containerAppName` | Name of the Container App | `movie-tracker-api` |
| `environmentName` | Container Apps Environment name | `movie-tracker-env` |
| `logAnalyticsName` | Log Analytics Workspace name | `movie-tracker-law` |
| `appInsightsName` | Application Insights resource name | `movie-tracker-ai` |
| `keyVaultName` | Azure Key Vault name | `movie-tracker-kv` |
| `userPrincipalId` | User principal ID for Key Vault Administrator access | `` (required) |
| `environmentType` | Environment type (demo/dev/staging/prod) | `demo` |
| `acrName` | Azure Container Registry name | `movietracker` |
| `containerImage` | Container image to deploy | `mcr.microsoft.com/k8se/quickstart:latest` |
| `containerCpu` | CPU cores | `0.5` |
| `containerMemory` | Memory allocation | `1Gi` |
| `targetPort` | Container target port | `8080` |
| `externalIngress` | Enable external ingress | `true` |
| `minReplicas` | Minimum replicas (0 = scale to zero) | `0` |
| `maxReplicas` | Maximum replicas | `5` |
| `containerRegistryServer` | Container registry server | `` (empty for public) |
| `containerRegistryUsername` | Container registry username | `` |
| `containerRegistryPassword` | Container registry password | `` (secure) |
| `azureOpenAIEndpoint` | Azure OpenAI endpoint URL | `` (optional) |
| `azureOpenAIDeployment` | Azure OpenAI deployment name | `` (optional) |
| `azureOpenAIKey` | Azure OpenAI API key | `` (secure, optional) |

### Security & Managed Identity

- **System-Assigned Identity**: Container App has a system-assigned managed identity enabled
- **RBAC Roles**: The managed identity is granted:
  - **Monitoring Metrics Publisher** on Application Insights
  - **Monitoring Reader** on Application Insights
  - **Key Vault Secrets User** on Azure Key Vault (read secrets only)
  - **AcrPull** on Azure Container Registry (pull container images)
- **Azure Key Vault**: Secure secrets management with RBAC authorization
  - **User Access**: Key Vault Administrator role for deployment user
  - **Container App Access**: Secrets User role for reading secrets
  - **Environment-specific settings**: Premium SKU for prod, Standard for demo/dev
- **Application Insights**: Integrated for telemetry and agent framework observability

## 🔄 CI/CD with GitHub Actions

### First-Deploy Bootstrap

On a clean resource group, `infra-deploy` cannot read an existing Container App image and the `containerImage` parameter is no longer in `demo.parameters.json`, so the Bicep default (`mcr.microsoft.com/k8se/quickstart:latest`) is used. The Container App will come up on the quickstart image. To land on the API image, run the `Build and Push API Image` workflow (or push a change under `MovieTracker.Api/**`) immediately after the first infra deploy — it builds the image, pushes it to ACR, and updates the Container App to the real tag. Subsequent infra deploys read the live image from the Container App and preserve it.

The two workflows share a GitHub Actions `concurrency` group (`movie-tracker-aca-deploy`) so they serialize and never race on the Container App's image field.

### Setup

1. Create a Service Principal:
   ```bash
   az ad sp create-for-rbac --name "movie-tracker-github" \
     --role contributor \
     --scopes /subscriptions/{subscription-id}/resourceGroups/RG-MovieTracker-Demo \
     --sdk-auth
   ```

2. Configure GitHub Secrets:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`

3. For federated credentials (recommended):
   ```bash
   az ad app federated-credential create \
     --id {app-id} \
     --parameters '{"name":"github-main","issuer":"https://token.actions.githubusercontent.com","subject":"repo:{org}/{repo}:ref:refs/heads/main","audiences":["api://AzureADTokenExchange"]}'
   ```

### Workflow Triggers

- **Push to main**: Validates and deploys
- **Pull Request**: Validates and runs what-if analysis
- **Manual**: Dispatch with environment selection

## 🎯 Microsoft Agent Framework Integration

This infrastructure is designed for the Microsoft Agent Framework. Key considerations:

1. **Health Endpoints**: Configure `/health` and `/health/ready` probes
2. **Scaling**: HTTP-based autoscaling with concurrent request limits
3. **Observability**: Application Insights integration for agent telemetry
4. **Secrets**: Secure handling of API keys for OpenAI/Azure OpenAI

### Sample Agent Configuration

```csharp
// In your .NET application
builder.Services.AddAgentFramework(options =>
{
    options.UseAzureOpenAI(
        endpoint: builder.Configuration["AZURE_OPENAI_ENDPOINT"],
        deploymentName: builder.Configuration["AZURE_OPENAI_DEPLOYMENT"],
        apiKey: builder.Configuration["AZURE_OPENAI_API_KEY"]
    );
});
```

## 📊 Outputs

After deployment, the following outputs are available:

| Output | Description |
|--------|-------------|
| `containerAppFqdn` | Fully qualified domain name of the Container App |
| `containerAppUrl` | Full HTTPS URL of the Container App |
| `environmentId` | Container Apps Environment resource ID |
| `logAnalyticsWorkspaceId` | Log Analytics Workspace resource ID |
| `appInsightsConnectionString` | Application Insights connection string |
| `appInsightsInstrumentationKey` | Application Insights instrumentation key |
| `containerAppId` | Container App resource ID |
| `containerAppName` | Container App resource name |
| `acrLoginServer` | Azure Container Registry login server |
| `acrRegistryId` | Azure Container Registry resource ID |
| `keyVaultUri` | Azure Key Vault URI |
| `keyVaultName` | Azure Key Vault resource name |
| `keyVaultId` | Azure Key Vault resource ID |

## 🔧 Troubleshooting

### View Logs

```bash
az containerapp logs show \
  --name movie-tracker-api \
  --resource-group RG-MovieTracker-Demo \
  --follow
```

### Check Deployment Status

```bash
az containerapp show \
  --name movie-tracker-api \
  --resource-group RG-MovieTracker-Demo \
  --query "properties.latestRevisionFqdn" -o tsv
```

### Restart Container App

```bash
az containerapp revision restart \
  --name movie-tracker-api \
  --resource-group RG-MovieTracker-Demo \
  --revision $(az containerapp revision list --name movie-tracker-api --resource-group RG-MovieTracker-Demo --query "[0].name" -o tsv)
```

## 📝 License

MIT
