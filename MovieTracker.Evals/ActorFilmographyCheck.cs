using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class ActorFilmographyCheck : IEvalCheck
{
    public string Name => "actorFilmography";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected filmography", this.Name);
        }

        ActorFilmographyExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<ActorFilmographyExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || expected.FilmographyIds is null || expected.FilmographyTitles is null)
        {
            return new EvalCheckResult(false, "no expected filmography", this.Name);
        }

        ActualResponsePayload? actual = ActualResponseParser.Parse(item.Context);
        if (actual is null || actual.MovieList is null || actual.MovieList.Count == 0)
        {
            return new EvalCheckResult(false, "movieList is empty", this.Name);
        }

        HashSet<int> filmographyIds = new HashSet<int>(expected.FilmographyIds);
        HashSet<string> filmographyTitles = new HashSet<string>(expected.FilmographyTitles, StringComparer.OrdinalIgnoreCase);

        foreach (MovieListEntry entry in actual.MovieList)
        {
            if (filmographyIds.Contains(entry.Id))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(entry.Title) && filmographyTitles.Contains(entry.Title))
            {
                continue;
            }

            return new EvalCheckResult(false, $"movie '{entry.Title}' (id {entry.Id}) is not in {expected.ActorName}'s filmography", this.Name);
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
