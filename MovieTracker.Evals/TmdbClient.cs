using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public sealed class TmdbClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public TmdbClient(string apiKey)
    {
        this.apiKey = apiKey;
        this.httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
        };
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    public async Task<IReadOnlyList<MovieSummary>> GetPopularAsync(Region region, int page)
    {
        string regionCode = region.ToString();
        string path = $"movie/popular?api_key={this.apiKey}&region={regionCode}&page={page}";
        using JsonDocument document = await this.GetJsonOrThrowAsync(path);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"TMDb /movie/popular (region={regionCode}, page={page}) response missing results array");
        }

        List<MovieSummary> list = new List<MovieSummary>();
        foreach (JsonElement entry in results.EnumerateArray())
        {
            MovieSummary? summary = this.TryParseSummary(entry);
            if (summary is not null)
            {
                list.Add(summary);
            }
        }

        return list;
    }

    public async Task<MovieDetails> GetMovieDetailsAsync(int tmdbId)
    {
        string path = $"movie/{tmdbId}?api_key={this.apiKey}&append_to_response=external_ids";
        using JsonDocument document = await this.GetJsonOrThrowAsync(path);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"TMDb /movie/{tmdbId} returned a non-object JSON body");
        }

        string? releaseDate = this.GetStringProperty(root, "release_date");
        string? overview = this.GetStringProperty(root, "overview");

        string? imdbId = null;
        if (root.TryGetProperty("external_ids", out JsonElement externalIds)
            && externalIds.ValueKind == JsonValueKind.Object)
        {
            imdbId = this.GetStringProperty(externalIds, "imdb_id");
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                imdbId = null;
            }
        }

        List<MovieGenre> genres = new List<MovieGenre>();
        if (root.TryGetProperty("genres", out JsonElement genresElement)
            && genresElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in genresElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!entry.TryGetProperty("id", out JsonElement idElement)
                    || idElement.ValueKind != JsonValueKind.Number
                    || !idElement.TryGetInt32(out int genreId))
                {
                    continue;
                }

                string? name = this.GetStringProperty(entry, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                genres.Add(new MovieGenre(genreId, name));
            }
        }

        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            releaseDate = null;
        }

        if (string.IsNullOrWhiteSpace(overview))
        {
            overview = null;
        }

        return new MovieDetails(releaseDate, imdbId, overview, genres);
    }

    public async Task<MovieCredits> GetCreditsAsync(int tmdbId)
    {
        string path = $"movie/{tmdbId}/credits?api_key={this.apiKey}";
        using JsonDocument document = await this.GetJsonOrThrowAsync(path);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"TMDb /movie/{tmdbId}/credits returned a non-object JSON body");
        }

        string? director = null;
        if (root.TryGetProperty("crew", out JsonElement crew) && crew.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement member in crew.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!member.TryGetProperty("job", out JsonElement jobElement)
                    || jobElement.ValueKind != JsonValueKind.String
                    || jobElement.GetString() != "Director")
                {
                    continue;
                }

                string? name = this.GetStringProperty(member, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    director = name;
                    break;
                }
            }
        }

        int? leadActorId = null;
        string? leadActorName = null;
        if (root.TryGetProperty("cast", out JsonElement cast) && cast.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement member in cast.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!member.TryGetProperty("order", out JsonElement orderElement)
                    || orderElement.ValueKind != JsonValueKind.Number
                    || !orderElement.TryGetInt32(out int order)
                    || order != 0)
                {
                    continue;
                }

                string? name = this.GetStringProperty(member, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!member.TryGetProperty("id", out JsonElement personIdElement)
                    || personIdElement.ValueKind != JsonValueKind.Number
                    || !personIdElement.TryGetInt32(out int personId))
                {
                    continue;
                }

                leadActorId = personId;
                leadActorName = name;
                break;
            }
        }

        return new MovieCredits(director, leadActorId, leadActorName);
    }

    public async Task<ActorFilmography> GetPersonMovieCreditsAsync(int personId)
    {
        string path = $"person/{personId}/movie_credits?api_key={this.apiKey}";
        using JsonDocument document = await this.GetJsonOrThrowAsync(path);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"TMDb /person/{personId}/movie_credits returned a non-object JSON body");
        }

        List<int> ids = new List<int>();
        List<string> titles = new List<string>();

        if (root.TryGetProperty("cast", out JsonElement castElement)
            && castElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in castElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (entry.TryGetProperty("id", out JsonElement idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.TryGetInt32(out int movieId))
                {
                    ids.Add(movieId);
                }

                string? title = this.GetStringProperty(entry, "title");
                if (!string.IsNullOrWhiteSpace(title))
                {
                    titles.Add(title);
                }
            }
        }

        return new ActorFilmography(ids, titles);
    }

    public async Task<IReadOnlyList<string>> GetTrailerVideoKeysAsync(int tmdbId)
    {
        string path = $"movie/{tmdbId}/videos?api_key={this.apiKey}";
        using JsonDocument document = await this.GetJsonOrThrowAsync(path);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"TMDb /movie/{tmdbId}/videos returned a non-object JSON body");
        }

        List<string> keys = new List<string>();
        if (root.TryGetProperty("results", out JsonElement results)
            && results.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in results.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? site = this.GetStringProperty(entry, "site");
                if (!string.Equals(site, "YouTube", StringComparison.Ordinal))
                {
                    continue;
                }

                string? type = this.GetStringProperty(entry, "type");
                if (!string.Equals(type, "Trailer", StringComparison.Ordinal))
                {
                    continue;
                }

                string? key = this.GetStringProperty(entry, "key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private MovieSummary? TryParseSummary(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!entry.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt32(out int tmdbId))
        {
            return null;
        }

        string? title = this.GetStringProperty(entry, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string? releaseDate = this.GetStringProperty(entry, "release_date");

        double popularity = 0;
        if (entry.TryGetProperty("popularity", out JsonElement popularityElement)
            && popularityElement.ValueKind == JsonValueKind.Number
            && popularityElement.TryGetDouble(out double popularityValue))
        {
            popularity = popularityValue;
        }

        return new MovieSummary(tmdbId, title, releaseDate, popularity);
    }

    private string? GetStringProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement propertyElement)
            || propertyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private async Task<JsonDocument> GetJsonOrThrowAsync(string relativeUrl)
    {
        string loggable = this.Sanitize(relativeUrl);

        HttpResponseMessage response;
        try
        {
            response = await this.httpClient.GetAsync(relativeUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"TMDb request failed: GET {loggable} — {this.Sanitize(ex.Message)}");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException($"TMDb request timed out: GET {loggable}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"TMDb request returned HTTP {(int)response.StatusCode}: GET {loggable}");
        }

        string body = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"TMDb response was not valid JSON: GET {loggable} — {this.Sanitize(ex.Message)}");
        }
    }

    private string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Replace(this.apiKey, "<redacted>");
    }
}
