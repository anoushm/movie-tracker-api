using Microsoft.Extensions.Configuration;

namespace MovieTracker.Evals;

public static class ConfigChecker
{
    public static string? FindMissingKey(IConfiguration configuration)
    {
        string? tmdbKey = configuration["TheMovieDb:Api-Key"];
        if (string.IsNullOrWhiteSpace(tmdbKey))
        {
            return "TheMovieDb:Api-Key";
        }

        string? omdbKey = configuration["OpenMovieDb:Api-Key"];
        if (string.IsNullOrWhiteSpace(omdbKey))
        {
            return "OpenMovieDb:Api-Key";
        }

        return null;
    }
}
