namespace MovieTracker.Api.Models;

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
