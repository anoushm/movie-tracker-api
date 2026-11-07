using System.Collections.Generic;

namespace MovieTracker.Api.Models;

public class WikidataInfo
{
    public Dictionary<string, object>? StructuredFacts { get; set; }
    public List<string>? RelatedEntities { get; set; }
}
