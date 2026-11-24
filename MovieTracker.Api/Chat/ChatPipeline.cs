using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.ChatSessions;
using MovieTracker.Api.Models;
using MovieTracker.Api.Telemetry;
using MovieTracker.Api.Tools;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ClientChatMessage = MovieTracker.Api.Models.ChatMessage;
using TelemetryHelpers = MovieTracker.Api.Telemetry.Telemetry;

namespace MovieTracker.Api.Chat;

public sealed class ChatPipeline : IChatPipeline
{
    private const int HydratedMovieLimit = 150;

    private readonly Lazy<ChatResponseFormat> lazyMovieListResponseFormat;

    private readonly ChatSessionRepository repo;
    private readonly Lazy<AIAgent> lazyAgent;
    private readonly Lazy<ChatPlanner> lazyChatPlanner;
    private readonly Lazy<MovieHydrator> lazyMovieHydrator;
    private readonly ChatContextReducer contextReducer;
    private readonly UsageLogger usageLogger;
    private readonly ILogger<ChatPipeline> logger;
    private readonly string deployedModel;

    public ChatPipeline(
        ChatSessionRepository repo,
        Lazy<AIAgent> lazyAgent,
        Lazy<ChatPlanner> lazyChatPlanner,
        Lazy<MovieHydrator> lazyMovieHydrator,
        ChatContextReducer contextReducer,
        UsageLogger usageLogger,
        IConfiguration configuration,
        ILogger<ChatPipeline> logger)
    {
        this.repo = repo;
        this.lazyAgent = lazyAgent;
        this.lazyChatPlanner = lazyChatPlanner;
        this.lazyMovieHydrator = lazyMovieHydrator;
        this.contextReducer = contextReducer;
        this.usageLogger = usageLogger;
        this.logger = logger;
        this.deployedModel = configuration["AzureOpenAI:DeploymentName"]!;

        this.lazyMovieListResponseFormat = new Lazy<ChatResponseFormat>(() =>
        {
            JsonSerializerOptions structuredOutputJsonOptions =
                new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { PropertyNamingPolicy = null };

            return ChatResponseFormat.ForJsonSchema(
                AIJsonUtilities.CreateJsonSchema(
                    typeof(MovieListResponse),
                    serializerOptions: structuredOutputJsonOptions,
                    inferenceOptions: new AIJsonSchemaCreateOptions
                    {
                        TransformOptions = new AIJsonSchemaTransformOptions
                        {
                            DisallowAdditionalProperties = true,
                            RequireAllProperties = true,
                        }
                    }),
                nameof(MovieListResponse));
        });
    }

    public async Task<IChatPipeline.StartOutcome> StartSessionAsync()
    {
        List<AIChatMessage> initialHistory = new List<AIChatMessage>
        {
            new AIChatMessage(ChatRole.System, ChatStartSystemPrompt.Text)
        };
        MovieTrackerChatSession session = await this.repo.NewChatSessionAsync(initialHistory);
        return new IChatPipeline.StartOutcome(session.id);
    }

    public Task<IChatPipeline.AskOutcome> AskAsync(string chatId, string input)
    {
        return this.RunPipelineAsync<IChatPipeline.AskOutcome>(
            chatId,
            input,
            () => new IChatPipeline.AskOutcome.NotFound(),
            async (session, sessionCache, newAssistantMessage) =>
            {
                List<ClientChatMessage> responseMessages = new List<ClientChatMessage>();
                foreach (AIChatMessage message in session.ChatHistory)
                {
                    ClientChatMessage? clientMessage = await this.lazyMovieHydrator.Value.ToClientMessageAsync(message, sessionCache);
                    if (clientMessage != null)
                    {
                        responseMessages.Add(clientMessage);
                    }
                }
                return new IChatPipeline.AskOutcome.Ok(session.FunnyFact, responseMessages);
            });
    }

