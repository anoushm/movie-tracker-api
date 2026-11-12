using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieTracker.Evals;

public static class PlotAnchorCaseLoader
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IReadOnlyList<PlotAnchorCase> Load()
    {
        string path = Path.Combine(ProjectRoot.Path, "eval-movie-cases.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"eval-movie-cases.json not found at {path}");
        }

        string json = File.ReadAllText(path);
        PlotAnchorCaseFile? file = JsonSerializer.Deserialize<PlotAnchorCaseFile>(json, jsonOptions);
        if (file is null || file.Cases is null)
        {
            throw new InvalidOperationException("eval-movie-cases.json could not be parsed into PlotAnchorCaseFile");
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlotAnchorCase caseEntry in file.Cases)
        {
            if (!seen.Add(caseEntry.Id))
            {
                throw new InvalidOperationException($"eval-movie-cases.json has duplicate id: {caseEntry.Id}");
            }
        }

        return file.Cases;
    }
}
