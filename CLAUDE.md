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
  Controllers/              # API controllers
  Program.cs                # Application entry point with minimal hosting
  Dockerfile                # Container configuration
infrastructure/             # Azure Bicep IaC templates
  main.bicep                # Main infrastructure template
  deploy.ps1 / deploy.sh    # Deployment scripts
```

## Key Endpoints

- `GET /health` - Health check endpoint
- `GET /health/ready` - Readiness probe endpoint
- `GET /openapi/v1.json` - OpenAPI spec (Development only)

## Azure Resources

- **Resource Group**: `RG-MovieTracker-Demo`
- **Container Registry**: `movietracker.azurecr.io`
- **Container App**: `movie-tracker-api`
- **Region**: West US 2

## Development Notes

- OpenAPI is only exposed in Development environment
- HTTPS redirection is only enabled in Development
- Container exposes ports 8080 (HTTP) and 8081
- Uses system-assigned managed identity for Azure service access
