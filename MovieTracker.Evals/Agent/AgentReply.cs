using System.Collections.Generic;

namespace MovieTracker.Evals.Agent;

public sealed record AgentReply(
    string Answer,
    string? FunnyFact,
    IReadOnlyList<MovieListEntry> MovieList);
