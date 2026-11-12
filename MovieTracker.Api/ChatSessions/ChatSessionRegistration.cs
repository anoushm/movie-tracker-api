using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace MovieTracker.Api.ChatSessions;

public static class ChatSessionRegistration
{
    public static void AddChatSessions(
        this IServiceCollection services,
        string cosmosEndpoint,
        string cosmosDatabase,
        string cosmosContainer,
        TokenCredential credential)
    {
        services.AddSingleton(sp => new CosmosClient(
            cosmosEndpoint,
            credential,
            new CosmosClientOptions { Serializer = new CosmosSystemTextJsonSerializer() }));

        services.AddSingleton(sp => new ChatSessionRepository(
            sp.GetRequiredService<CosmosClient>(),
            cosmosDatabase,
            cosmosContainer,
            sp.GetRequiredService<Tracer>(),
            sp.GetRequiredService<ILogger<ChatSessionRepository>>()));
    }
}
