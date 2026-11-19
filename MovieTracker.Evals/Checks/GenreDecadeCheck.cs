using System;
using System.Text.Json;
using Microsoft.Agents.AI;
using MovieTracker.Evals.Agent;
using MovieTracker.Evals.Cases;

namespace MovieTracker.Evals.Checks;

public sealed class GenreDecadeCheck : IEvalCheck
{
    public const string CheckName = "genreDecade";

    public string Name => CheckName;

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected genre/decade", this.Name);
        }

        GenreDecadeExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<GenreDecadeExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || string.IsNullOrEmpty(expected.GenreName))
        {
            return new EvalCheckResult(false, "no expected genre/decade", this.Name);
        }

        ActualResponsePayload? actual = ActualResponseParser.Parse(item.Context);
        if (actual is null || actual.MovieList is null || actual.MovieList.Count == 0)
        {
            return new EvalCheckResult(false, "movieList is empty", this.Name);
        }

        int decadeStart = expected.DecadeStart;
        int decadeEnd = decadeStart + 9;

        foreach (MovieListEntry entry in actual.MovieList)
        {
            bool genreMatches = false;
            if (entry.GenreIds is not null)
            {
                foreach (int genreId in entry.GenreIds)
                {
                    if (genreId == expected.GenreId)
                    {
                        genreMatches = true;
                        break;
                    }
                }
            }

            if (!genreMatches && entry.GenreNames is not null)
            {
                foreach (string genreName in entry.GenreNames)
                {
                    if (string.Equals(genreName, expected.GenreName, StringComparison.OrdinalIgnoreCase))
                    {
                        genreMatches = true;
                        break;
                    }
                }
            }

            if (!genreMatches)
            {
                return new EvalCheckResult(false, $"movie '{entry.Title}' is not in genre '{expected.GenreName}'", this.Name);
            }

            int? year = ReleaseYearExtractor.Extract(entry.ReleaseDate);
            if (year is null || year.Value < decadeStart || year.Value > decadeEnd)
            {
                return new EvalCheckResult(false, $"movie '{entry.Title}' releaseDate '{entry.ReleaseDate}' outside {decadeStart}..{decadeEnd}", this.Name);
            }
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
