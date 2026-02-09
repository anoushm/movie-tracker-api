# Movie Tracker API - Container App Deployment Script
# Usage: .\deploy.ps1 -ResourceGroup RG-MovieTracker-Demo -Location westus2

param(
    [string]$ResourceGroup = 'RG-MovieTracker-Demo',
    [string]$Location = 'westus3'
)

Write-Host "Movie Tracker API Deployment"
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Location: $Location"
Write-Host ""

# Check Azure login
az account show >$null 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Not logged in to Azure. Run: az login"
    exit 1
}

# Create resource group
Write-Host "Creating resource group..."
az group create --name $ResourceGroup --location $Location --output none

# Deploy
Write-Host "Deploying infrastructure..."
az deployment group create `
    --resource-group $ResourceGroup `
    --template-file main.bicep `
    --parameters demo.parameters.json `
    --output table

# Show URL
Write-Host ""
Write-Host "Deployment complete!"
Write-Host ""
az containerapp show --name movie-tracker-api --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv | ForEach-Object { Write-Host "URL: https://$_" }
