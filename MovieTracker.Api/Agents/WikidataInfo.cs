namespace MovieTracker.Api.Agents;

public class WikidataInfo
{
    public Dictionary<string, object>? StructuredFacts { get; set; }
    public List<string>? RelatedEntities { get; set; }
}
