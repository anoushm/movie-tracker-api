using System.Collections.Generic;

namespace MovieTracker.Api.Models;

public class MovieListResponse
{
    public string SystemMessage { get; set; } = string.Empty;

    public List<MovieListItem> MovieList { get; set; } = new List<MovieListItem>();
}
