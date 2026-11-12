using System;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class DirectorCheck : IEvalCheck
{
    public string Name => "director";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        return DirectorNameMatcher.Evaluate(this.Name, item);
    }
}
