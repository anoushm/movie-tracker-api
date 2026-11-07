using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tools;

public class ChatPlanner
{
    private readonly IChatClient chatClient;
    private readonly WikipediaTools wikipediaTools;
    private readonly OmdbTools omdbTools;
    private readonly TrailerTools trailerTools;

    public ChatPlanner(IChatClient chatClient, WikipediaTools wikipediaTools, OmdbTools omdbTools, TrailerTools trailerTools)
    {
        this.chatClient = chatClient;
        this.wikipediaTools = wikipediaTools;
        this.omdbTools = omdbTools;
        this.trailerTools = trailerTools;
    }

    public IEnumerable<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(HandleTrailerRequest),
        AIFunctionFactory.Create(GetMovieRating),
        AIFunctionFactory.Create(CompareMovieRatings),
        AIFunctionFactory.Create(FilterMoviesByRating),
        AIFunctionFactory.Create(GetChatContext),
        AIFunctionFactory.Create(GetMovieContext),
    ];

    [Description("Get a movie's trailer link from its title. Call this when the user asks for a " +
                 "trailer, teaser, preview or promo. Returns the [TRAILER]...[/TRAILER] string to " +
                 "embed in SystemMessage.")]
    public async Task<string> HandleTrailerRequest(
        [Description("The film's title. Resolve any reference yourself first - if the user said " +
                     "'that one' or 'it', pass the title you named earlier in the conversation. " +
                     "This tool cannot see the conversation, only this argument.")] string movieTitle)
        => await this.trailerTools.GetTrailerForTitleAsync(movieTitle);

    [Description("Get a movie's rating from its IMDb ID. Use this whenever the user asks about a rating, " +
                 "score or 'which was highest rated' without naming a source: IMDb is the default rating. " +
                 "Always returns the IMDb rating as the primary rating, with Rotten Tomatoes and Metacritic " +
                 "as secondary context only. Prefer calling this over quoting any TMDb vote average that " +
                 "appeared in earlier search or detail results.")]
    [return: Description("JSON object containing IMDb rating as the primary rating, with other ratings as additional context")]
    public async Task<string> GetMovieRating(
        [Description("The IMDb ID of the movie (e.g., 'tt1375666')")] string imdbId)
    {
        MovieRatingResult result = await this.omdbTools.GetMovieRatingsAsync(imdbId);

        if (!result.IsSuccess)
        {
            return JsonSerializer.Serialize(new { Error = result.ErrorMessage });
        }

        return JsonSerializer.Serialize(new
        {
            Title = result.Title,
            Year = result.Year,
            ImdbRating = result.ImdbRating,
            ImdbVotes = result.ImdbVotes,
            RottenTomatoesRating = result.RottenTomatoesRating,
            MetacriticRating = result.MetacriticRating,
            BoxOffice = result.BoxOffice,
            Rated = result.Rated,
            Runtime = result.Runtime,
            Genre = result.Genre,
            Director = result.Director,
            Writer = result.Writer,
            Actors = result.Actors,
            Awards = result.Awards,
            Plot = result.Plot,
            Summary = $"{result.Title} ({result.Year}) has an IMDb rating of {result.ImdbRating}"
        });
    }

    [Description("Compare IMDb ratings of multiple movies and find the highest rated one. Always uses IMDb ratings for comparison.")]
    [return: Description("Comparison results showing which movie has the highest IMDb rating")]
    public async Task<string> CompareMovieRatings(
        [Description("Comma-separated list of IMDb IDs to compare (e.g., 'tt0068646,tt0071562,tt0099685')")] string imdbIds)
    {
        List<string> ids = imdbIds.Split(',').Select(id => id.Trim()).ToList();
        RatingComparisonResult result = await this.omdbTools.CompareMovieRatingsAsync(ids);

        if (!result.IsSuccess)
        {
            return JsonSerializer.Serialize(new { Error = result.ErrorMessage });
        }

        return JsonSerializer.Serialize(new
        {
            Winner = result.HighestRatedTitle,
            HighestRating = result.HighestRating,
            AllMovies = result.AllMovies.Select(m => new
            {
                Title = $"{m.Title} ({m.Year})",
                ImdbRating = m.ImdbRating,
                RottenTomatoesRating = m.RottenTomatoesRating,
                MetacriticRating = m.MetacriticRating,
                Awards = m.Awards
            }),
            Summary = $"{result.HighestRatedTitle} has the highest IMDb rating of {result.HighestRating}"
        });
    }

    [Description("Filter movies by IMDb rating threshold. Always uses IMDb ratings for filtering.")]
    [return: Description("List of movies that meet the minimum IMDb rating requirement")]
    public async Task<string> FilterMoviesByRating(
        [Description("Comma-separated list of IMDb IDs to filter")] string imdbIds,
        [Description("Minimum IMDb rating threshold (e.g., 7.0)")] double minimumRating)
    {
        List<string> ids = imdbIds.Split(',').Select(id => id.Trim()).ToList();
        List<MovieRatingResult> qualifyingMovies = await this.omdbTools.FilterMoviesByRatingAsync(ids, minimumRating);

        return JsonSerializer.Serialize(new
        {
            MinimumRating = minimumRating,
            TotalMoviesChecked = ids.Count,
            QualifyingMoviesCount = qualifyingMovies.Count,
            QualifyingMovies = qualifyingMovies.Select(m => new
            {
                Title = $"{m.Title} ({m.Year})",
                ImdbRating = m.ImdbRating,
                RottenTomatoesRating = m.RottenTomatoesRating,
                MetacriticRating = m.MetacriticRating,
                BoxOffice = m.BoxOffice
            }),
            Summary = $"Found {qualifyingMovies.Count} out of {ids.Count} movies with IMDb rating {minimumRating}+"
        });
    }

    [Description("Enhanced funny fact generator using Wikipedia data")]
    public async Task<string?> GenerateEnhancedFunnyFactAsync(string userQuery)
    {
        DetectedEntity entity = await DetectEntityAsync(userQuery);

        if (entity.IsNone)
        {
            return null;
        }

        WikipediaResult? wikipediaInfo = await this.wikipediaTools.GetEnhancedInfoAsync(entity.Name, entity.Type);

        if (wikipediaInfo?.ConfidenceScore is not > 0.5)
        {
            return null;
        }

        string enhancedPrompt = $@"
            Based on this Wikipedia information about '{entity.Name}':
            Summary: {wikipediaInfo.WikipediaContent?.Summary}

            Generate ONE surprising, entertaining fact that most people wouldn't know.
            Use ONLY the information above. If it does not support an interesting fact,
            reply with exactly NONE rather than drawing on anything else you know.
            Keep it under 100 characters and make it engaging for movie fans.
            ";
        ChatResponse result = await this.chatClient.GetResponseAsync(enhancedPrompt);
        return NullIfNone(result.Text);
    }

    [Description("Gets detailed information about movies/actors for chat context")]
    public async Task<string?> GetChatContext(
        [Description("The exact movie title, actor name or director name to look up")] string entityName,
        [Description("What the name refers to: 'movie', 'actor' or 'director'")] string entityType = "movie")
    {
        WikipediaResult? wikipediaInfo = await this.wikipediaTools.GetEnhancedInfoAsync(entityName, entityType);

        if (wikipediaInfo == null)
        {
            return null;
        }

        return JsonSerializer.Serialize(new
        {
            Entity = entityName,
            Summary = wikipediaInfo.WikipediaContent?.Summary,
            Facts = wikipediaInfo.StructuredData?.StructuredFacts,
            Confidence = wikipediaInfo.ConfidenceScore
        });
    }

    private sealed record DetectedEntity(string Name, string Type)
    {
        public static readonly DetectedEntity None = new("NONE", "movie");

        public bool IsNone => string.Equals(Name, "NONE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DetectedEntity> DetectEntityAsync(string userQuery)
    {
        string entityDetectionPrompt = $@"
    Analyze the following user query and determine whether it names a specific movie,
    actor/actress, or director.

    Reply with the entity name and its type separated by a single pipe: NAME|TYPE
    TYPE must be exactly one of: movie, actor, director
    For generic queries like 'action movies' or 'comedies', reply with exactly: NONE

    User query: {userQuery}

    Examples:
    Query: 'list movies with tom hanks in 90s' -> Tom Hanks|actor
    Query: 'show me the matrix movies' -> The Matrix|movie
    Query: 'what popular movies came out last year' -> NONE
    Query: 'movies directed by Christopher Nolan' -> Christopher Nolan|director
    ";
        ChatResponse detectionResult = await this.chatClient.GetResponseAsync(entityDetectionPrompt);
        return ParseDetectedEntity(detectionResult.Text);
    }

    private static DetectedEntity ParseDetectedEntity(string? raw)
    {
        string text = raw?.Trim().Trim('\'', '"', '.', ' ') ?? string.Empty;
        if (text.Length == 0)
        {
            return DetectedEntity.None;
        }

        string[] parts = text.Split('|', 2, StringSplitOptions.TrimEntries);
        string name = parts[0].Trim('\'', '"', '.', ' ');

        if (name.Length == 0 || string.Equals(name, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedEntity.None;
        }

        string type = (parts.Length > 1 ? parts[1].Trim('\'', '"', '.', ' ') : "movie").ToLowerInvariant() switch
        {
            "actor" or "actress" or "person" or "cast" => "actor",
            "director" or "filmmaker" => "director",
            _ => "movie",
        };

        return new DetectedEntity(name, type);
    }

    private static string? NullIfNone(string? text)
    {
        string? trimmed = text?.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            || string.Equals(trimmed.Trim('\'', '"', '.', ' '), "NONE", StringComparison.OrdinalIgnoreCase)
                ? null
                : trimmed;
    }

    [Description("Provides rich context about movies/actors for enhanced responses")]
    public async Task<string?> GetMovieContext(
        [Description("The user's question, used to work out which movie or person to describe")] string userQuery)
    {
        DetectedEntity entity = await DetectEntityAsync(userQuery);

        if (entity.IsNone)
        {
            return null;
        }

        WikipediaResult? wikipediaInfo = await this.wikipediaTools.GetEnhancedInfoAsync(entity.Name, entity.Type);

        if (wikipediaInfo?.ConfidenceScore is not > 0.5)
        {
            return null;
        }

        string contextPrompt = $@"
        Based on this Wikipedia information about '{entity.Name}':
        Summary: {wikipediaInfo.WikipediaContent?.Summary}
        Facts: {JsonSerializer.Serialize(wikipediaInfo.StructuredData?.StructuredFacts)}

        Provide 1-2 interesting, engaging facts that would make this movie/person sound fascinating to movie fans.
        Focus on surprising details, cultural impact, or behind-the-scenes stories.
        Use ONLY the information above; reply with exactly NONE if it does not support one.
        Keep it concise but engaging.
        ";
        ChatResponse result = await this.chatClient.GetResponseAsync(contextPrompt);
        return NullIfNone(result.Text);
    }
}
