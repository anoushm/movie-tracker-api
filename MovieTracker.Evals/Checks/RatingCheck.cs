using System;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals.Checks;

public sealed class RatingCheck : IEvalCheck
{
    public const string CheckName = "rating";

    public string Name => CheckName;

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected rating", this.Name);
        }

        RatingExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<RatingExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || string.IsNullOrEmpty(expected.ImdbRating))
        {
            return new EvalCheckResult(false, "no expected rating", this.Name);
        }

        string answer = item.Response ?? string.Empty;
        if (answer.IndexOf(expected.ImdbRating, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return new EvalCheckResult(false, $"missing rating '{expected.ImdbRating}'", this.Name);
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
