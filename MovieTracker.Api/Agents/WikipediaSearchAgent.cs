using System.ComponentModel;
using System.Text.Json;

namespace MovieTracker.Api.Agents;

public class WikipediaSearchAgent
{
    private readonly HttpClient httpClient;
    private readonly ILogger<WikipediaSearchAgent> logger;

    public WikipediaSearchAgent(HttpClient httpClient, ILogger<WikipediaSearchAgent> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    [Description("Gets enhanced movie/actor information from Wikipedia and Wikidata")]
    [return: Description("Rich information including trivia, context, and detailed facts")]
    public async Task<WikipediaResult?> GetEnhancedInfo(
        [Description("Movie title, actor name, or director name")] string entityName,
        [Description("Type: 'movie', 'actor', or 'director'")] string entityType = "movie")
    {
        try
        {
            WikipediaContent? wikipediaData = await SearchWikipedia(entityName);

            WikidataInfo? wikidataInfo = await QueryWikidata(entityName, entityType);

            return new WikipediaResult
            {
                EntityName = entityName,
                WikipediaContent = wikipediaData,
                StructuredData = wikidataInfo,
                ConfidenceScore = CalculateConfidence(wikipediaData, wikidataInfo)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching Wikipedia data for {EntityName}", entityName);
            return null;
        }
    }

    private async Task<WikipediaContent?> SearchWikipedia(string entityName)
    {
        string searchUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(entityName)}";
        HttpResponseMessage response = await httpClient.GetAsync(searchUrl);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string content = await response.Content.ReadAsStringAsync();
        WikipediaSummary? summary = JsonSerializer.Deserialize<WikipediaSummary>(content);

        return new WikipediaContent
        {
            Summary = summary?.Extract,
            Thumbnail = summary?.Thumbnail?.Source
        };
    }

    private async Task<WikidataInfo?> QueryWikidata(string entityName, string entityType)
    {
        string sparqlQuery = entityType.ToLower() switch
        {
            "movie" => BuildMovieQuery(entityName),
            "actor" => BuildActorQuery(entityName),
            "director" => BuildDirectorQuery(entityName),
            _ => BuildMovieQuery(entityName)
        };

        string queryUrl = "https://query.wikidata.org/sparql";
        FormUrlEncodedContent requestContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("query", sparqlQuery),
            new KeyValuePair<string, string>("format", "json")
        });

