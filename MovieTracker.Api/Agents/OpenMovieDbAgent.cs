using System.Globalization;
using OMDbApiNet;
using OMDbApiNet.Model;

namespace MovieTracker.Api.Agents;

public class OpenMovieDbAgent
{
    private readonly AsyncOmdbClient omdbClient;

    public OpenMovieDbAgent(IConfiguration configuration)
    {
        string apiKey = configuration["OpenMovieDb:Api-Key"] ?? throw new ArgumentNullException("Missing OMDb Api Key");

        omdbClient = new AsyncOmdbClient(apiKey);
    }

    public async Task<MovieRatingResult> GetMovieRatings(string imdbId)
    {
        try
        {
            Item movie = await omdbClient.GetItemByIdAsync(imdbId);

            if (movie.Response == "False")
            {
                return new MovieRatingResult("", "", "", "", "", "", false, $"Movie not found for IMDb ID: {imdbId}");
            }

            List<Rating> ratings = movie.Ratings;

            return new MovieRatingResult(
                Title: movie.Title ?? "",
                Year: movie.Year ?? "",
                ImdbRating: movie.ImdbRating ?? "N/A",
                RottenTomatoesRating: GetRatingBySource(ratings, "Rotten Tomatoes"),
                MetacriticRating: GetRatingBySource(ratings, "Metacritic"),
                BoxOffice: movie.BoxOffice ?? "N/A",
                IsSuccess: true,
                Rated: movie.Rated ?? "",
                Runtime: movie.Runtime ?? "",
                Genre: movie.Genre ?? "",
                Director: movie.Director ?? "",
                Writer: movie.Writer ?? "",
                Actors: movie.Actors ?? "",
                Awards: movie.Awards ?? "",
                Plot: movie.Plot ?? "",
                ImdbVotes: movie.ImdbVotes ?? ""
            );
        }
        catch (Exception ex)
        {
            return new MovieRatingResult("", "", "", "", "", "", false, $"Error: {ex.Message}");
        }
    }

    /// <summary>Fans out one OMDb round trip per id via Task.WhenAll; positional order is preserved.</summary>
    private static async Task<List<MovieRatingResult>> GetManyAsync(
        List<string> imdbIds,
        Func<string, Task<MovieRatingResult>> lookup) =>
        [.. await Task.WhenAll(imdbIds.Select(lookup))];

    public async Task<RatingComparisonResult> CompareMovieRatings(List<string> imdbIds)
    {
        try
        {
            List<MovieRatingResult> movieRatings = (await GetManyAsync(imdbIds, GetMovieRatings))
                .Where(rating => rating.IsSuccess)
                .ToList();

            if (!movieRatings.Any())
            {
                return new RatingComparisonResult("", "", new List<MovieRatingResult>(), false, "No valid movies found for comparison");
            }

            MovieRatingResult? highest = movieRatings
                .Where(m => double.TryParse(m.ImdbRating, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .OrderByDescending(m => double.Parse(m.ImdbRating, CultureInfo.InvariantCulture))
                .FirstOrDefault();

            if (highest == null)
            {
                return new RatingComparisonResult("", "", movieRatings, false, "No movies with valid IMDb ratings found");
            }

            return new RatingComparisonResult(
                HighestRatedTitle: $"{highest.Title} ({highest.Year})",
                HighestRating: highest.ImdbRating,
                AllMovies: movieRatings,
                IsSuccess: true
            );
        }
        catch (Exception ex)
        {
            return new RatingComparisonResult("", "", new List<MovieRatingResult>(), false, $"Error: {ex.Message}");
        }
    }

    public async Task<List<MovieRatingResult>> FilterMoviesByRating(List<string> imdbIds, double minimumRating)
    {
        List<MovieRatingResult> ratings = await GetManyAsync(imdbIds, GetMovieRatings);

        return [.. ratings.Where(rating =>
            rating.IsSuccess
            && double.TryParse(rating.ImdbRating, NumberStyles.Any, CultureInfo.InvariantCulture, out double actualRating)
            && actualRating >= minimumRating)];
    }

    private static string GetRatingBySource(List<Rating> ratings, string sourceName)
    {
        if (ratings == null)
        {
            return "N/A";
        }

        Rating? rating = ratings.FirstOrDefault(r => r.Source.Contains(sourceName, StringComparison.OrdinalIgnoreCase));
        return rating?.Value ?? "N/A";
    }
}
