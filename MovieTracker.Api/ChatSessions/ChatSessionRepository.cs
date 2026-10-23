using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace MovieTracker.Api.ChatSessions;

public class ChatSessionRepository
{
    private readonly Container chatSessionsContainer;
    private readonly Tracer tracer;
    private readonly ILogger<ChatSessionRepository> logger;

    public ChatSessionRepository(CosmosClient cosmosClient, string database, string container, Tracer tracer, ILogger<ChatSessionRepository> logger)
    {
        this.chatSessionsContainer = cosmosClient.GetContainer(database, container);
        this.tracer = tracer;
        this.logger = logger;
    }

    public async Task<MoviceTrackerChatSession> NewChatSession(List<AIChatMessage> chatHistory)
    {
        using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.new-chat-session");
        try
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
        catch (Exception ex)
        {
            this.logger.LogCritical("{@ex}", ex);
            throw;
        }
    }

    public async Task<MoviceTrackerChatSession> GetChatSession(string id)
    {
        using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.get-chat-session");
        try
        {
            ItemResponse<MoviceTrackerChatSession> response =
                await this.chatSessionsContainer.ReadItemAsync<MoviceTrackerChatSession>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (Exception ex)
        {
            this.logger.LogCritical("{@ex}", ex);
            throw;
        }
    }

    public async Task<MoviceTrackerChatSession> SaveChatSession(MoviceTrackerChatSession session)
    {
        using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.save-chat-session");
        try
        {
            ItemResponse<MoviceTrackerChatSession> response =
                await this.chatSessionsContainer.ReplaceItemAsync(session, session.id, new PartitionKey(session.PartitionKey));
            return response.Resource;
        }
        catch (Exception ex)
        {
            this.logger.LogCritical("{@ex}", ex);
            throw;
        }
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
