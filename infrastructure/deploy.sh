#!/bin/bash

# Movie Tracker API - Container App Deployment Script
# Usage: ./deploy.sh <environment> [resource-group]

set -e

ENVIRONMENT=${1:-demo}
RESOURCE_GROUP=${2:-"RG-MovieTracker-Demo"}
LOCATION="westus2"

echo "=========================================="
echo "Movie Tracker API - Container App Deploy"
echo "=========================================="
echo "Environment: $ENVIRONMENT"
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo "=========================================="

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    echo "Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

# Get current subscription
SUBSCRIPTION=$(az account show --query name -o tsv)
echo "Using subscription: $SUBSCRIPTION"
echo ""

# Create resource group if it doesn't exist
echo "Ensuring resource group exists..."
az group create \
    --name "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --tags domain=movie-tracker env=$ENVIRONMENT \
    --output none

# Validate the deployment
echo "Validating Bicep template..."
az deployment group validate \
    --resource-group "$RESOURCE_GROUP" \
    --template-file main.bicep \
    --parameters "parameters/${ENVIRONMENT}.parameters.json" \
    --output none

echo "Validation successful!"
echo ""

# Deploy
echo "Deploying infrastructure..."
az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file main.bicep \
    --parameters "parameters/${ENVIRONMENT}.parameters.json" \
    --name "movie-tracker-deploy-$(date +%Y%m%d-%H%M%S)" \
    --output table

echo ""
echo "=========================================="
echo "Deployment complete!"
echo "=========================================="

# Get outputs
echo ""
echo "Container App URL:"
az deployment group show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$(az deployment group list --resource-group $RESOURCE_GROUP --query '[0].name' -o tsv)" \
    --query properties.outputs.containerAppUrl.value \
    -o tsv
