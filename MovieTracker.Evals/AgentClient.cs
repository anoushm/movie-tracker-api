using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public sealed class AgentClient
{
    private readonly HttpClient httpClient;

    public AgentClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<string> StartChatAsync()
    {
        HttpResponseMessage response = await this.httpClient.GetAsync("/api/chat/start");
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("chatId", out JsonElement chatIdElement)
            || chatIdElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("chat/start response missing chatId");
        }

        string? chatId = chatIdElement.GetString();
        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException("chat/start returned empty chatId");
        }

        return chatId;
    }

    public async Task<AgentReply> AskAsync(string chatId, string question)
    {
        HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(
            $"/api/chat/{chatId}/ask/v2",
            new { input = question });
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("ask/v2 response was not a JSON object");
        }

        if (!root.TryGetProperty("funnyFact", out JsonElement funnyFactElement))
        {
            throw new InvalidOperationException("ask/v2 response missing funnyFact");
        }

        string? funnyFact;
        if (funnyFactElement.ValueKind == JsonValueKind.String)
        {
            funnyFact = funnyFactElement.GetString();
        }
        else if (funnyFactElement.ValueKind == JsonValueKind.Null)
        {
            funnyFact = null;
        }
        else
        {
            throw new InvalidOperationException($"ask/v2 response has funnyFact of unexpected type: {funnyFactElement.ValueKind}");
        }

        if (!root.TryGetProperty("turn", out JsonElement turnElement)
            || turnElement.ValueKind != JsonValueKind.Object
            || !turnElement.TryGetProperty("text", out JsonElement textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("ask/v2 response missing turn.text");
        }

        string? answer = textElement.GetString();
        if (answer is null)
        {
            throw new InvalidOperationException("ask/v2 returned null turn.text");
        }

        if (!turnElement.TryGetProperty("movieList", out JsonElement movieListElement))
        {
            throw new InvalidOperationException("ask/v2 response missing turn.movieList");
        }

        if (movieListElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"ask/v2 response turn.movieList has unexpected type: {movieListElement.ValueKind}");
        }

        List<MovieListEntry> movieList = new List<MovieListEntry>();
        int entryIndex = 0;
        foreach (JsonElement entry in movieListElement.EnumerateArray())
        {
            movieList.Add(this.ParseMovieListEntry(entry, entryIndex));
            entryIndex++;
        }

        return new AgentReply(answer, funnyFact, movieList);
    }

    private MovieListEntry ParseMovieListEntry(JsonElement entry, int entryIndex)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}] is not a JSON object");
        }

        int id = 0;
        if (entry.TryGetProperty("id", out JsonElement idElement))
        {
            if (idElement.ValueKind == JsonValueKind.Number)
            {
                if (!idElement.TryGetInt32(out id))
                {
                    throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].id is not a 32-bit integer");
                }
            }
            else if (idElement.ValueKind == JsonValueKind.String)
            {
                string? raw = idElement.GetString();
                if (!int.TryParse(raw, out id))
                {
                    throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].id ('{raw}') is not a 32-bit integer");
                }
            }
            else
            {
                throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].id has unexpected type: {idElement.ValueKind}");
            }
        }

        string title = string.Empty;
        if (entry.TryGetProperty("title", out JsonElement titleElement))
        {
            if (titleElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].title has unexpected type: {titleElement.ValueKind}");
            }

            title = titleElement.GetString() ?? string.Empty;
        }

        string? releaseDate = null;
        if (entry.TryGetProperty("releaseDate", out JsonElement releaseElement))
        {
            if (releaseElement.ValueKind == JsonValueKind.String)
            {
                releaseDate = releaseElement.GetString();
            }
            else if (releaseElement.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].releaseDate has unexpected type: {releaseElement.ValueKind}");
            }
        }

        List<int> genreIds = new List<int>();
        if (entry.TryGetProperty("genreIds", out JsonElement genreIdsElement))
        {
            if (genreIdsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].genreIds has unexpected type: {genreIdsElement.ValueKind}");
            }

            foreach (JsonElement gid in genreIdsElement.EnumerateArray())
            {
                if (gid.ValueKind != JsonValueKind.Number || !gid.TryGetInt32(out int value))
                {
                    throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].genreIds has a non-integer element");
                }

                genreIds.Add(value);
            }
        }

        List<string> genreNames = new List<string>();
        if (entry.TryGetProperty("genreNames", out JsonElement genreNamesElement)
            && genreNamesElement.ValueKind != JsonValueKind.Null)
        {
            if (genreNamesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].genreNames has unexpected type: {genreNamesElement.ValueKind}");
            }

            foreach (JsonElement gn in genreNamesElement.EnumerateArray())
            {
                if (gn.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"ask/v2 response turn.movieList[{entryIndex}].genreNames has a non-string element");
                }

                string? name = gn.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    genreNames.Add(name);
                }
            }
        }

        return new MovieListEntry(id, title, releaseDate, genreIds, genreNames);
    }
}
