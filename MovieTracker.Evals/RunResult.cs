using System;
using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record RunResult(
    DateTimeOffset RunAt,
    int Total,
    int Passed,
    IReadOnlyList<PerCheckCount> PerCheck,
    IReadOnlyList<CaseResult> Cases);
