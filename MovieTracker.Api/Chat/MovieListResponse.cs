using System.Collections.Generic;

namespace MovieTracker.Api.Chat;

public class MovieListResponse
{
    public string SystemMessage { get; set; } = string.Empty;

    public List<MovieListItem> MovieList { get; set; } = new List<MovieListItem>();
}
