using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Core;
using TMDbLib.Client;
using TMDbLib.Objects.Discover;

namespace MovieTracker.Api.Tools;

public record MovieSearchResult(string MovieId, string MovieName, string ReleaseDate, string ImdbId);
public record GenresItem(string GenreId, string GenreName);

public class TheMovieDBTool
{
    private readonly string apiKey;
    private readonly ILogger<TheMovieDBTool> logger;

    public TheMovieDBTool(IConfiguration configuration, ILogger<TheMovieDBTool> logger)
    {
        this.apiKey = configuration["TheMovieDb:Api-Key"] ?? throw new ArgumentNullException("Missing The Movie Db Api Key");
        this.logger = logger;
    }

    private record MovieItem(string MovieId, string MovieName);
    public record PersonSearchResult(string PersonId, string PersonName);

    private async Task<Result<T>> RunTmdbAsync<T>(Func<TMDbClient, Task<T>> operation)
    {
        try
        {
            TMDbClient client = new TMDbClient(apiKey);
            T result = await operation(client);
            return Result<T>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TMDb API call failed");
            return Result<T>.Failure(AppError.External("TMDb service is currently unavailable"));
        }
    }

    private static Result<int> ParseMovieId(string movieId)
    {
        if (string.IsNullOrWhiteSpace(movieId))
        {
            return Result<int>.Failure(AppError.Validation("Movie ID is required"));
        }

        if (!int.TryParse(movieId, out int id))
        {
            return Result<int>.Failure(AppError.Validation($"Invalid movie ID format: {movieId}"));
        }

        return Result<int>.Success(id);
    }

    private static Result<List<int>> ParseIdList(string? ids, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return Result<List<int>>.Success(new List<int>());
        }

        List<int> result = new List<int>();
        string[] parts = ids.Split(',');

        foreach (string part in parts)
        {
            if (!int.TryParse(part.Trim(), out int id))
            {
                return Result<List<int>>.Failure(AppError.Validation($"Invalid {fieldName} format: {part}"));
            }
            result.Add(id);
        }

