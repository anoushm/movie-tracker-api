using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record AgentReply(
    string Answer,
    string? FunnyFact,
    IReadOnlyList<MovieListEntry> MovieList);
