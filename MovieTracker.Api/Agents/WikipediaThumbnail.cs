using System.Text.Json.Serialization;

namespace MovieTracker.Api.Agents;

public class WikipediaThumbnail
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
