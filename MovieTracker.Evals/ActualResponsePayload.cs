using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record ActualResponsePayload(
    string? FunnyFact,
    IReadOnlyList<MovieListEntry> MovieList);
