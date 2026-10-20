using System;
using System.Collections.Generic;

namespace MovieTracker.Api.Chat;

public record MovieViewModel(
    string PosterPath,
    bool Adult,
    string Overview,
    DateTime? ReleaseDate,
    List<int> GenreIds,
    string Id,
    string OriginalTitle,
    string OriginalLanguage,
    string Title,
    string BackdropPath,
    double? Popularity,
    int VoteCount,
    bool Video,
    double VoteAverage,
    bool Favorite,
    string ImdbId,
    MovieTrailerInfo? Trailer,
    List<string>? GenreNames = null,
    int? Runtime = null,
    string? Tagline = null
);
