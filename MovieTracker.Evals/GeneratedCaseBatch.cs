using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record GeneratedCaseBatch(
    IReadOnlyList<GeneratedCase> Cases,
    int TrailerProducedCount);
