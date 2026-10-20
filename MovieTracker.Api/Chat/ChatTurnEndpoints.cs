using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using MovieTracker.Api.ChatSessions;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace MovieTracker.Api.Chat;

public static class ChatTurnEndpoints
{
    private const int MaxModelTurns = 12;
    private const int CollapsedToolResultChars = 160;
    private const int CollapsedArgumentChars = 60;
    private const int CollapsedResultNames = 6;
    private const int HydratedMovieLimit = 150;

    private static readonly JsonSerializerOptions StructuredOutputJsonOptions =
        new(AIJsonUtilities.DefaultOptions) { PropertyNamingPolicy = null };

    private static readonly ChatResponseFormat MovieListResponseFormat =
        ChatResponseFormat.ForJsonSchema(
            AIJsonUtilities.CreateJsonSchema(
                typeof(MovieListResponse),
                serializerOptions: StructuredOutputJsonOptions,
                inferenceOptions: new AIJsonSchemaCreateOptions
                {
                    TransformOptions = new AIJsonSchemaTransformOptions
                    {
                        DisallowAdditionalProperties = true,
                        RequireAllProperties = true,
                    }
                }),
            nameof(MovieListResponse));

#pragma warning disable MAAI001
    private static readonly IChatReducer ContextReducer = new PipelineCompactionStrategy(
    [
        new ToolResultCompactionStrategy(
            trigger: CompactionTriggers.HasToolCalls(),
            minimumPreservedGroups: 2)
        {
            ToolCallFormatter = SummariseToolCalls,
        },
        new SlidingWindowCompactionStrategy(
            trigger: CompactionTriggers.TurnsExceed(MaxModelTurns),
            minimumPreservedTurns: 4),
    ]).AsChatReducer();

    private static string SummariseToolCalls(CompactionMessageGroup group)
    {
        Dictionary<string, string> results = new Dictionary<string, string>();
        foreach (FunctionResultContent result in group.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>())
        {
            results[result.CallId] = result.Result?.ToString() ?? string.Empty;
        }

        List<string> lines = new List<string> { "[Earlier tool calls]" };
        foreach (FunctionCallContent call in group.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            string text = results.TryGetValue(call.CallId, out string? result) ? result : string.Empty;
            lines.Add($"{call.Name}({SummariseArguments(call.Arguments)}) -> {SummariseResult(text)}");
        }

        return string.Join("\n", lines);
    }

