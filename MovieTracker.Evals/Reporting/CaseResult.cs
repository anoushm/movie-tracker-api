namespace MovieTracker.Evals.Reporting;

public sealed record CaseResult(string Id, string Check, bool Passed, string Reason)
{
    public bool Skipped { get; init; }
}
