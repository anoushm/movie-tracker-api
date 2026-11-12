using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record ActorFilmography(
    IReadOnlyList<int> MovieIds,
    IReadOnlyList<string> MovieTitles);
