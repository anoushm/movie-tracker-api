using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class FunnyFactNotEmptyCheck : IEvalCheck
{
    public string Name => "funnyFactNotEmpty";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.Context))
        {
            return new EvalCheckResult(false, "funnyFact missing (no context)", this.Name);
        }

        ActualResponsePayload? actual;
        try
        {
            actual = JsonSerializer.Deserialize<ActualResponsePayload>(item.Context);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse Context: {ex.Message}", this.Name);
        }

        if (actual is null || string.IsNullOrWhiteSpace(actual.FunnyFact))
        {
            return new EvalCheckResult(false, "funnyFact null or empty", this.Name);
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
