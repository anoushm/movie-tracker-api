using System.Collections.Generic;
using MovieTracker.Evals.Agent;

namespace MovieTracker.Evals.Checks;

public sealed record ActualResponsePayload(
    string? FunnyFact,
    IReadOnlyList<MovieListEntry> MovieList);
