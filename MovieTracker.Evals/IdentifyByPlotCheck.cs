using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class IdentifyByPlotCheck : IEvalCheck
{
    public string Name => "identifyByPlot";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected source movie", this.Name);
        }

        IdentifyByPlotExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<IdentifyByPlotExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || string.IsNullOrEmpty(expected.SourceTitle))
        {
            return new EvalCheckResult(false, "no expected source movie", this.Name);
        }

        ActualResponsePayload? actual = ActualResponseParser.Parse(item.Context);
        string answer = item.Response ?? string.Empty;

        if (MovieMatcher.MovieInAnswer(expected.SourceId, expected.SourceTitle, actual, answer))
        {
            return new EvalCheckResult(true, "ok", this.Name);
        }

        return new EvalCheckResult(false, $"source movie '{expected.SourceTitle}' (id {expected.SourceId}) is not in the answer", this.Name);
    }
}
