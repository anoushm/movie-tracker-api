using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.ChatSessions;
using MovieTracker.Api.Models;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ClientChatMessage = MovieTracker.Api.Models.ChatMessage;

namespace MovieTracker.Api.Chat;

public sealed class MovieHydrator
{
    private readonly IDistributedCache cache;
    private readonly TMDbClient tmdbClient;
    private readonly ILogger<MovieHydrator> logger;

    public MovieHydrator(IDistributedCache cache, TMDbClient tmdbClient, ILogger<MovieHydrator> logger)
    {
        this.cache = cache;
        this.tmdbClient = tmdbClient;
        this.logger = logger;
    }

    public ConcurrentDictionary<string, MovieViewModel> LoadHydrationCache(MovieTrackerChatSession session)
    {
        if (session.HydratedMovies is null)
        {
            return new ConcurrentDictionary<string, MovieViewModel>();
        }
        return new ConcurrentDictionary<string, MovieViewModel>(session.HydratedMovies);
    }

    public async Task<ClientChatMessage?> ToClientMessageAsync(
        AIChatMessage message,
        ConcurrentDictionary<string, MovieViewModel> sessionCache)
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

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AssistantChatMessage("No movies were found", movieItems);
            }

            if (!root.TryGetProperty("SystemMessage", out JsonElement systemMessageElement)
                || systemMessageElement.ValueKind != JsonValueKind.String)
            {
                return new AssistantChatMessage("No movies were found", movieItems);
            }

            if (!root.TryGetProperty("MovieList", out JsonElement movieList)
                || movieList.ValueKind != JsonValueKind.Array)
            {
                return new AssistantChatMessage("No movies were found", movieItems);
            }

            string systemMessage = systemMessageElement.GetString() ?? string.Empty;

            List<JsonElement> entries = movieList.EnumerateArray().ToList();
            MovieViewModel?[] slots = new MovieViewModel?[entries.Count];

            await Task.WhenAll(entries.Select((movie, index) =>
                this.SafeProcessMovieAsync(movie, slots, index, sessionCache)));

            movieItems = [.. slots.Where(slot => slot is not null).Select(slot => slot!)];

            return new AssistantChatMessage(systemMessage, movieItems);
        }
        catch (JsonException)
        {
            return new AssistantChatMessage("No movies were found", movieItems);
        }
    }

    private async Task SafeProcessMovieAsync(
        JsonElement movie,
        MovieViewModel?[] slots,
        int index,
        ConcurrentDictionary<string, MovieViewModel> sessionCache)
    {
        try
        {
            await this.ProcessMovieAsync(movie, slots, index, sessionCache);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Failed to hydrate a movie entry; skipping it");
        }
    }

    private async Task ProcessMovieAsync(
        JsonElement movie,
        MovieViewModel?[] slots,
        int index,
        ConcurrentDictionary<string, MovieViewModel> sessionCache)
    {
        if (!movie.TryGetProperty("MovieId", out JsonElement movieIdElement))
        {
            this.logger.LogWarning("Movie entry has no MovieId");
            return;
        }

        string? movieId = movieIdElement.ValueKind == JsonValueKind.Number
            ? movieIdElement.GetRawText()
            : movieIdElement.GetString();

        if (string.IsNullOrWhiteSpace(movieId) || !int.TryParse(movieId, out int tmdbMovieId))
        {
            this.logger.LogWarning("Skipping movie with non-numeric MovieId '{MovieId}'", movieId);
            return;
        }

        if (sessionCache.TryGetValue(movieId, out MovieViewModel? cachedForSession))
        {
            slots[index] = cachedForSession;
            return;
        }

        byte[]? dataInBytes = await this.cache.GetAsync(movieId);
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

        Movie tmdbMovie = await this.tmdbClient.GetMovieAsync(tmdbMovieId, MovieMethods.Videos);
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
            this.logger.LogWarning(ex, "Failed to get trailer for movie {MovieId}", movieId);
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
        await this.cache.SetAsync(movieId, movieViewModelBytes);
    }
}
