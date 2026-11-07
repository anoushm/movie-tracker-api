using System.Collections.Generic;

namespace MovieTracker.Api.Models;

public record RatingComparisonResult(
    string HighestRatedTitle,
    string HighestRating,
    List<MovieRatingResult> AllMovies,
    bool IsSuccess,
    string ErrorMessage = ""
);
