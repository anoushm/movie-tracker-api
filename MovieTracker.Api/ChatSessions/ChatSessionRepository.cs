using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;

namespace MovieTracker.Api.ChatSessions;

public class ChatSessionRepository
{
    private readonly Container chatSessionsContainer;

    public ChatSessionRepository(CosmosClient cosmosClient, string database, string container)
    {
        this.chatSessionsContainer = cosmosClient.GetContainer(database, container);
    }

    public async Task<MoviceTrackerChatSession> NewChatSession(List<ChatMessage> chatHistory)
    {
        string id = GenId();
        MoviceTrackerChatSession session = new MoviceTrackerChatSession
        {
            id = id,
            PartitionKey = id,
            ChatHistory = chatHistory,
            FunnyFact = null
        };
        await this.chatSessionsContainer.CreateItemAsync(session, new PartitionKey(id));
        return session;
    }

    private static string GenId()
    {
        string id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(5))
            .Replace('/', '~')
            .Replace('+', '-')
            .Replace("=", "");
        if (id.Contains('-') || id.Contains('~'))
        {
            return GenId();
        }
        return id;
    }
}
