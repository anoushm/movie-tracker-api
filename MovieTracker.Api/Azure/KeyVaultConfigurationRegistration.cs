using System;
using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace MovieTracker.Api.Azure;

public static class KeyVaultConfigurationRegistration
{
    public static WebApplicationBuilder AddKeyVaultWhenLocal(
        this WebApplicationBuilder builder,
        AzureCredentialFactory factory,
        TokenCredential credential)
    {
        if (factory.IsInAzure)
        {
            return builder;
        }

        string? keyVaultUri = builder.Configuration["KeyVault:Uri"];
        if (string.IsNullOrEmpty(keyVaultUri))
        {
            throw new InvalidOperationException("Missing configuration value: KeyVault:Uri");
        }

        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultUri),
            credential,
            new MovieTrackerKeyVaultSecretManager());

        return builder;
    }
}
