using System.Collections.Generic;

namespace MovieTracker.Api.Models;

public class WikidataResults
{
    public List<Dictionary<string, WikidataValue>>? Bindings { get; set; }
}
