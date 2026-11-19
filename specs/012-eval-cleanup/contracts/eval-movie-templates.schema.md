# Contract: `eval-movie-templates.json`

Shape of the file lives at `MovieTracker.Evals/eval-movie-templates.json` and is loaded by `TemplateLoader.Load`.

**No structural change in this slice.** The one meaningful behavior change is on the reader side: `TemplateLoader.Load` now rejects an unknown `check` value with a fail-fast `InvalidOperationException`.

## Shape

```json
{
  "templates": [
    {
      "id": "<string>",
      "template": "<string with {placeholders} for a single-turn template>",
      "turns": ["<string>", "<string>", "..."],
      "check": "<string in the discovered IEvalCheck.Name set>"
    }
  ]
}
```

- `id`: template id. Case id is `{id}-{slot}`, slot ∈ {1, 2}.
- Exactly one of `template` and `turns` MUST be present. Single-turn templates set `template`. Multi-turn templates (today only `followUpDirector`) set `turns`.
- `check`: one of `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`. These must match `<Something>Check.CheckName` on the corresponding discovered check class.
- `listIncludes` is a valid check name but is only used by plot anchors (see `eval-movie-cases.schema.md`); a template naming it has the same reader behavior as any other recognised check, but today no template uses it.

## Example (current file, unchanged)

```json
{
  "templates": [
    { "id": "director",         "template": "Who directed {title}?",                              "check": "director" },
    { "id": "actorFilmography", "template": "Show me movies starring {actor}",                     "check": "actorFilmography" },
    { "id": "identifyByPlot",   "template": "Which {year} movie is this: {overview}",             "check": "identifyByPlot" },
    { "id": "genreDecade",      "template": "Show me {genre} movies from the {decade}s",          "check": "genreDecade" },
    { "id": "rating",           "template": "What is the IMDb rating of {title} ({year})?",       "check": "rating" },
    { "id": "trailer",          "template": "Show me the trailer for {title} ({year}).",          "check": "trailer" },
    { "id": "followUpDirector", "turns":    ["Tell me about {title} ({year})","Who directed it?"], "check": "followUpDirector" },
    { "id": "funnyFactNotEmpty","template": "Tell me a fun fact about {title} ({year}).",         "check": "funnyFactNotEmpty" }
  ]
}
```

## Reader behavior

- `TemplateLoader.Load()` parses the file, then validates each `check` against `CheckDiscovery.DiscoverAll()` keys. An unknown `check` throws `InvalidOperationException` naming the unknown value. The harness exits non-zero before any agent call.
- Every parsed template yields exactly 2 generated cases per run (one per slot) under `GeneratedCaseBuilder.BuildAsync`.
