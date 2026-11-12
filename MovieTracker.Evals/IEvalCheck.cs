using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public interface IEvalCheck
{
    string Name { get; }

    EvalCheckResult Evaluate(EvalItem item);
}
