using System.Text.Json;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;

namespace MovieTracker.Api.Agents;

/// <summary>Resolves a movie title to its best official YouTube trailer via TMDb and returns a `[TRAILER]`-wrapped embed string.</summary>
public class TrailerAgent(TMDbClient tmdbClient)
{
    public async Task<string> GetTrailerForTitle(string movieTitle)
    {
        string? title = movieTitle?.Trim();

        if (string.IsNullOrWhiteSpace(title) || title.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                Error = "No movie title was supplied.",
                Hint = "Pass the film's title. If the user referred to it indirectly - 'that one', "
                     + "'it' - use the title from earlier in the conversation; this tool cannot see it."
            });
        }

        SearchContainer<SearchMovie> searchResults = await tmdbClient.SearchMovieAsync(title);
        SearchMovie? match = searchResults.Results.FirstOrDefault();

        if (match is null)
        {
            return JsonSerializer.Serialize(new
            {
                Error = $"No movie found matching '{title}'.",
                Hint = "Check the title, or call SearchMovies to find the right one first."
            });
        }

        ResultContainer<Video> videos = await tmdbClient.GetMovieVideosAsync(match.Id);

        Video? trailer = videos.Results
            .Where(video => video.Type == "Trailer" && video.Site == "YouTube")
            .OrderByDescending(video => video.Official)
            .ThenByDescending(video => video.Size)
            .FirstOrDefault();

        if (trailer is null)
        {
            return JsonSerializer.Serialize(new
            {
                MovieId = match.Id.ToString(),
                MovieName = match.Title,
                Error = $"No YouTube trailer is available for '{match.Title}'.",
                Hint = "Say so plainly rather than substituting a different film's trailer."
            });
        }

        return JsonSerializer.Serialize(new
        {
            MovieId = match.Id.ToString(),
            MovieName = match.Title,
            ReleaseDate = match.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
            TrailerName = trailer.Name,
            EmbedInSystemMessage = $"[TRAILER]https://www.youtube.com/watch?v={trailer.Key}[/TRAILER]"
        });
    }
}
