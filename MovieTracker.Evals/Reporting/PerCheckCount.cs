namespace MovieTracker.Evals.Reporting;

public sealed record PerCheckCount(string Check, int Passed, int Total, int Skipped = 0);
