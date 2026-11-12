# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Movie Tracker API is a .NET 10 Web API for tracking movies, built with ASP.NET Core and containerized for deployment to Azure Container Apps. The project is designed to integrate with the Microsoft Agent Framework.

## Build and Run Commands

```bash
# Restore dependencies
dotnet restore MovieTracker.Api/MovieTracker.Api.csproj

# Build the project
dotnet build MovieTracker.Api/MovieTracker.Api.csproj

# Run locally (Development mode)
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj

# Run tests (when available)
dotnet test

# Build Docker image locally
docker build -t movie-tracker-api:local -f MovieTracker.Api/Dockerfile MovieTracker.Api/

# Build and push to Azure Container Registry
az acr build --registry movietracker --image movie-tracker-api:latest MovieTracker.Api/
```

## Architecture

- **Framework**: .NET 10, ASP.NET Core Web API
- **Container**: Multi-stage Dockerfile targeting Linux
- **Deployment**: Azure Container Apps with Azure Container Registry
- **Observability**: Application Insights integration

### Project Structure

```
MovieTracker.Api/           # Main API project
  Program.cs                # Composition root (minimal hosting)
  HealthEndpoints.cs        # MapHealthEndpoints extension
  Agents/                   # MovieAgent (AIAgent registration)
  Tools/                    # TmdbTools, OmdbTools, TrailerTools, WikipediaTools, DateTimeTools, ChatPlanner, MovieTools (DI registration)
  Chat/                     # ChatTurnEndpoints (AskAsync, AskV2Async, MapChatEndpoints)
  Models/                   # Every DTO, record, and view model
  ChatSessions/             # ChatSessionRepository, MovieTrackerChatSession, ChatStartSystemPrompt, CosmosSystemTextJsonSerializer, ChatSessionRegistration
  Telemetry/                # InstrumentedAIFunction, ModelPricing, Telemetry, TelemetryRegistration
  Dockerfile                # Container configuration
infrastructure/             # Azure Bicep IaC templates
  main.bicep                # Main infrastructure template
```

## Key Endpoints

- `GET /health` - Health check endpoint
- `GET /health/ready` - Readiness probe endpoint
- `GET /health/cosmos` - Cosmos DB connectivity probe (real read round trip via managed identity)
- `GET /openapi/v1.json` - OpenAPI spec (Development only)

## Azure Resources

- **Resource Group**: `RG-MovieTracker-Demo`
- **Container Registry**: `movietracker.azurecr.io`
- **Container App**: `movie-tracker-api`
- **Cosmos DB Account**: `movie-tracker-cosmos` (serverless, NoSQL vector search, westus3)
- **Region**: westus3

## Development Notes

- OpenAPI is only exposed in Development environment
- HTTPS redirection is only enabled in Development
- Container exposes ports 8080 (HTTP) and 8081
- Uses system-assigned managed identity for Azure service access
- Cosmos configuration keys: `Cosmos:Endpoint`, `Cosmos:Database`, `Cosmos:Container` (env-var equivalents `Cosmos__Endpoint`, `Cosmos__Database`, `Cosmos__Container`). Startup fails fast if any is missing.
- Local run is `az login` + `dotnet run`. Secrets come from Key Vault via `AzureCliCredential`; non-secret `Azure:TenantId`, `KeyVault:Uri`, and `Cosmos:Endpoint` live in the gitignored `MovieTracker.Api/appsettings.Development.json`. That file is only loaded when the Development ASP.NET Core environment is active — ensured by either a local `MovieTracker.Api/Properties/launchSettings.json` or `$env:ASPNETCORE_ENVIRONMENT = "Development"`. `AZURE_CLIENT_ID` and `AZURE_TOKEN_CREDENTIALS` are no longer meaningful locally — the `CONTAINER_APP_NAME` env var is the only signal for "running in Azure".
