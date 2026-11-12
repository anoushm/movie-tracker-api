using System.Collections.Generic;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace MovieTracker.Api.Azure;

public sealed class MovieTrackerKeyVaultSecretManager : KeyVaultSecretManager
{
    private static readonly IReadOnlyDictionary<string, string> secretKeyMap =
        new Dictionary<string, string>
        {
            ["azure-openai-key"] = "AzureOpenAI:ApiKey",
            ["themoviedb-api-key"] = "TheMovieDb:Api-Key",
            ["omdb-api-key"] = "OpenMovieDb:Api-Key",
        };

    public override bool Load(SecretProperties secret)
    {
        return secretKeyMap.ContainsKey(secret.Name);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        return secretKeyMap[secret.Name];
    }
}
