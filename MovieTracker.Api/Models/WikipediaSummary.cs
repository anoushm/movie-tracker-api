using System.Text.Json.Serialization;

namespace MovieTracker.Api.Models;

public class WikipediaSummary
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("extract")]
    public string? Extract { get; set; }

    [JsonPropertyName("thumbnail")]
    public WikipediaThumbnail? Thumbnail { get; set; }
}
