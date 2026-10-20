using System.Text.Json.Serialization;

namespace MovieTracker.Api.Agents;

/// <summary>Deserialization shape for Wikipedia's REST summary payload. The lowercase JsonPropertyName attributes are required because the API returns lowercase keys.</summary>
public class WikipediaSummary
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("extract")]
    public string? Extract { get; set; }

    [JsonPropertyName("thumbnail")]
    public WikipediaThumbnail? Thumbnail { get; set; }
}
