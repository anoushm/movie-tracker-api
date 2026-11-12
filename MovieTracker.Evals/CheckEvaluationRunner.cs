using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MovieTracker.Evals;

public sealed class CheckEvaluationRunner
{
    private readonly IReadOnlyDictionary<string, IEvalCheck> discovery;

    public CheckEvaluationRunner(IReadOnlyDictionary<string, IEvalCheck> discovery)
    {
        this.discovery = discovery;
    }

    public async Task<IReadOnlyList<CaseResult>> EvaluateGroupAsync(
        string checkName,
        IReadOnlyList<(string CaseId, EvalItem Item)> group)
    {
        if (!this.discovery.TryGetValue(checkName, out IEvalCheck? check))
        {
            List<CaseResult> missing = new List<CaseResult>(group.Count);
            foreach ((string caseId, EvalItem _) in group)
            {
                missing.Add(new CaseResult(caseId, checkName, false, $"no IEvalCheck registered for '{checkName}'"));
            }

            return missing;
        }

        EvalCheck wrappedCheck = FunctionEvaluator.Create(checkName, check.Evaluate);
        LocalEvaluator localEvaluator = new LocalEvaluator(wrappedCheck);

        List<EvalItem> items = group.Select(pair => pair.Item).ToList();
        AgentEvaluationResults evalResults = await localEvaluator.EvaluateAsync(items, checkName, CancellationToken.None);

        List<CaseResult> output = new List<CaseResult>(group.Count);
        for (int index = 0; index < group.Count; index++)
        {
            (string caseId, EvalItem _) = group[index];

            if (index >= evalResults.Items.Count)
            {
                output.Add(new CaseResult(caseId, checkName, false, "evaluator returned no result"));
                continue;
            }

            EvaluationResult evaluationResult = evalResults.Items[index];
            if (!evaluationResult.Metrics.TryGetValue(checkName, out EvaluationMetric? metric))
            {
                output.Add(new CaseResult(caseId, checkName, false, $"evaluator result missing metric '{checkName}'"));
                continue;
            }

            if (metric is not BooleanMetric boolean)
            {
                output.Add(new CaseResult(caseId, checkName, false, $"evaluator metric '{checkName}' is not a BooleanMetric"));
                continue;
            }

            bool passed = boolean.Value ?? false;
            string reason;
            if (!string.IsNullOrEmpty(boolean.Reason))
            {
                reason = boolean.Reason;
            }
            else if (passed)
            {
                reason = "ok";
            }
            else
            {
                reason = "fail";
            }

            output.Add(new CaseResult(caseId, checkName, passed, reason));
        }

        return output;
    }
}
