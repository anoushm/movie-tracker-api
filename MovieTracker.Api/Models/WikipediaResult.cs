namespace MovieTracker.Api.Models;

public class WikipediaResult
{
    public string EntityName { get; set; } = "";
    public WikipediaContent? WikipediaContent { get; set; }
    public WikidataInfo? StructuredData { get; set; }
    public double ConfidenceScore { get; set; }
}
