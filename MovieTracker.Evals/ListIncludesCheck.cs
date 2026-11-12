using System;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class ListIncludesCheck : IEvalCheck
{
    public string Name => "listIncludes";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected titles", this.Name);
        }

        ListIncludesExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<ListIncludesExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || expected.ExpectedTitles is null || expected.ExpectedTitles.Count == 0)
        {
            return new EvalCheckResult(false, "no expected titles", this.Name);
        }

        ActualResponsePayload? actual = ActualResponseParser.Parse(item.Context);
        string answer = item.Response ?? string.Empty;

        foreach (string expectedTitle in expected.ExpectedTitles)
        {
            if (string.IsNullOrEmpty(expectedTitle))
            {
                continue;
            }

            if (MovieMatcher.MovieInAnswer(expectedId: null, expectedTitle: expectedTitle, actual, answer))
            {
                continue;
            }

            return new EvalCheckResult(false, $"missing '{expectedTitle}'", this.Name);
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
