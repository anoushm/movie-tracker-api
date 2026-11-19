using Microsoft.Agents.AI;

namespace MovieTracker.Evals.Checks;

public sealed class FollowUpDirectorCheck : IEvalCheck
{
    public const string CheckName = "followUpDirector";

    public string Name => CheckName;

    public EvalCheckResult Evaluate(EvalItem item)
    {
        return DirectorNameMatcher.Evaluate(this.Name, item);
    }
}
