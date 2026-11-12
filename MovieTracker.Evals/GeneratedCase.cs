using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record GeneratedCase(
    string Id,
    string Question,
    IReadOnlyList<string>? Turns,
    TemplateCheck Check,
    string? ExpectedJson,
    bool NoTruthValue);
