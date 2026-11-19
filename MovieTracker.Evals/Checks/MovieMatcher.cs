using System;
using MovieTracker.Evals.Agent;

namespace MovieTracker.Evals.Checks;

public static class MovieMatcher
{
    public static bool MovieInAnswer(int? expectedId, string expectedTitle, ActualResponsePayload? actual, string answer)
    {
        if (expectedId is not null && actual is not null && actual.MovieList is not null)
        {
            foreach (MovieListEntry entry in actual.MovieList)
            {
                if (entry.Id == expectedId.Value)
                {
                    return true;
                }
            }
        }

        if (string.IsNullOrEmpty(expectedTitle))
        {
            return false;
        }

        if (actual is not null && actual.MovieList is not null)
        {
            foreach (MovieListEntry entry in actual.MovieList)
            {
                if (string.Equals(entry.Title, expectedTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrEmpty(answer)
            && answer.IndexOf(expectedTitle, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }
}
