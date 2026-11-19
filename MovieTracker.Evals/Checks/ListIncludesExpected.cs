using System.Collections.Generic;

namespace MovieTracker.Evals.Checks;

public sealed record ListIncludesExpected(IReadOnlyList<string> ExpectedTitles);
