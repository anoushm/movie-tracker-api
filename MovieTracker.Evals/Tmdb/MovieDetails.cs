using System.Collections.Generic;

namespace MovieTracker.Evals.Tmdb;

public sealed record MovieDetails(
    string? ReleaseDate,
    string? ImdbId,
    string? Overview,
    IReadOnlyList<MovieGenre> Genres);
