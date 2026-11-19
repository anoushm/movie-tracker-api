using Microsoft.Agents.AI;

namespace MovieTracker.Evals.Checks;

public interface IEvalCheck
{
    string Name { get; }

    EvalCheckResult Evaluate(EvalItem item);
}
