namespace MovieTracker.Evals.Reporting;

public sealed record CheckComparison(
    string Check,
    int? PreviousPassed,
    int? PreviousTotal,
    int? CurrentPassed,
    int? CurrentTotal,
    int? CurrentSkipped);