    private static string SummariseArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", arguments
            .Where(argument => argument.Value is not null)
            .Select(argument =>
            {
                string value = argument.Value!.ToString() ?? string.Empty;
                if (value.Length > CollapsedArgumentChars)
                {
                    value = value[..CollapsedArgumentChars] + "...";
                }
                return $"{argument.Key}={value}";
            }));
    }

    private static string SummariseResult(string text)
    {
        if (text.Length == 0)
        {
            return "(no result)";
        }

        try
        {
            if (text.TrimStart().StartsWith('['))
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    List<string?> names = document.RootElement.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.Object)
                        .Select(NameOf)
                        .Where(name => name is not null)
                        .ToList();

                    int total = document.RootElement.GetArrayLength();
                    string label = total == 1 ? "result" : "results";
                    if (total == 0)
                    {
                        return "0 results";
                    }

                    if (names.Count > 0)
                    {
                        string shown = string.Join(", ", names.Take(CollapsedResultNames));
                        string more = names.Count > CollapsedResultNames ? $", +{names.Count - CollapsedResultNames} more" : string.Empty;
                        return $"{total} {label}: {shown}{more}";
                    }

                    return $"{total} {label}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return text.Length <= CollapsedToolResultChars
            ? text
            : $"{text[..CollapsedToolResultChars]}... [{text.Length - CollapsedToolResultChars} more chars elided]";
    }

    private static string? NameOf(JsonElement element)
    {
        foreach (string property in new[] { "MovieName", "Title", "PersonName", "Name", "GenreName" })
        {
            if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
#pragma warning restore MAAI001

    public static async Task<IResult> Ask(
        string chatId,
        HttpRequest request,
        ChatSessionRepository repo,
        AIAgent agent,
        IDistributedCache cache,
        TMDbClient tmdbClient,
        ILogger<ChatMessageResponse> logger)
    {
        try
        {
            Ask? ask;
            try
            {
                ask = await request.ReadFromJsonAsync<Ask>();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid request, missing ask object" });
            }

            if (ask is null)
            {
                return Results.BadRequest(new { error = "Invalid request, missing ask object" });
            }

            MoviceTrackerChatSession chatSession;
            try
            {
                chatSession = await repo.GetChatSession(chatId);
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.NotFound(new { error = "Chat not found" });
            }

            ConcurrentDictionary<string, MovieViewModel> sessionCache = LoadHydrationCache(chatSession);
            await RunTurnAsync(agent, chatSession, ask.Input);

            List<ChatMessage> responseMessages = new List<ChatMessage>();
            foreach (AIChatMessage message in chatSession.ChatHistory)
            {
                ChatMessage? clientMessage = await ToClientMessageAsync(message, sessionCache, cache, tmdbClient, logger);
                if (clientMessage != null)
                {
                    responseMessages.Add(clientMessage);
                }
            }

            await SaveSessionAsync(repo, chatSession, sessionCache, logger);

            return Results.Ok(new ChatMessageResponse(null, responseMessages));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat-Ask failed for session {ChatId}", chatId);
            return Results.Json(new { error = "Chat request failed." }, statusCode: 503);
        }
    }

    public static async Task<IResult> AskV2(
        string chatId,
        HttpRequest request,
        ChatSessionRepository repo,
        AIAgent agent,
        IDistributedCache cache,
        TMDbClient tmdbClient,
        ILogger<ChatTurnResponse> logger)
    {
        try
        {
            Ask? ask;
            try
            {
                ask = await request.ReadFromJsonAsync<Ask>();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid request, missing ask object" });
            }

            if (ask is null)
            {
                return Results.BadRequest(new { error = "Invalid request, missing ask object" });
            }

            MoviceTrackerChatSession chatSession;
            try
            {
                chatSession = await repo.GetChatSession(chatId);
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.NotFound(new { error = "Chat not found" });
            }

            ConcurrentDictionary<string, MovieViewModel> sessionCache = LoadHydrationCache(chatSession);
            AIChatMessage? newAssistantMessage = await RunTurnAsync(agent, chatSession, ask.Input);

            ChatMessage turn;
            if (newAssistantMessage == null)
            {
                turn = new AssistantChatMessage("No movies were found", new List<MovieViewModel>());
            }
            else
            {
                ChatMessage? hydrated = await ToClientMessageAsync(newAssistantMessage, sessionCache, cache, tmdbClient, logger);
                turn = hydrated ?? new AssistantChatMessage("No movies were found", new List<MovieViewModel>());
            }

            await SaveSessionAsync(repo, chatSession, sessionCache, logger);

            return Results.Ok(new ChatTurnResponse(null, turn));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat-Ask-V2 failed for session {ChatId}", chatId);
            return Results.Json(new { error = "Chat request failed." }, statusCode: 503);
        }
    }

    private static ConcurrentDictionary<string, MovieViewModel> LoadHydrationCache(MoviceTrackerChatSession session)
    {
        if (session.HydratedMovies is null)
        {
            return new ConcurrentDictionary<string, MovieViewModel>();
        }
        return new ConcurrentDictionary<string, MovieViewModel>(session.HydratedMovies);
    }

    private static async Task SaveSessionAsync(
        ChatSessionRepository repo,
        MoviceTrackerChatSession session,
        ConcurrentDictionary<string, MovieViewModel> sessionCache,
        ILogger logger)
    {
        if (sessionCache.Count > HydratedMovieLimit)
        {
            logger.LogInformation(
                "Hydration cache for session {ChatId} holds {Count} movies; trimming to {Limit}",
                session.id, sessionCache.Count, HydratedMovieLimit);
        }

        session.HydratedMovies = sessionCache
            .Take(HydratedMovieLimit)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        await repo.SaveChatSession(session);
    }

    private static async Task<AIChatMessage?> RunTurnAsync(AIAgent agent, MoviceTrackerChatSession chatSession, string input)
    {
        List<AIChatMessage> chatMessages = chatSession.ChatHistory;

        chatMessages.Add(new AIChatMessage(ChatRole.User, input));

        ChatOptions chatOptions = new ChatOptions
        {
            ResponseFormat = MovieListResponseFormat,
        };

        IEnumerable<AIChatMessage> reduced = await ContextReducer.ReduceAsync([.. chatMessages], default);
        List<AIChatMessage> modelMessages = reduced.ToList();

        AgentResponse result = await agent.RunAsync(modelMessages, null, new ChatClientAgentRunOptions(chatOptions));

        chatMessages.AddRange(result.Messages);

        return result.Messages.LastOrDefault(
            message => message.Role == ChatRole.Assistant && !string.IsNullOrEmpty(message.Text));
    }

    private static async Task<ChatMessage?> ToClientMessageAsync(
        AIChatMessage message,
        ConcurrentDictionary<string, MovieViewModel> sessionCache,
        IDistributedCache cache,
        TMDbClient tmdbClient,
        ILogger logger)
    {
        string text = message.Text;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (message.Role == ChatRole.User)
        {
            return new UserChatMessage(text);
        }

        if (message.Role != ChatRole.Assistant)
        {
            return null;
        }

        text = text.Replace("```json", "").Replace("```", "");
        List<MovieViewModel> movieItems = new List<MovieViewModel>();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            string? systemMessage = root.GetProperty("SystemMessage").GetString() ?? string.Empty;

            if (root.TryGetProperty("MovieList", out JsonElement movieList))
            {
                List<JsonElement> entries = movieList.EnumerateArray().ToList();
                MovieViewModel?[] slots = new MovieViewModel?[entries.Count];

                await Task.WhenAll(entries.Select((movie, index) =>
                    SafeProcessMovieAsync(movie, slots, index, sessionCache, cache, tmdbClient, logger)));

                movieItems = [.. slots.Where(slot => slot is not null).Select(slot => slot!)];
            }

            return new AssistantChatMessage(systemMessage ?? string.Empty, movieItems);
        }
        catch (JsonException)
        {
            return new AssistantChatMessage("No movies were found", movieItems);
        }
    }

    private static async Task SafeProcessMovieAsync(
        JsonElement movie,
        MovieViewModel?[] slots,
        int index,
        ConcurrentDictionary<string, MovieViewModel> sessionCache,
        IDistributedCache cache,
        TMDbClient tmdbClient,
        ILogger logger)
    {
        try
        {
            await ProcessMovieAsync(movie, slots, index, sessionCache, cache, tmdbClient, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to hydrate a movie entry; skipping it");
        }
    }

    private static async Task ProcessMovieAsync(
        JsonElement movie,
        MovieViewModel?[] slots,
        int index,
        ConcurrentDictionary<string, MovieViewModel> sessionCache,
        IDistributedCache cache,
        TMDbClient tmdbClient,
        ILogger logger)
    {
        if (!movie.TryGetProperty("MovieId", out JsonElement movieIdElement))
        {
            logger.LogWarning("Movie entry has no MovieId");
            return;
        }

        string? movieId = movieIdElement.ValueKind == JsonValueKind.Number
            ? movieIdElement.GetRawText()
            : movieIdElement.GetString();

        if (string.IsNullOrWhiteSpace(movieId) || !int.TryParse(movieId, out int tmdbMovieId))
        {
            logger.LogWarning("Skipping movie with non-numeric MovieId '{MovieId}'", movieId);
            return;
        }

        if (sessionCache.TryGetValue(movieId, out MovieViewModel? cachedForSession))
        {
            slots[index] = cachedForSession;
            return;
        }

        byte[]? dataInBytes = await cache.GetAsync(movieId);
        if (dataInBytes != null)
        {
            MovieViewModel? movieViewModel = JsonSerializer.Deserialize<MovieViewModel>(dataInBytes);
            if (movieViewModel != null)
            {
                sessionCache[movieId] = movieViewModel;
                slots[index] = movieViewModel;
            }
            return;
        }

        Movie tmdbMovie = await tmdbClient.GetMovieAsync(tmdbMovieId, MovieMethods.Videos);
        string imdbId = tmdbMovie.ImdbId ?? "";

        MovieTrailerInfo? trailerInfo = null;
        try
        {
            Video? trailer = tmdbMovie.Videos?.Results
                .Where(v => v.Type == "Trailer" && v.Site == "YouTube")
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.Size)
                .FirstOrDefault();

            trailerInfo = MovieTrailerInfo.CreateFrom(trailer);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get trailer for movie {MovieId}", movieId);
            trailerInfo = null;
        }

        MovieViewModel resolved = new MovieViewModel(
            tmdbMovie.PosterPath,
            tmdbMovie.Adult,
            tmdbMovie.Overview,
            tmdbMovie.ReleaseDate,
            tmdbMovie.Genres.Select(g => g.Id).ToList(),
            movieId,
            tmdbMovie.OriginalTitle,
            tmdbMovie.OriginalLanguage,
            tmdbMovie.Title,
            tmdbMovie.BackdropPath,
            tmdbMovie.Popularity,
            tmdbMovie.VoteCount,
            tmdbMovie.Video,
            tmdbMovie.VoteAverage,
            Favorite: false,
            imdbId,
            trailerInfo,
            GenreNames: tmdbMovie.Genres.Select(g => g.Name).ToList(),
            Runtime: tmdbMovie.Runtime,
            Tagline: tmdbMovie.Tagline);

        sessionCache[movieId] = resolved;
        slots[index] = resolved;

        byte[] movieViewModelBytes = JsonSerializer.SerializeToUtf8Bytes(resolved);
        await cache.SetAsync(movieId, movieViewModelBytes);
    }
}
