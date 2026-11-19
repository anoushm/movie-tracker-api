using System.Collections.Generic;

namespace MovieTracker.Evals.Agent;

public sealed record MovieListEntry(
    int Id,
    string Title,
    string? ReleaseDate,
    IReadOnlyList<int> GenreIds,
    IReadOnlyList<string> GenreNames);
