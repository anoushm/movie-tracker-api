using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public static class ResultsStore
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<RunResult?> ReadPreviousAsync()
    {
        string path = GetPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path);
            RunResult? previous = JsonSerializer.Deserialize<RunResult>(json, jsonOptions);
            return previous;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: could not parse previous results file: {ex.Message}");
            return null;
        }
    }

    public static async Task WriteAsync(RunResult current)
    {
        string resultsDirectory = Path.Combine(ProjectRoot.Path, "results");
        if (!Directory.Exists(resultsDirectory))
        {
            Directory.CreateDirectory(resultsDirectory);
        }

        string path = GetPath();
        string json = JsonSerializer.Serialize(current, jsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private static string GetPath()
    {
        return Path.Combine(ProjectRoot.Path, "results", "last-run.json");
    }
}
