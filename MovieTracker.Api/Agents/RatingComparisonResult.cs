namespace MovieTracker.Api.Agents;

public record RatingComparisonResult(
    string HighestRatedTitle,
    string HighestRating,
    List<MovieRatingResult> AllMovies,
    bool IsSuccess,
    string ErrorMessage = ""
);
