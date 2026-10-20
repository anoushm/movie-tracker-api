using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using TMDbLib.Client;
using TMDbLib.Objects.Discover;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.People;
using TMDbLib.Objects.Search;

namespace MovieTracker.Api.Prompts;

public record MovieSearchResult(
    string MovieId,
    string MovieName,
    string ReleaseDate,
    string ImdbId,
    int? RuntimeMinutes = null);

public record GenresItem(string GenreId, string GenreName);

public class TheMovieDBKernelFunctions
{
    private const int MaxConcurrentTmdbCalls = 8;
    private const int MaxPersonCredits = 25;
    private const int MaxGenreFilterLookups = 60;
    private const string CertificationCountry = "US";
    private const int RatingSortMinimumVotes = 200;

    private readonly TMDbClient client;

    public TheMovieDBKernelFunctions(TMDbClient client)
    {
        this.client = client;
    }

    public IEnumerable<AITool> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(GetGenresList),
            AIFunctionFactory.Create(SearchForPeople),
            AIFunctionFactory.Create(GetPersonMovieCredits),
            AIFunctionFactory.Create(SearchMovies),
            AIFunctionFactory.Create(GetMovieTrailers),
            AIFunctionFactory.Create(SearchKeywords),
            AIFunctionFactory.Create(DescribeMovie),
            AIFunctionFactory.Create(DiscoverMovies),
        ];
    }

    private async Task<Dictionary<int, string>> ResolveImdbIdsAsync(IEnumerable<int> movieIds)
    {
        ConcurrentDictionary<int, string> resolved = new ConcurrentDictionary<int, string>();
        using SemaphoreSlim throttle = new SemaphoreSlim(MaxConcurrentTmdbCalls);

        await Task.WhenAll(movieIds.Distinct().Select(async movieId =>
        {
            await throttle.WaitAsync();
            try
            {
                ExternalIdsMovie externalIds = await client.GetMovieExternalIdsAsync(movieId);
                resolved[movieId] = externalIds.ImdbId ?? "";
            }
            catch
            {
                resolved[movieId] = "";
            }
            finally
            {
                throttle.Release();
            }
        }));

        return new Dictionary<int, string>(resolved);
    }

    private sealed record MovieFacts(
        string ImdbId,
        IReadOnlyList<int> GenreIds,
        IReadOnlyList<string> GenreNames,
        int? Runtime);

    private async Task<Dictionary<int, MovieFacts>> ResolveMovieFactsAsync(IEnumerable<int> movieIds)
    {
        ConcurrentDictionary<int, MovieFacts> resolved = new ConcurrentDictionary<int, MovieFacts>();
        using SemaphoreSlim throttle = new SemaphoreSlim(MaxConcurrentTmdbCalls);

        await Task.WhenAll(movieIds.Distinct().Select(async movieId =>
        {
            await throttle.WaitAsync();
            try
            {
                Movie movie = await client.GetMovieAsync(movieId);
                List<int> genreIds = [.. movie.Genres?.Select(genre => genre.Id) ?? []];
                List<string> genreNames = [.. movie.Genres?.Select(genre => genre.Name).Where(name => name is not null) ?? []];
                resolved[movieId] = new MovieFacts(
                    movie.ImdbId ?? "",
                    genreIds,
                    genreNames,
                    movie.Runtime);
            }
            catch
            {
                resolved[movieId] = new MovieFacts("", [], [], null);
            }
            finally
            {
                throttle.Release();
            }
        }));

        return new Dictionary<int, MovieFacts>(resolved);
    }

    private async Task<List<MovieSearchResult>> ToSearchResultsAsync<T>(
        IReadOnlyList<T> movies,
        Func<T, int> getId,
        Func<T, string?> getTitle,
        Func<T, DateTime?> getReleaseDate)
    {
        Dictionary<int, string> imdbIds = await ResolveImdbIdsAsync(movies.Select(getId));

        return
        [
            .. movies.Select(movie => new MovieSearchResult(
                getId(movie).ToString(),
                getTitle(movie) ?? "",
                getReleaseDate(movie)?.ToString("yyyy-MM-dd") ?? "",
                imdbIds.TryGetValue(getId(movie), out string? imdbId) ? imdbId : ""))
        ];
    }

    private static bool TryGetTmdbId(string? movieId, out int id, out string error)
    {
        return TryGetId(movieId, "movie", "13", "Call SearchMovies or DiscoverMovies first and use the MovieId it returns.", out id, out error);
    }

    private static bool TryGetPersonId(string? personId, out int id, out string error)
    {
        return TryGetId(personId, "person", "525", "Call SearchForPeople first and use the PersonId it returns.", out id, out error);
    }

    private static bool TryGetId(string? value, string kind, string example, string hint, out int id, out string error)
    {
        if (int.TryParse(value, out id))
        {
            error = string.Empty;
            return true;
        }

        error = JsonSerializer.Serialize(new
        {
            Error = $"'{value}' is not a valid TMDb {kind} id. Ids are numeric, e.g. '{example}'.",
            Hint = hint
        });
        return false;
    }

    private static List<int> ParseIdList(string ids)
    {
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out int parsed) ? parsed : (int?)null)
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value)
            .ToList();
    }

    [Description("Get the list of official genres for movies.")]
    [return: Description("a json list of official genres for movies, with the following properties GenreId and the GenreName")]
    public async Task<string> GetGenresList()
    {
        List<Genre> genres = await client.GetMovieGenresAsync();
        List<GenresItem> genresList = genres.Select(g => new GenresItem(g.Id.ToString(), g.Name)).ToList();
        return JsonSerializer.Serialize(genresList);
    }

    public record PersonSearchResult(string PersonId, string PersonName);

    [Description("Search for people / cast by their name and also known as names.")]
    [return: Description("a json list of people with the following properties PersonId and the PersonName")]
    public async Task<string> SearchForPeople(
        [Description("The name of the person or cast member")] string personName)
    {
        SearchContainer<SearchPerson> searchResults = await client.SearchPersonAsync(personName, includeAdult: false);
        List<PersonSearchResult> personSearchResults =
            searchResults.Results.Select(p => new PersonSearchResult(p.Id.ToString(), p.Name)).ToList();
        return JsonSerializer.Serialize(personSearchResults);
    }

    [Description("Get one person's filmography from their PersonId, with the exact job they did on " +
                 "each film. This is the right tool for 'what did X direct', 'what did X write' and " +
                 "'what has X been in', because it is the only one that can tell a directing credit " +
                 "from a writing or producing one - pass job='Director' for 'directed by'. Prefer " +
                 "this over DiscoverMovies(crewIds:) for any question about one person's own work. " +
                 "For 'sci-fi films X directed', pass genreIds as well and let this filter them; the " +
                 "returned credits also carry Genres so you can narrow further yourself.")]
    [return: Description("JSON with the person's credits: MovieId, MovieName, ReleaseDate, ImdbId, Genres, and either Character (acting) or Job")]
    public async Task<string> GetPersonMovieCredits(
        [Description("The TMDb PersonId, as returned by SearchForPeople")] string personId,
        [Description("Optional: keep only credits of this kind. 'Director' for 'directed by', " +
                     "'Writer' for 'wrote', 'Producer' for 'produced', 'Actor' for 'starred in'. " +
                     "Omit for acting roles plus all crew work.")] string? job = null,
        [Description("Optional: only films released in or after this year (YYYY)")] string? fromYear = null,
        [Description("Optional: only films released in or before this year (YYYY)")] string? toYear = null,
        [Description("Optional: keep only films in these genres, as comma-separated genre ids from " +
                     "GetGenresList. Use this for 'sci-fi films X directed' rather than filtering the " +
                     "results yourself - a person's credits do not otherwise carry genres.")] string? genreIds = null,
        [Description("Optional: how to order the credits. 'popularity' (default, best known first), " +
                     "'longest' or 'shortest' by runtime, 'newest' or 'oldest' by release date. For " +
                     "'the longest film X directed' pass 'longest' and take the first result - do not " +
                     "ask for the default order and compare the runtimes yourself.")] string? sortBy = null)
    {
        if (!TryGetPersonId(personId, out int tmdbPersonId, out string idError))
        {
            return idError;
        }

        MovieCredits credits = await client.GetPersonMovieCreditsAsync(tmdbPersonId);

        string? wantJob = job?.Trim();
        bool filterByJob = !string.IsNullOrEmpty(wantJob);

        (CreditKind jobKind, string[] jobNames) = filterByJob
            ? ResolveJobFilter(wantJob!)
            : (CreditKind.Crew, []);

        bool includeCast = !filterByJob || jobKind == CreditKind.Cast;
        bool includeCrew = !filterByJob || jobKind == CreditKind.Crew;

        List<CreditProjection> acting = !includeCast
            ? new List<CreditProjection>()
            : (credits.Cast ?? new List<MovieRole>()).Select(role => new CreditProjection(
                MovieId: role.Id.ToString(),
                MovieName: role.Title,
                ReleaseDate: role.ReleaseDate,
                Credit: string.IsNullOrWhiteSpace(role.Character) ? "Actor" : $"as {role.Character}",
                Popularity: (double?)role.Popularity)).ToList();

        List<CreditProjection> working = (!includeCrew ? new List<MovieJob>() : (credits.Crew ?? new List<MovieJob>()))
            .Where(crew => !filterByJob
                || jobNames.Contains(crew.Job ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Select(crew => new CreditProjection(
                MovieId: crew.Id.ToString(),
                MovieName: crew.Title,
                ReleaseDate: crew.ReleaseDate,
                Credit: crew.Job,
                Popularity: (double?)null))
            .ToList();

        List<GroupedCredit> all = acting.Concat(working)
            .Where(credit => WithinYears(credit.ReleaseDate, fromYear, toYear))
            .GroupBy(credit => credit.MovieId)
            .Select(group => new GroupedCredit(
                MovieId: group.First().MovieId,
                MovieName: group.First().MovieName,
                ReleaseDate: group.First().ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
                Credit: string.Join(", ", group.Select(c => c.Credit).Distinct()),
                Popularity: group.Max(c => c.Popularity),
                Date: group.First().ReleaseDate ?? DateTime.MinValue))
            .OrderByDescending(credit => credit.Popularity ?? -1)
            .ThenByDescending(credit => credit.Date)
            .ToList();

        List<int> wantGenres = string.IsNullOrEmpty(genreIds) ? new List<int>() : ParseIdList(genreIds);
        bool filterByGenre = wantGenres.Count > 0;

        string? order = sortBy?.Trim().ToLowerInvariant();
        bool sortNeedsRuntime = order is "longest" or "shortest";

        List<GroupedCredit> candidates = all.Take(filterByGenre || sortNeedsRuntime ? MaxGenreFilterLookups : MaxPersonCredits).ToList();

        Dictionary<int, MovieFacts> facts = await ResolveMovieFactsAsync(
            candidates.Select(credit => int.TryParse(credit.MovieId, out int id) ? id : 0).Where(id => id > 0));

        static MovieFacts FactsFor(Dictionary<int, MovieFacts> lookup, string movieId)
        {
            if (int.TryParse(movieId, out int id) && lookup.TryGetValue(id, out MovieFacts? found))
            {
                return found;
            }
            return new MovieFacts("", [], [], null);
        }

        List<GroupedCredit> matching = filterByGenre
            ? candidates.Where(credit => FactsFor(facts, credit.MovieId).GenreIds.Intersect(wantGenres).Any()).ToList()
            : candidates;

        matching = order switch
        {
            "longest" => [.. matching.OrderByDescending(c => FactsFor(facts, c.MovieId).Runtime ?? 0)],
            "shortest" => [.. matching
                .Where(c => FactsFor(facts, c.MovieId).Runtime is > 0)
                .OrderBy(c => FactsFor(facts, c.MovieId).Runtime)],
            "newest" => [.. matching.OrderByDescending(c => c.Date)],
            "oldest" => [.. matching.Where(c => c.Date > DateTime.MinValue).OrderBy(c => c.Date)],
            _ => matching,
        };

        List<object> shown = matching.Take(MaxPersonCredits).Select(credit => (object)new
        {
            credit.MovieId,
            credit.MovieName,
            credit.ReleaseDate,
            credit.Credit,
            FactsFor(facts, credit.MovieId).ImdbId,
            Genres = FactsFor(facts, credit.MovieId).GenreNames,
            RuntimeMinutes = FactsFor(facts, credit.MovieId).Runtime,
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            PersonId = personId,
            JobFilter = filterByJob ? wantJob : "(none - acting and all crew credits)",
            GenreFilter = filterByGenre ? genreIds : "(none)",
            SortedBy = order is "longest" or "shortest" or "newest" or "oldest" ? order : "popularity",
            TotalCredits = all.Count,
            TotalMatching = matching.Count,
            Returned = shown.Count,
            Credits = shown,
            Note = matching.Count > shown.Count
                ? $"Showing {shown.Count} of {matching.Count} matching credits, best known first."
                : "Complete list for this filter.",
            Hint = "ImdbId is included - pass it straight to GetMovieRating or CompareMovieRatings. "
                 + "No need to call DescribeMovie first."
        });
    }

    private sealed record CreditProjection(string MovieId, string MovieName, DateTime? ReleaseDate, string Credit, double? Popularity);

    private sealed record GroupedCredit(string MovieId, string MovieName, string ReleaseDate, string Credit, double? Popularity, DateTime Date);

    private enum CreditKind { Cast, Crew }

    private static (CreditKind Kind, string[] Jobs) ResolveJobFilter(string job)
    {
        return job.Trim().ToLowerInvariant() switch
        {
            "actor" or "actress" or "acting" or "cast" or "star" or "starring" or "self"
                => (CreditKind.Cast, []),
            "director" or "directed" or "directing" or "filmmaker"
                => (CreditKind.Crew, ["Director"]),
            "writer" or "writing" or "written" or "screenwriter" or "screenplay" or "author"
                => (CreditKind.Crew, ["Writer", "Screenplay", "Story", "Author"]),
            "producer" or "produced" or "producing"
                => (CreditKind.Crew, ["Producer", "Executive Producer", "Co-Producer", "Associate Producer"]),
            "composer" or "music"
                => (CreditKind.Crew, ["Original Music Composer", "Composer", "Music"]),
            "cinematographer" or "cinematography"
                => (CreditKind.Crew, ["Director of Photography", "Cinematography"]),
            "editor" or "editing"
                => (CreditKind.Crew, ["Editor", "Film Editor"]),
            _ => (CreditKind.Crew, [job.Trim()]),
        };
    }

    private static bool WithinYears(DateTime? releaseDate, string? fromYear, string? toYear)
    {
        if (releaseDate is not { } date)
        {
            return string.IsNullOrEmpty(fromYear) && string.IsNullOrEmpty(toYear);
        }

        if (int.TryParse(fromYear, out int from) && date.Year < from)
        {
            return false;
        }
        if (int.TryParse(toYear, out int to) && date.Year > to)
        {
            return false;
        }

        return true;
    }

    [Description("Search for movies by their title and release year. Use this to find movies, you can search by movie name or part of a movie name")]
    [return: Description("a json list of movies with the following properties MovieId, MovieName, ReleaseDate, and ImdbId")]
    public async Task<string> SearchMovies(
        [Description("The title of the movie, or part of the title")] string movieTitle,
        [Description("Optional: The year the movie was released")] string? releaseYear = null)
    {
        _ = int.TryParse(releaseYear, out int yearAsInt);
        SearchContainer<SearchMovie> searchResults = await client.SearchMovieAsync(movieTitle, year: yearAsInt);

        List<MovieSearchResult> movieSearchResults = await ToSearchResultsAsync(
            searchResults.Results,
            movie => movie.Id,
            movie => movie.Title,
            movie => movie.ReleaseDate);

        return JsonSerializer.Serialize(movieSearchResults);
    }

    [Description("Get movie trailers, teasers, video clips, behind-the-scenes content, and interviews for a specific movie. Use this when users ask to 'show trailer', 'play trailer', 'watch video', 'preview movie', 'see teaser', 'video content', 'behind-the-scenes', or any video-related requests for a movie.")]
    [return: Description("JSON object containing all available video content including trailers, teasers, clips, and behind-the-scenes footage")]
    public async Task<string> GetMovieTrailers(
        [Description("The TMDb movie ID")] string movieId)
    {
        if (!TryGetTmdbId(movieId, out int tmdbId, out string idError))
        {
            return idError;
        }

        Movie movie = await client.GetMovieAsync(tmdbId, MovieMethods.Videos);

        List<object> allVideos = movie.Videos.Results
            .Where(v => v.Site == "YouTube")
            .Select(v => (object)new
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

        List<Video> trailers = movie.Videos.Results.Where(v => v.Site == "YouTube" && v.Type == "Trailer").ToList();
        List<Video> teasers = movie.Videos.Results.Where(v => v.Site == "YouTube" && v.Type == "Teaser").ToList();
        List<Video> clips = movie.Videos.Results.Where(v => v.Site == "YouTube" && v.Type == "Clip").ToList();
        List<Video> behindScenes = movie.Videos.Results.Where(v => v.Site == "YouTube" && v.Type == "Behind the Scenes").ToList();
        List<Video> featurettes = movie.Videos.Results.Where(v => v.Site == "YouTube" && v.Type == "Featurette").ToList();

        Video? mainTrailerVideo = trailers.FirstOrDefault(t => t.Official) ?? trailers.FirstOrDefault() ?? movie.Videos.Results.FirstOrDefault(v => v.Site == "YouTube");

        return JsonSerializer.Serialize(new
        {
            MovieTitle = movie.Title,
            MovieYear = movie.ReleaseDate?.Year,
            TotalVideoCount = allVideos.Count,
            MainTrailer = mainTrailerVideo == null ? null : new
            {
                Name = mainTrailerVideo.Name,
                Type = mainTrailerVideo.Type,
                Key = mainTrailerVideo.Key,
                YouTubeUrl = $"https://www.youtube.com/watch?v={mainTrailerVideo.Key}",
                EmbedUrl = $"https://www.youtube.com/embed/{mainTrailerVideo.Key}",
                ThumbnailUrl = $"https://img.youtube.com/vi/{mainTrailerVideo.Key}/maxresdefault.jpg",
                Official = mainTrailerVideo.Official
            },
            Videos = new
            {
                Trailers = ProjectVideos(trailers),
                Teasers = ProjectVideos(teasers),
                Clips = ProjectVideos(clips),
                BehindTheScenes = ProjectVideos(behindScenes),
                Featurettes = ProjectVideos(featurettes)
            },
            Summary = allVideos.Count > 0
                ? $"Found {allVideos.Count} video(s) for {movie.Title} including trailers, clips, and behind-the-scenes content"
                : $"No video content available for {movie.Title}"
        });
    }

    private static List<object> ProjectVideos(IEnumerable<Video> videos)
    {
        return videos.Select(v => (object)new
        {
            Name = v.Name,
            Type = v.Type,
            Key = v.Key,
            YouTubeUrl = $"https://www.youtube.com/watch?v={v.Key}",
            EmbedUrl = $"https://www.youtube.com/embed/{v.Key}",
            ThumbnailUrl = $"https://img.youtube.com/vi/{v.Key}/maxresdefault.jpg",
            Official = v.Official
        }).ToList();
    }

    [Description("Search for keywords related to movies.")]
    [return: Description("A JSON list of keywords with their properties such as KeywordId and Name.")]
    public async Task<string> SearchKeywords(
        [Description("The name or partial name of the keyword")] string keyword)
    {
        SearchContainer<SearchKeyword> keywords = await client.SearchKeywordAsync(keyword);
        List<object> keywordList = keywords.Results.Select(k => (object)new { KeywordId = k.Id, Name = k.Name }).ToList();
        return JsonSerializer.Serialize(keywordList);
    }

    [Description("Get full details for one movie by its TMDb MovieId: overview, release date, genres " +
                 "(with ids), runtime, tagline, language, top-billed cast, poster art, worldwide box " +
                 "office and budget, and the ImdbId. This is the single movie-detail tool - use it " +
                 "whenever you need more about a film than a search result carries, including 'how " +
                 "much did it make' and 'who was in it'. For the rating, call GetMovieRating with the " +
                 "ImdbId this returns.")]
    [return: Description("Serialized JSON containing information about a specific movie including ImdbId")]
    public async Task<string> DescribeMovie(
        [Description("The movie ID of a specific movie")] string movieId)
    {
        if (!TryGetTmdbId(movieId, out int tmdbId, out string idError))
        {
            return idError;
        }

        Movie movie = await client.GetMovieAsync(tmdbId, MovieMethods.Credits);

        object movieData = new
        {
            MovieId = movieId,
            Title = movie.Title,
            Overview = movie.Overview,
            ReleaseDate = movie.ReleaseDate?.ToString("yyyy-MM-dd"),
            Genres = movie.Genres.Select(g => new { Id = g.Id, Name = g.Name }).ToList(),
            Runtime = movie.Runtime,
            Tagline = movie.Tagline,
            TmdbVoteAverage = movie.VoteAverage,
            TmdbVoteCount = movie.VoteCount,
            Language = movie.OriginalLanguage,
            ImdbId = movie.ImdbId ?? "",
            PosterPath = movie.PosterPath,
            BackdropPath = movie.BackdropPath,
            WorldwideRevenueUsd = movie.Revenue,
            BudgetUsd = movie.Budget,
            Cast = movie.Credits?.Cast?.Take(5).Select(c => c.Name).ToList()
        };

        return JsonSerializer.Serialize(movieData);
    }

    private static DiscoverMovieSortBy? ParseSortBy(string? sortBy)
    {
        return sortBy?.Trim().ToLowerInvariant() switch
        {
            "popularity" => DiscoverMovieSortBy.PopularityDesc,
            "rating" or "vote_average" => DiscoverMovieSortBy.VoteAverageDesc,
            "newest" or "release_date" => DiscoverMovieSortBy.PrimaryReleaseDateDesc,
            "oldest" => DiscoverMovieSortBy.PrimaryReleaseDate,
            "revenue" => DiscoverMovieSortBy.RevenueDesc,
            "votes" or "vote_count" => DiscoverMovieSortBy.VoteCountDesc,
            _ => null,
        };
    }

    [Description("Discover movies matching filters, optionally sorted. This is the tool for " +
                 "'best', 'top rated', 'most popular', 'highest grossing' and 'newest' questions - " +
                 "pass sortBy rather than sorting the results yourself.")]
    [return: Description("A JSON list of movies with their properties such as MovieId, MovieName, ReleaseDate, and ImdbId.")]
    public async Task<string> DiscoverMovies(
        [Description("Optional: Start release date (YYYY-MM-DD)")] string? releaseDateFrom = null,
        [Description("Optional: End release date (YYYY-MM-DD)")] string? releaseDateTo = null,
        [Description("Optional: Include movies these people ACTED in, as comma-separated PersonIds. " +
                     "Actors only - a director will not match here.")] string? castIds = null,
        [Description("Optional: Include movies these people worked on BEHIND the camera in ANY role - " +
                     "director, writer, producer, composer, editor - as comma-separated PersonIds. " +
                     "This cannot distinguish those roles: it matches any crew credit, including " +
                     "'Thanks'. For 'directed by' or 'written by' specifically, use " +
                     "GetPersonMovieCredits with a job filter instead. Use this one only to combine a " +
                     "behind-the-camera person with other filters like genre or date.")] string? crewIds = null,
        [Description("Optional: Include movies with these genre IDs (comma-separated)")] string? genreIds = null,
        [Description("Optional: Include movies with these keyword IDs (comma-separated)")] string? keywordIds = null,
        [Description("Optional: Minimum vote average (1-10)")] double? minVoteAverage = null,
        [Description("Optional: Maximum vote average (1-10)")] double? maxVoteAverage = null,
        [Description("Optional: Minimum vote count")] int? minVoteCount = null,
        [Description("Optional: Maximum vote count")] int? maxVoteCount = null,
        [Description("Optional: How to order the results. One of 'popularity', 'rating', 'newest', " +
                     "'oldest', 'revenue', 'votes'. Use 'rating' for 'best' or 'top rated' - it applies " +
                     "a minimum vote count so obscure films with one perfect vote do not win. Note this " +
                     "orders by the TMDb vote average, which is not the IMDb rating: to answer a rating " +
                     "question, still call GetMovieRating or CompareMovieRatings on the results.")] string? sortBy = null,
        [Description("Optional: 'all' (default) requires a movie to match every genre id supplied; " +
                     "'any' matches at least one. Use 'any' for 'action or comedy'.")] string? genreMatch = null,
        [Description("Optional: US content rating ceiling - 'G', 'PG', 'PG-13', 'R' or 'NC-17'. " +
                     "Returns only films rated at or below it. Use this for anything about kids, " +
                     "families or age-appropriateness; do not judge a film's rating yourself.")] string? maxCertification = null,
        [Description("Optional: exact US content rating, e.g. 'PG-13'. Prefer maxCertification unless " +
                     "the user asked for precisely one rating.")] string? certification = null,
        [Description("Optional: longest runtime in minutes. Use this for 'under two hours' or " +
                     "'something short' instead of fetching each film to check.")] int? maxRuntimeMinutes = null,
        [Description("Optional: shortest runtime in minutes, for 'a proper long epic'.")] int? minRuntimeMinutes = null)
    {
        DiscoverMovie query = client.DiscoverMoviesAsync();

        DiscoverMovieSortBy? sort = ParseSortBy(sortBy);
        if (sort.HasValue)
        {
            query = query.OrderBy(sort.Value);

            if (sort.Value == DiscoverMovieSortBy.VoteAverageDesc && !minVoteCount.HasValue)
            {
                minVoteCount = RatingSortMinimumVotes;
            }
        }

        if (!string.IsNullOrEmpty(releaseDateFrom))
        {
            DateTime releaseDate = DateTime.Parse(releaseDateFrom);
            query = query.WherePrimaryReleaseDateIsAfter(releaseDate);
        }

        if (!string.IsNullOrEmpty(releaseDateTo))
        {
            DateTime releaseDate = DateTime.Parse(releaseDateTo);
            query = query.WherePrimaryReleaseDateIsBefore(releaseDate);
        }

        if (!string.IsNullOrEmpty(castIds))
        {
            List<int> castIdList = ParseIdList(castIds);
            if (castIdList.Count > 0)
            {
                query = query.IncludeWithAllOfCast(castIdList);
            }
        }

        if (!string.IsNullOrEmpty(crewIds))
        {
            List<int> crewIdList = ParseIdList(crewIds);
            if (crewIdList.Count > 0)
            {
                query = query.IncludeWithAllOfCrew(crewIdList);
            }
        }

        if (!string.IsNullOrEmpty(genreIds))
        {
            List<int> genreIdList = ParseIdList(genreIds);
            if (genreIdList.Count > 0)
            {
                query = string.Equals(genreMatch?.Trim(), "any", StringComparison.OrdinalIgnoreCase)
                    ? query.IncludeWithAnyOfGenre(genreIdList)
                    : query.IncludeWithAllOfGenre(genreIdList);
            }
        }

        if (!string.IsNullOrEmpty(keywordIds))
        {
            List<int> keywordIdList = ParseIdList(keywordIds);
            if (keywordIdList.Count > 0)
            {
                query = query.IncludeWithAllOfKeywords(keywordIdList);
            }
        }

        if (!string.IsNullOrWhiteSpace(maxCertification))
        {
            query = query.WhereCertificationIsAtMost(CertificationCountry, maxCertification.Trim().ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(certification))
        {
            query = query.WhereCertificationIs(CertificationCountry, certification.Trim().ToUpperInvariant());
        }

        if (maxRuntimeMinutes is > 0)
        {
            query = query.WhereRuntimeIsAtMost(maxRuntimeMinutes.Value);
        }

        if (minRuntimeMinutes is > 0)
        {
            query = query.WhereRuntimeIsAtLeast(minRuntimeMinutes.Value);
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

        SearchContainer<SearchMovie> searchResults = await query.Query();
        bool wantsRuntimeFilter = maxRuntimeMinutes is > 0 || minRuntimeMinutes is > 0;

        if (searchResults.Results.Count == 0
            && !string.IsNullOrEmpty(castIds)
            && string.IsNullOrEmpty(crewIds))
        {
            return JsonSerializer.Serialize(new
            {
                Results = Array.Empty<MovieSearchResult>(),
                Hint = "No movies matched those cast ids. If the person is a director, writer or "
                     + "producer rather than an actor, pass their PersonId as crewIds instead of "
                     + "castIds and try again. Do not answer from memory."
            });
        }

        if (wantsRuntimeFilter)
        {
            Dictionary<int, MovieFacts> runtimeFacts = await ResolveMovieFactsAsync(searchResults.Results.Select(movie => movie.Id));

            List<MovieSearchResult> verified = searchResults.Results
                .Select(movie => (Movie: movie, Runtime: runtimeFacts.TryGetValue(movie.Id, out MovieFacts? f) ? f.Runtime : null))
                .Where(entry => entry.Runtime is > 0
                    && (maxRuntimeMinutes is not > 0 || entry.Runtime <= maxRuntimeMinutes)
                    && (minRuntimeMinutes is not > 0 || entry.Runtime >= minRuntimeMinutes))
                .Select(entry => new MovieSearchResult(
                    entry.Movie.Id.ToString(),
                    entry.Movie.Title ?? "",
                    entry.Movie.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
                    runtimeFacts.TryGetValue(entry.Movie.Id, out MovieFacts? found) ? found.ImdbId : "",
                    entry.Runtime))
                .ToList();

            return JsonSerializer.Serialize(new
            {
                Results = verified,
                Note = $"{verified.Count} of {searchResults.Results.Count} results actually satisfy the "
                     + "runtime filter; TMDb's own runtime filter is unreliable, so the rest were "
                     + "verified and dropped here. RuntimeMinutes is the checked value.",
            });
        }

        List<MovieSearchResult> movieList = await ToSearchResultsAsync(
            searchResults.Results,
            movie => movie.Id,
            movie => movie.Title,
            movie => movie.ReleaseDate);

        return JsonSerializer.Serialize(movieList);
    }
}
