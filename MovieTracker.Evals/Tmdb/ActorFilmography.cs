using System.Collections.Generic;

namespace MovieTracker.Evals.Tmdb;

public sealed record ActorFilmography(
    IReadOnlyList<int> MovieIds,
    IReadOnlyList<string> MovieTitles);
