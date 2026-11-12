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

        ActualResponsePayload? actual = ActualResponseParser.Parse(item.Context);
        if (actual is null)
        {
            return new EvalCheckResult(false, "could not parse Context", this.Name);
        }

        if (string.IsNullOrWhiteSpace(actual.FunnyFact))
        {
            return new EvalCheckResult(false, "funnyFact null or empty", this.Name);
        }

        return new EvalCheckResult(true, "ok", this.Name);
    }
}
