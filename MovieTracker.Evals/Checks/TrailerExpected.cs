using System.Collections.Generic;

namespace MovieTracker.Evals.Checks;

public sealed record TrailerExpected(IReadOnlyList<string> VideoKeys);