        HttpResponseMessage response = await httpClient.PostAsync(queryUrl, requestContent);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string jsonResponse = await response.Content.ReadAsStringAsync();
        return ParseWikidataResponse(jsonResponse, entityType);
    }

    private WikidataInfo? ParseWikidataResponse(string jsonResponse, string entityType)
    {
        try
        {
            WikidataResponse? response = JsonSerializer.Deserialize<WikidataResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (response?.Results?.Bindings == null || !response.Results.Bindings.Any())
            {
                return null;
            }

            Dictionary<string, object> facts = new Dictionary<string, object>();
            List<string> relatedEntities = new List<string>();

            foreach (Dictionary<string, WikidataValue> binding in response.Results.Bindings)
            {
                switch (entityType.ToLower())
                {
                    case "movie":
                        ParseMovieBinding(binding, facts, relatedEntities);
                        break;
                    case "actor":
                        ParseActorBinding(binding, facts, relatedEntities);
                        break;
                    case "director":
                        ParseDirectorBinding(binding, facts, relatedEntities);
                        break;
                }
            }

            return new WikidataInfo
            {
                StructuredFacts = facts,
                RelatedEntities = relatedEntities.Distinct().ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing Wikidata response");
            return null;
        }
    }

    private void ParseMovieBinding(Dictionary<string, WikidataValue> binding, Dictionary<string, object> facts, List<string> relatedEntities)
    {
        if (binding.TryGetValue("director", out WikidataValue? director) && binding.TryGetValue("directorLabel", out WikidataValue? directorLabel))
        {
            facts["Director"] = directorLabel.Value ?? "";
            relatedEntities.Add(directorLabel.Value ?? "");
        }

        if (binding.TryGetValue("releaseDate", out WikidataValue? releaseDate))
        {
            if (DateTime.TryParse(releaseDate.Value, out DateTime date))
            {
                facts["Release Date"] = date.ToString("yyyy-MM-dd");
            }
        }

        if (binding.TryGetValue("boxOffice", out WikidataValue? boxOffice))
        {
            facts["Box Office"] = boxOffice.Value ?? "";
        }
    }

    private void ParseActorBinding(Dictionary<string, WikidataValue> binding, Dictionary<string, object> facts, List<string> relatedEntities)
    {
        if (binding.TryGetValue("birthDate", out WikidataValue? birthDate))
        {
            if (DateTime.TryParse(birthDate.Value, out DateTime date))
            {
                facts["Birth Date"] = date.ToString("yyyy-MM-dd");
            }
        }

        if (binding.TryGetValue("birthPlaceLabel", out WikidataValue? birthPlace))
        {
            facts["Birth Place"] = birthPlace.Value ?? "";
        }

        if (binding.TryGetValue("movies", out WikidataValue? movieCount))
        {
            if (int.TryParse(movieCount.Value, out int count))
            {
                facts["Movie Count"] = count;
            }
        }
    }

    private void ParseDirectorBinding(Dictionary<string, WikidataValue> binding, Dictionary<string, object> facts, List<string> relatedEntities)
    {
        if (binding.TryGetValue("birthDate", out WikidataValue? birthDate))
        {
            if (DateTime.TryParse(birthDate.Value, out DateTime date))
            {
                facts["Birth Date"] = date.ToString("yyyy-MM-dd");
            }
        }

        if (binding.TryGetValue("birthPlaceLabel", out WikidataValue? birthPlace))
        {
            facts["Birth Place"] = birthPlace.Value ?? "";
        }

        if (binding.TryGetValue("movies", out WikidataValue? movieCount))
        {
            if (int.TryParse(movieCount.Value, out int count))
            {
                facts["Movies Directed"] = count;
            }
        }

        if (binding.TryGetValue("awards", out WikidataValue? awardCount))
        {
            if (int.TryParse(awardCount.Value, out int count))
            {
                facts["Awards"] = count;
            }
        }
    }

    private string BuildMovieQuery(string movieTitle)
    {
        return $@"
            SELECT DISTINCT ?item ?itemLabel ?director ?directorLabel ?releaseDate ?boxOffice WHERE {{
              ?item wdt:P31 wd:Q11424.
              ?item rdfs:label ""{movieTitle}""@en.
              OPTIONAL {{ ?item wdt:P57 ?director. }}
              OPTIONAL {{ ?item wdt:P577 ?releaseDate. }}
              OPTIONAL {{ ?item wdt:P2142 ?boxOffice. }}
              SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"". }}
            }}
            LIMIT 10";
    }

    private string BuildActorQuery(string actorName)
    {
        return $@"
    SELECT DISTINCT ?item ?itemLabel ?birthDate ?birthPlace ?birthPlaceLabel ?occupation ?occupationLabel WHERE {{
      ?item wdt:P31 wd:Q5.
      ?item rdfs:label ""{actorName}""@en.
      ?item wdt:P106 ?occupation.
      FILTER(?occupation IN (wd:Q33999, wd:Q10800557, wd:Q2259451))
      OPTIONAL {{ ?item wdt:P569 ?birthDate. }}
      OPTIONAL {{ ?item wdt:P19 ?birthPlace. }}
      SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"". }}
    }}
    LIMIT 10";
    }

    private string BuildDirectorQuery(string directorName)
    {
        return $@"
    SELECT DISTINCT ?item ?itemLabel ?birthDate ?birthPlace ?birthPlaceLabel WHERE {{
      ?item wdt:P31 wd:Q5.
      ?item rdfs:label ""{directorName}""@en.
      ?item wdt:P106 wd:Q2526255.
      OPTIONAL {{ ?item wdt:P569 ?birthDate. }}
      OPTIONAL {{ ?item wdt:P19 ?birthPlace. }}
      SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"". }}
    }}
    LIMIT 10";
    }

    /// <summary>Scores grounding availability against the &gt; 0.5 gate in ChatPlanner. Wikipedia summary &gt; 100 chars contributes 0.6; any Wikidata facts contribute 0.4; capped at 1.0.</summary>
    private double CalculateConfidence(WikipediaContent? wikipedia, WikidataInfo? wikidata)
    {
        double score = 0.0;
        if (wikipedia?.Summary?.Length > 100)
        {
            score += 0.6;
        }
        if (wikidata?.StructuredFacts?.Any() == true)
        {
            score += 0.4;
        }
        return Math.Min(score, 1.0);
    }
}
