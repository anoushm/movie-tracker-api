using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieTracker.Evals;

public static class TemplateLoader
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IReadOnlyList<CaseTemplate> Load()
    {
        string path = Path.Combine(ProjectRoot.Path, "eval-movie-templates.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"eval-movie-templates.json not found at {path}");
        }

        string json = File.ReadAllText(path);
        CaseTemplateFile? file = JsonSerializer.Deserialize<CaseTemplateFile>(json, jsonOptions);
        if (file is null || file.Templates is null)
        {
            throw new InvalidOperationException("eval-movie-templates.json could not be parsed into CaseTemplateFile");
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CaseTemplate template in file.Templates)
        {
            if (!seen.Add(template.Id))
            {
                throw new InvalidOperationException($"eval-movie-templates.json has duplicate id: {template.Id}");
            }
        }

        return file.Templates;
    }
}
