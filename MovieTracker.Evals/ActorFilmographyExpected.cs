using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed record ActorFilmographyExpected(
    string ActorName,
    IReadOnlyList<int> FilmographyIds,
    IReadOnlyList<string> FilmographyTitles);
