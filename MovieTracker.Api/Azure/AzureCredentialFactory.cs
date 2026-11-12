using System;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace MovieTracker.Api.Azure;

public sealed class AzureCredentialFactory
{
    private readonly IConfiguration configuration;

    public AzureCredentialFactory(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public bool IsInAzure
    {
        get
        {
            string? containerAppName = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME");
            return !string.IsNullOrEmpty(containerAppName);
        }
    }

    public TokenCredential Create()
    {
        if (this.IsInAzure)
        {
            string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            if (string.IsNullOrEmpty(clientId))
            {
                return new ManagedIdentityCredential();
            }

            return new ManagedIdentityCredential(clientId);
        }

        string? tenantId = this.configuration["Azure:TenantId"];
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException("Missing configuration value: Azure:TenantId");
        }

        return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });
    }
}
