using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public sealed class OmdbClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public OmdbClient(string apiKey)
    {
        this.apiKey = apiKey;
        this.httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.omdbapi.com/"),
        };
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    public async Task<string?> GetImdbRatingAsync(string imdbId)
    {
        string relativeUrl = $"?i={imdbId}&apikey={this.apiKey}";

        HttpResponseMessage response;
        try
        {
            response = await this.httpClient.GetAsync(relativeUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"OMDb request failed for i={imdbId}: {this.Sanitize(ex.Message)}");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException($"OMDb request timed out for i={imdbId}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OMDb request returned HTTP {(int)response.StatusCode} for i={imdbId}");
        }

        string body = await response.Content.ReadAsStringAsync();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"OMDb response was not valid JSON for i={imdbId}: {this.Sanitize(ex.Message)}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("imdbRating", out JsonElement ratingElement)
                || ratingElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? rating = ratingElement.GetString();
            if (string.IsNullOrWhiteSpace(rating)
                || string.Equals(rating, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return rating;
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
