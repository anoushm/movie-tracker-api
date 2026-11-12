namespace MovieTracker.Evals;

public sealed record CheckComparison(
    string Check,
    int? PreviousPassed,
    int? PreviousTotal,
    int? CurrentPassed,
    int? CurrentTotal);
