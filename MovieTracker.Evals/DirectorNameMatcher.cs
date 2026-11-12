using System;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public static class DirectorNameMatcher
{
    public static EvalCheckResult Evaluate(string checkName, EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected director", checkName);
        }

        DirectorExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<DirectorExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", checkName);
        }

        if (expected is null || string.IsNullOrEmpty(expected.DirectorName))
        {
            return new EvalCheckResult(false, "no expected director", checkName);
        }

        string answer = item.Response ?? string.Empty;
        if (answer.IndexOf(expected.DirectorName, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return new EvalCheckResult(false, $"missing director '{expected.DirectorName}'", checkName);
        }

        return new EvalCheckResult(true, "ok", checkName);
    }
}