        return Result<List<int>>.Success(result);
    }

    private static Result<DateTime?> ParseDate(string? dateStr, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
        {
            return Result<DateTime?>.Success(null);
        }

        if (!DateTime.TryParse(dateStr, out DateTime date))
        {
            return Result<DateTime?>.Failure(AppError.Validation($"Invalid {fieldName} format: {dateStr}. Use YYYY-MM-DD."));
        }

        return Result<DateTime?>.Success(date);
    }

    [Description("Get the list of official genres for movies.")]
    [return: Description("a json list of official genres for movies, with the following properties GenreId and the GenreName")]
    public async Task<Result<string>> GetGenresList()
    {
        Result<List<GenresItem>> result = await RunTmdbAsync(async client =>
        {
            var genres = await client.GetMovieGenresAsync();
            return genres.Select(g => new GenresItem(g.Id.ToString(), g.Name)).ToList();
        });

        return result.Match(
            onSuccess: genres => Result<string>.Success(JsonSerializer.Serialize(genres)),
            onFailure: error => Result<string>.Failure(error));
    }

    [Description("Search for people / cast by their name and also known as names.")]
    [return: Description("a json list of people with the following properties PersonId and the PersonName")]
    public async Task<Result<string>> SearchForPeople(
        [Description("The name of the person or cast member")] string personName)
    {
        if (string.IsNullOrWhiteSpace(personName))
        {
            return Result<string>.Failure(AppError.Validation("Person name is required"));
        }

        Result<List<PersonSearchResult>> result = await RunTmdbAsync(async client =>
        {
            var searchResults = await client.SearchPersonAsync(personName, includeAdult: false, region: "en-US");
            return searchResults.Results.Select(p => new PersonSearchResult(p.Id.ToString(), p.Name)).ToList();
        });

        return result.Match(
            onSuccess: people =>
            {
                if (people.Count == 0)
                {
                    return Result<string>.Failure(AppError.NotFound($"No people found for '{personName}'"));
                }
                return Result<string>.Success(JsonSerializer.Serialize(people));
            },
            onFailure: error => Result<string>.Failure(error));
    }

    [Description("Search for movies by their title and release year. Use this to find movies, you can search by movie name or part of a movie name")]
    [return: Description("a json list of movies with the following properties MovieId, MovieName, ReleaseDate, and ImdbId")]
    public async Task<Result<string>> SearchMovies(
        [Description("The title of the movie, or part of the title")] string movieTitle,
        [Description("Optional: The year the movie was released")] string? releaseYear = null)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
        {
            return Result<string>.Failure(AppError.Validation("Movie title is required"));
        }

        int yearAsInt = 0;
        if (!string.IsNullOrWhiteSpace(releaseYear) && !int.TryParse(releaseYear, out yearAsInt))
        {
            return Result<string>.Failure(AppError.Validation($"Invalid release year format: {releaseYear}"));
        }

        Result<List<MovieSearchResult>> result = await RunTmdbAsync(async client =>
        {
            var searchResults = await client.SearchMovieAsync(movieTitle, year: yearAsInt);
            List<MovieSearchResult> movieSearchResults = new List<MovieSearchResult>();

            foreach (var movie in searchResults.Results)
            {
                var externalIds = await client.GetMovieExternalIdsAsync(movie.Id);
                movieSearchResults.Add(new MovieSearchResult(
                    movie.Id.ToString(),
                    movie.Title,
                    movie.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
                    externalIds.ImdbId ?? ""
                ));
            }

            return movieSearchResults;
        });

        return result.Match(
            onSuccess: movies =>
            {
                if (movies.Count == 0)
                {
                    return Result<string>.Failure(AppError.NotFound($"No movies found for '{movieTitle}'"));
                }
                return Result<string>.Success(JsonSerializer.Serialize(movies));
            },
            onFailure: error => Result<string>.Failure(error));
    }

    [Description("Get movie trailers, teasers, video clips, behind-the-scenes content, and interviews for a specific movie.")]
    [return: Description("JSON object containing all available video content including trailers, teasers, clips, and behind-the-scenes footage")]
    public async Task<Result<string>> GetMovieTrailers(
        [Description("The TMDb movie ID")] string movieId)
    {
        Result<int> parseResult = ParseMovieId(movieId);
        if (parseResult.IsFailure)
        {
            return Result<string>.Failure(parseResult.Error!);
        }

        int id = parseResult.Value;

        Result<string> result = await RunTmdbAsync(async client =>
        {
            var videos = await client.GetMovieVideosAsync(id);

            var allVideos = videos.Results
                .Where(v => v.Site == "YouTube")
                .Select(v => new
                {
                    Name = v.Name,
                    Type = v.Type,
                    Key = v.Key,
                    YouTubeUrl = $"https://www.youtube.com/watch?v={v.Key}",
                    EmbedUrl = $"https://www.youtube.com/embed/{v.Key}",
                    ThumbnailUrl = $"https://img.youtube.com/vi/{v.Key}/maxresdefault.jpg",
                    Official = v.Official
                })
                .ToList();

            var trailers = allVideos.Where(v => v.Type == "Trailer").ToList();
            var teasers = allVideos.Where(v => v.Type == "Teaser").ToList();
            var clips = allVideos.Where(v => v.Type == "Clip").ToList();
            var behindScenes = allVideos.Where(v => v.Type == "Behind the Scenes").ToList();
            var featurettes = allVideos.Where(v => v.Type == "Featurette").ToList();

            var movie = await client.GetMovieAsync(id);

            return JsonSerializer.Serialize(new
            {
                MovieTitle = movie.Title,
                MovieYear = movie.ReleaseDate?.Year,
                TotalVideoCount = allVideos.Count,
                MainTrailer = trailers.FirstOrDefault(t => t.Official) ?? trailers.FirstOrDefault() ?? allVideos.FirstOrDefault(),
                Videos = new
                {
                    Trailers = trailers,
                    Teasers = teasers,
                    Clips = clips,
                    BehindTheScenes = behindScenes,
                    Featurettes = featurettes
                },
                Summary = allVideos.Count > 0
                    ? $"Found {allVideos.Count} video(s) for {movie.Title} including trailers, clips, and behind-the-scenes content"
                    : $"No video content available for {movie.Title}"
            });
        });

        return result;
    }

    [Description("Get movie information with trailer included for inline chat display.")]
    [return: Description("Complete movie information with embedded trailer for chat display")]
    public async Task<Result<string>> GetMovieWithTrailer(
        [Description("The TMDb movie ID")] string movieId)
    {
        Result<int> parseResult = ParseMovieId(movieId);
        if (parseResult.IsFailure)
        {
            return Result<string>.Failure(parseResult.Error!);
        }

        int id = parseResult.Value;

        Result<string> result = await RunTmdbAsync(async client =>
        {
            var movie = await client.GetMovieAsync(id);
            var videos = await client.GetMovieVideosAsync(id);

            var trailer = videos.Results
                .Where(v => v.Type == "Trailer" && v.Site == "YouTube")
                .OrderByDescending(v => v.Official)
                .FirstOrDefault();

            return JsonSerializer.Serialize(new
            {
                MovieId = movieId,
                Title = movie.Title,
                Overview = movie.Overview,
                ReleaseDate = movie.ReleaseDate?.ToString("yyyy-MM-dd"),
                ImdbId = movie.ImdbId ?? "",
                DisplayType = "movie-with-inline-trailer",
                Trailer = trailer != null ? new
                {
                    HasTrailer = true,
                    Name = trailer.Name,
                    YouTubeUrl = $"https://www.youtube.com/watch?v={trailer.Key}",
                    EmbedUrl = $"https://www.youtube.com/embed/{trailer.Key}",
                    ThumbnailUrl = $"https://img.youtube.com/vi/{trailer.Key}/maxresdefault.jpg",
                    DisplayInline = true,
                    AllowFullScreen = true
                } : new
                {
                    HasTrailer = false,
                    Name = "No trailer available",
                    YouTubeUrl = "",
                    EmbedUrl = "",
                    ThumbnailUrl = "",
                    DisplayInline = false,
                    AllowFullScreen = false
                },
                ChatMessage = trailer != null
                    ? "Here's the movie info with trailer - tap to watch full screen!"
                    : "Here's the movie info (no trailer available)"
            });
        });

        return result;
    }

    [Description("Handle generic video/trailer requests when context is unclear.")]
    [return: Description("Response asking for clarification about which movie trailer they want")]
    public Task<Result<string>> HandleGenericTrailerRequest(
        [Description("The user's generic trailer request")] string userQuery)
    {
        return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(new
        {
            Type = "clarification-needed",
            Message = "I'd be happy to show you a trailer! Which movie are you interested in?",
            Suggestions = new[]
            {
                "Try: 'Show me the Inception trailer'",
                "Or: 'Play the Batman trailer'",
                "Or: 'Trailer for Top Gun Maverick'"
            },
            FollowUp = "Just tell me the movie name and I'll find the trailer for you!"
        })));
    }

    [Description("Get detailed information about a specific movie by its ID.")]
    [return: Description("Detailed information about the movie, including title, overview, release date, genres, runtime, and ImdbId.")]
    public async Task<Result<string>> GetMovieDetails(
        [Description("The ID of the movie")] string movieId)
    {
        Result<int> parseResult = ParseMovieId(movieId);
        if (parseResult.IsFailure)
        {
            return Result<string>.Failure(parseResult.Error!);
        }

        int id = parseResult.Value;

        Result<string> result = await RunTmdbAsync(async client =>
        {
            var movie = await client.GetMovieAsync(id);

            var movieDetails = new
            {
                MovieId = movieId,
                Title = movie.Title,
                Overview = movie.Overview,
                ReleaseDate = movie.ReleaseDate?.ToString("yyyy-MM-dd"),
                Genres = movie.Genres.Select(g => new { Id = g.Id, Name = g.Name }).ToList(),
                Runtime = movie.Runtime,
                VoteAverage = movie.VoteAverage,
                VoteCount = movie.VoteCount,
                ImdbId = movie.ImdbId ?? "",
                PosterPath = movie.PosterPath,
                BackdropPath = movie.BackdropPath
            };

            return JsonSerializer.Serialize(movieDetails);
        });

        return result;
    }

    [Description("Search for keywords related to movies.")]
    [return: Description("A JSON list of keywords with their properties such as KeywordId and Name.")]
    public async Task<Result<string>> SearchKeywords(
        [Description("The name or partial name of the keyword")] string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result<string>.Failure(AppError.Validation("Keyword is required"));
        }

        Result<List<object>> result = await RunTmdbAsync(async client =>
        {
            var keywords = await client.SearchKeywordAsync(keyword);
            return keywords.Results.Select(k => (object)new { KeywordId = k.Id, Name = k.Name }).ToList();
        });

        return result.Match(
            onSuccess: keywords =>
            {
                if (keywords.Count == 0)
                {
                    return Result<string>.Failure(AppError.NotFound($"No keywords found for '{keyword}'"));
                }
                return Result<string>.Success(JsonSerializer.Serialize(keywords));
            },
            onFailure: error => Result<string>.Failure(error));
    }

    [Description("Returns detailed information about a specific movie in a serialized format")]
    [return: Description("Serialized JSON containing information about a specific movie including ImdbId")]
    public async Task<Result<string>> DescribeMovie(
        [Description("The movie ID of a specific movie")] string movieId)
    {
        Result<int> parseResult = ParseMovieId(movieId);
        if (parseResult.IsFailure)
        {
            return Result<string>.Failure(parseResult.Error!);
        }

        int id = parseResult.Value;

        Result<string> result = await RunTmdbAsync(async client =>
        {
            var movie = await client.GetMovieAsync(id);
            var credits = await client.GetMovieCreditsAsync(movie.Id);

            var movieData = new
            {
                MovieId = movieId,
                Title = movie.Title,
                Overview = movie.Overview,
                ReleaseDate = movie.ReleaseDate?.ToString("yyyy-MM-dd"),
                Genres = movie.Genres.Select(g => g.Name).ToList(),
                Runtime = movie.Runtime,
                Tagline = movie.Tagline,
                Rating = movie.VoteAverage,
                Language = movie.OriginalLanguage,
                ImdbId = movie.ImdbId ?? "",
                Cast = credits?.Cast.Take(5).Select(c => c.Name).ToList()
            };

            return JsonSerializer.Serialize(movieData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        });

        return result;
    }

    [Description("Discover movies based on various filters and sort options.")]
    [return: Description("A JSON list of movies with their properties such as MovieId, MovieName, ReleaseDate, and ImdbId.")]
    public async Task<Result<string>> DiscoverMovies(
        [Description("Optional: Start release date (YYYY-MM-DD)")] string? releaseDateFrom = null,
        [Description("Optional: End release date (YYYY-MM-DD)")] string? releaseDateTo = null,
        [Description("Optional: Include movies with these cast IDs (comma-separated)")] string? castIds = null,
        [Description("Optional: Include movies with these genre IDs (comma-separated)")] string? genreIds = null,
        [Description("Optional: Include movies with these keyword IDs (comma-separated)")] string? keywordIds = null,
        [Description("Optional: Minimum vote average (1-10)")] double? minVoteAverage = null,
        [Description("Optional: Maximum vote average (1-10)")] double? maxVoteAverage = null,
        [Description("Optional: Minimum vote count")] int? minVoteCount = null,
        [Description("Optional: Maximum vote count")] int? maxVoteCount = null)
    {
        Result<DateTime?> dateFromResult = ParseDate(releaseDateFrom, "releaseDateFrom");
        if (dateFromResult.IsFailure)
        {
            return Result<string>.Failure(dateFromResult.Error!);
        }

        Result<DateTime?> dateToResult = ParseDate(releaseDateTo, "releaseDateTo");
        if (dateToResult.IsFailure)
        {
            return Result<string>.Failure(dateToResult.Error!);
        }

        Result<List<int>> castIdsResult = ParseIdList(castIds, "castIds");
        if (castIdsResult.IsFailure)
        {
            return Result<string>.Failure(castIdsResult.Error!);
        }

        Result<List<int>> genreIdsResult = ParseIdList(genreIds, "genreIds");
        if (genreIdsResult.IsFailure)
        {
            return Result<string>.Failure(genreIdsResult.Error!);
        }

        Result<List<int>> keywordIdsResult = ParseIdList(keywordIds, "keywordIds");
        if (keywordIdsResult.IsFailure)
        {
            return Result<string>.Failure(keywordIdsResult.Error!);
        }

        DateTime? dateFrom = dateFromResult.Value;
        DateTime? dateTo = dateToResult.Value;
        List<int> parsedCastIds = castIdsResult.Value!;
        List<int> parsedGenreIds = genreIdsResult.Value!;
        List<int> parsedKeywordIds = keywordIdsResult.Value!;

        Result<List<MovieSearchResult>> result = await RunTmdbAsync(async client =>
        {
            DiscoverMovie query = client.DiscoverMoviesAsync();

            if (dateFrom.HasValue)
            {
                query = query.WherePrimaryReleaseDateIsAfter(dateFrom.Value);
            }

            if (dateTo.HasValue)
            {
                query = query.WherePrimaryReleaseDateIsBefore(dateTo.Value);
            }

            if (parsedCastIds.Count > 0)
            {
                query = query.IncludeWithAllOfCast(parsedCastIds);
            }

            if (parsedGenreIds.Count > 0)
            {
                query = query.IncludeWithAllOfGenre(parsedGenreIds);
            }

            if (parsedKeywordIds.Count > 0)
            {
                query = query.IncludeWithAllOfKeywords(parsedKeywordIds);
            }

            if (minVoteAverage.HasValue)
            {
                query = query.WhereVoteAverageIsAtLeast(minVoteAverage.Value);
            }

            if (maxVoteAverage.HasValue)
            {
                query = query.WhereVoteAverageIsAtMost(maxVoteAverage.Value);
            }

            if (minVoteCount.HasValue)
            {
                query = query.WhereVoteCountIsAtLeast(minVoteCount.Value);
            }

            if (maxVoteCount.HasValue)
            {
                query = query.WhereVoteCountIsAtMost(maxVoteCount.Value);
            }

            var searchResults = await query.Query();
            List<MovieSearchResult> movieList = new List<MovieSearchResult>();

            foreach (var movie in searchResults.Results)
            {
                var externalIds = await client.GetMovieExternalIdsAsync(movie.Id);
                movieList.Add(new MovieSearchResult(
                    movie.Id.ToString(),
                    movie.Title,
                    movie.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
                    externalIds.ImdbId ?? ""
                ));
            }

            return movieList;
        });

        return result.Match(
            onSuccess: movies =>
            {
                if (movies.Count == 0)
                {
                    return Result<string>.Failure(AppError.NotFound("No movies found matching the criteria"));
                }
                return Result<string>.Success(JsonSerializer.Serialize(movies));
            },
            onFailure: error => Result<string>.Failure(error));
    }
}
