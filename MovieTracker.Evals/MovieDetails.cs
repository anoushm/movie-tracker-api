using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record MovieDetails(
    string? ReleaseDate,
    string? ImdbId,
    string? Overview,
    IReadOnlyList<MovieGenre> Genres);
