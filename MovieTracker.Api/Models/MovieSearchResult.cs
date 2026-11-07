namespace MovieTracker.Api.Models;

public record MovieSearchResult(
    string MovieId,
    string MovieName,
    string ReleaseDate,
    string ImdbId,
    int? RuntimeMinutes = null);
