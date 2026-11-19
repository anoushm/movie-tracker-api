using System.Collections.Generic;

namespace MovieTracker.Evals.Checks;

public sealed record ActorFilmographyExpected(
    string ActorName,
    IReadOnlyList<int> FilmographyIds,
    IReadOnlyList<string> FilmographyTitles);
