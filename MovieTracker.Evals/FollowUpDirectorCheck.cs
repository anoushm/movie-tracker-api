using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class FollowUpDirectorCheck : IEvalCheck
{
    public string Name => "followUpDirector";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        return DirectorNameMatcher.Evaluate(this.Name, item);
    }
}
