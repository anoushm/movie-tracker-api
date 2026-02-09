# Movie Tracker API

A .NET 10 Web API for tracking movies, built with ASP.NET Core and containerized for deployment to Azure Container Apps.

## ?? Features

- Built on .NET 10
- OpenAPI/Swagger documentation
- Docker containerization support
- Azure Container Registry (ACR) integration
- Azure Container Apps deployment ready

## ?? Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- Docker (for local container testing)
- Azure subscription with access to:
  - Azure Container Registry (ACR)
  - Azure Container Apps

## ??? Project Structure

```
MovieTracker.Api/
??? Controllers/
?   ??? WeatherForecastController.cs
??? Program.cs
??? WeatherForecast.cs
??? MovieTracker.Api.csproj
```

## ??? Local Development

### Run the API locally

```bash
cd MovieTracker.Api
dotnet restore
dotnet build
dotnet run
```

The API will be available at `https://localhost:5001` (or the port specified in your launch settings).

### View API Documentation

When running in Development mode, OpenAPI documentation is available at:
- `/openapi/v1.json` - OpenAPI specification

## ?? Container Configuration

The project uses a multi-stage Dockerfile for containerization:

- **Base Image**: `mcr.microsoft.com/dotnet/aspnet:10.0`
- **Build Image**: `mcr.microsoft.com/dotnet/sdk:10.0`
- **Container Registry**: `movietracker.azurecr.io`
- **Repository Name**: `movie-tracker-api`
- **Container Ports**: 8080, 8081

### Local Docker Build

To build and run the container locally:

```bash
# Build the image
docker build -t movie-tracker-api:local .

# Run the container
docker run -p 8080:8080 -p 8081:8081 movie-tracker-api:local
```

## ?? Build, Push, and Deploy to Azure

### Step 1: Login to Azure

```bash
az login
```

### Step 2: Login to Azure Container Registry

```bash
az acr login --name movietracker
```

### Step 3: Build and Push Container Image to ACR

**Option A: Build locally and push**

```bash
# Build the Docker image
docker build -t movietracker.azurecr.io/movie-tracker-api:latest .

# Push the image to ACR
docker push movietracker.azurecr.io/movie-tracker-api:latest
```

**Option B: Build directly in ACR (recommended)**

```bash
az acr build --registry movietracker --image movie-tracker-api:latest .
```

This command will:
- Build the Docker image using the Dockerfile in Azure
- Push the image to your Azure Container Registry
- All processing happens in Azure (no local Docker daemon required)

**Option C: Build locally, push to ACR, and update container app (all steps)**

```bash
# Build the Docker image locally
docker build -t movietracker.azurecr.io/movie-tracker-api:latest .

# Push the image to ACR
docker push movietracker.azurecr.io/movie-tracker-api:latest

# Update the container app with the new image
az containerapp update --name movie-tracker-api --resource-group RG-MovieTracker-Demo --image movietracker.azurecr.io/movie-tracker-api:latest
```

### Step 4: Update Azure Container App

```bash
az containerapp update --name movie-tracker-api --resource-group RG-MovieTracker-Demo --image movietracker.azurecr.io/movie-tracker-api:latest
```

### Step 5: Verify Deployment (Optional)

Get the FQDN of your deployed container app:

```bash
az containerapp show --name movie-tracker-api --resource-group RG-MovieTracker-Demo --query properties.configuration.ingress.fqdn -o tsv
```

## ?? Configuration

### User Secrets

The project uses User Secrets for local development. Secret ID: `f4a3ca8a-b4ee-4b8f-b8dc-b96b2918e8a6`

To manage user secrets:

```bash
dotnet user-secrets set "SecretKey" "SecretValue"
dotnet user-secrets list
```

## ?? Azure Resources

This project is configured to work with the following Azure resources:

- **Resource Group**: `RG-MovieTracker-Demo`
- **Container Registry**: `movietracker.azurecr.io`
- **Container App**: `movie-tracker-api`

## ?? Notes

- The container base image uses the official Microsoft .NET 10 ASP.NET runtime
- HTTPS redirection is enabled by default
- The API uses minimal hosting model introduced in .NET 6+
- Container debugging is enabled for development

## ?? Contributing

1. Create a feature branch
2. Make your changes
3. Test locally
4. Build and test the container image
5. Submit a pull request

## ?? License

[Specify your license here]

## ?? Additional Resources

- [.NET 10 Documentation](https://docs.microsoft.com/dotnet/)
- [Azure Container Apps Documentation](https://docs.microsoft.com/azure/container-apps/)
- [Azure Container Registry Documentation](https://docs.microsoft.com/azure/container-registry/)