    public Task<IChatPipeline.AskV2Outcome> AskV2Async(string chatId, string input)
    {
        return this.RunPipelineAsync<IChatPipeline.AskV2Outcome>(
            chatId,
            input,
            () => new IChatPipeline.AskV2Outcome.NotFound(),
            async (session, sessionCache, newAssistantMessage) =>
            {
                ClientChatMessage turn;
                if (newAssistantMessage == null)
                {
                    turn = new AssistantChatMessage("No movies were found", new List<MovieViewModel>());
                }
                else
                {
                    ClientChatMessage? hydrated = await this.lazyMovieHydrator.Value.ToClientMessageAsync(newAssistantMessage, sessionCache);
                    turn = hydrated ?? new AssistantChatMessage("No movies were found", new List<MovieViewModel>());
                }
                return new IChatPipeline.AskV2Outcome.Ok(session.FunnyFact, turn);
            });
    }

    private async Task<T> RunPipelineAsync<T>(
        string chatId,
        string input,
        Func<T> notFound,
        Func<MovieTrackerChatSession, ConcurrentDictionary<string, MovieViewModel>, AIChatMessage?, Task<T>> hydrateAndProject)
    {
        MovieTrackerChatSession? chatSession = await this.TryLoadSessionAsync(chatId);
        if (chatSession is null)
        {
            return notFound();
        }

        ConcurrentDictionary<string, MovieViewModel> sessionCache = this.lazyMovieHydrator.Value.LoadHydrationCache(chatSession);
        AIChatMessage? newAssistantMessage = await this.RunTurnAsync(chatSession, input);

        T outcome = await hydrateAndProject(chatSession, sessionCache, newAssistantMessage);

        await this.SaveSessionAsync(chatSession, sessionCache);

        return outcome;
    }

    private async Task<MovieTrackerChatSession?> TryLoadSessionAsync(string chatId)
    {
        try
        {
            return await this.repo.GetChatSessionAsync(chatId);
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<AIChatMessage?> RunTurnAsync(MovieTrackerChatSession chatSession, string input)
    {
        List<AIChatMessage> chatMessages = chatSession.ChatHistory;

        Task<string?> funnyFactTask = this.SafeGenerateFunnyFactAsync(input);

        chatMessages.Add(new AIChatMessage(ChatRole.User, input));

        ChatOptions chatOptions = new ChatOptions
        {
            ResponseFormat = this.lazyMovieListResponseFormat.Value,
        };

        IEnumerable<AIChatMessage> reduced = await this.contextReducer.ReduceAsync([.. chatMessages], default);
        List<AIChatMessage> modelMessages = reduced.ToList();

        if (chatMessages.Count > 0)
        {
            TelemetryHelpers.ContextRetainedRatio.Record((double)modelMessages.Count / chatMessages.Count);
        }

        this.logger.LogInformation(
            "Chat-Ask context: sending {IncludedMessages} of {TotalMessages} stored messages to the model",
            modelMessages.Count, chatMessages.Count);

        AgentResponse result = await this.lazyAgent.Value.RunAsync(modelMessages, null, new ChatClientAgentRunOptions(chatOptions));

        chatMessages.AddRange(result.Messages);

        TelemetryHelpers.ToolCallsPerTurn.Record(result.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count());

        this.usageLogger.LogAndTag(result, this.deployedModel);

        string? funnyFact = await funnyFactTask;
        if (funnyFact != null)
        {
            chatSession.FunnyFact = funnyFact;
        }

        return result.Messages.LastOrDefault(
            message => message.Role == ChatRole.Assistant && !string.IsNullOrEmpty(message.Text));
    }

    private async Task<string?> SafeGenerateFunnyFactAsync(string input)
    {
        try
        {
            return await this.lazyChatPlanner.Value.GenerateEnhancedFunnyFactAsync(input);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Funny fact generation failed; answering without one");
            return null;
        }
    }

    private async Task SaveSessionAsync(
        MovieTrackerChatSession session,
        ConcurrentDictionary<string, MovieViewModel> sessionCache)
    {
        if (sessionCache.Count > HydratedMovieLimit)
        {
            this.logger.LogInformation(
                "Hydration cache for session {ChatId} holds {Count} movies; trimming to {Limit}",
                session.id, sessionCache.Count, HydratedMovieLimit);
        }

        session.HydratedMovies = sessionCache
            .Take(HydratedMovieLimit)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        await this.repo.SaveChatSessionAsync(session);
    }
}
