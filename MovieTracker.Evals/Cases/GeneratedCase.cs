using System.Collections.Generic;

namespace MovieTracker.Evals.Cases;

public sealed record GeneratedCase(
    string Id,
    string Question,
    IReadOnlyList<string>? Turns,
    string Check,
    string? ExpectedJson,
    bool NoTruthValue);
