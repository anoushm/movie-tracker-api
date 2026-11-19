using System;
using System.Collections.Generic;

namespace MovieTracker.Evals.Reporting;

public sealed record RunResult(
    DateTimeOffset RunAt,
    int Total,
    int Passed,
    IReadOnlyList<PerCheckCount> PerCheck,
    IReadOnlyList<CaseResult> Cases,
    int Skipped);
