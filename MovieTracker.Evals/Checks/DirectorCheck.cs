using System;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals.Checks;

public sealed class DirectorCheck : IEvalCheck
{
    public const string CheckName = "director";

    public string Name => CheckName;

    public EvalCheckResult Evaluate(EvalItem item)
    {
        return DirectorNameMatcher.Evaluate(this.Name, item);
    }
}
