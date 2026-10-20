using MovieTracker.Api.Chat;

namespace MovieTracker.Api.Agents;

/// <summary>Flattened OMDb lookup result for one film: IMDb primary rating plus secondary context.</summary>
public record MovieRatingResult(
    string Title,
    string Year,
    string ImdbRating,
    string RottenTomatoesRating,
    string MetacriticRating,
    string BoxOffice,
    bool IsSuccess,
    string ErrorMessage = "",
    MovieTrailerInfo? Trailer = null,
    string Rated = "",
    string Runtime = "",
    string Genre = "",
    string Director = "",
    string Writer = "",
    string Actors = "",
    string Awards = "",
    string Plot = "",
    string ImdbVotes = ""
);
