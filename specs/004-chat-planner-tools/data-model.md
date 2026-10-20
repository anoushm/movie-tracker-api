# Phase 1 Data Model: ChatPlanner Tools

**Branch**: `004-chat-planner-tools` | **Date**: 2026-10-20

The slice is a port. Entities below carry the backend's shape as-is except
for namespace adaptation (`MovieTracker.Backend.*` → `MovieTracker.Api.*`)
and the one-public-type-per-file split that slice 003's CS-1 convention
requires. Backend source paths are given so each entity has a verifiable
origin.

## New services

### `ChatPlanner` (`Prompts/ChatPlanner.cs`)

- **Source**: `MovieTracker.Backend/Prompts/ChatPlanner.cs:14-329`.
- **Lifetime**: Scoped (backend `Program.cs:277`). The slice 003 `AIAgent`
  and `IChatClient` registrations also change from Singleton to Scoped in
  this slice; see research Decision 6.
- **Constructor dependencies**: `IChatClient`, `WikipediaSearchAgent`,
  `OpenMovieDbAgent`, `TrailerAgent`.
- **Public API**:
  - `IEnumerable<AITool> CreateTools()` — the six FR-001 entries in order,
    created via `AIFunctionFactory.Create`.
  - `Task<string?> GenerateEnhancedFunnyFact(string userQuery)` — invoked
    from `ChatTurnEndpoints.RunTurnAsync` via `SafeGenerateFunnyFactAsync`,
    NOT exposed as a tool (FR-018).
  - Six tool methods: `HandleTrailerRequest`, `GetMovieRating`,
    `CompareMovieRatings`, `FilterMoviesByRating`, `GetChatContext`,
    `GetMovieContext`.
- **Private helpers**: `DetectEntity`, `ParseDetectedEntity`, `NullIfNone`,
  nested `DetectedEntity` record.

### `OpenMovieDbAgent` (`Agents/OpenMovieDbAgent.cs`)

- **Source**: `MovieTracker.Backend/Agents/OpenMovieDbAgent.cs:59-171`.
- **Lifetime**: Scoped (backend `Program.cs:206`).
- **Constructor dependencies**: `IConfiguration` (reads `OpenMovieDb:Api-Key`).
- **Public API**:
  - `Task<MovieRatingResult> GetMovieRatings(string imdbId)`.
  - `Task<RatingComparisonResult> CompareMovieRatings(List<string> imdbIds)`.
  - `Task<List<MovieRatingResult>> FilterMoviesByRating(List<string>, double)`.
- **Internals**: wraps `OMDbApiNet.AsyncOmdbClient`. The private
  `GetManyAsync` runs OMDb lookups via `Task.WhenAll`, preserving positional
  order (FR-003).

### `TrailerAgent` (`Agents/TrailerAgent.cs`)

- **Source**: `MovieTracker.Backend/Agents/TrailerAgent.cs:23-83`.
- **Lifetime**: Scoped (backend `Program.cs:207`).
- **Constructor dependencies**: `TMDbClient` (slice 003's singleton).
- **Public API**: `Task<string> GetTrailerForTitle(string movieTitle)`
  returning the JSON payload (success + `EmbedInSystemMessage` wrapper, or
  error+hint for empty/UNKNOWN titles, no match, or no YouTube trailer).

### `WikipediaSearchAgent` (`Agents/WikipediaSearchAgent.cs`)

- **Source**: `MovieTracker.Backend/Agents/WikipediaSearchAgent.cs:8-282`.
- **Lifetime**: Typed `HttpClient` service via `AddHttpClient<T>()` (backend
  `Program.cs:191-201`). No `AddScoped<T>()` alongside — would shadow the
  typed registration (backend note at `:202-205`).
- **Constructor dependencies**: `HttpClient` (typed), `ILogger<WikipediaSearchAgent>`.
- **Public API**: `Task<WikipediaResult?> GetEnhancedInfo(string entityName, string entityType = "movie")`.
- **HttpClient configuration (FR-005)**:
  - Two `UserAgent` `ProductInfoHeaderValue` entries: the first
    `"{serviceName}/{serviceVersion}"` using this API's assembly name and
    informational version; the second
    `"(+https://github.com/anoushm/movie-tracker-api)"` as the Wikimedia
    policy comment (research Decision 2).
  - `Timeout = TimeSpan.FromSeconds(15)`.
- **Behavioural invariants** (FR-005): outer try/catch returns `null` on
  any exception; `SearchWikipedia` is one GET to
  `/api/rest_v1/page/summary/{name}` (no section fetches); `QueryWikidata`
  POSTs form-encoded `query` + `format=json` to
  `https://query.wikidata.org/sparql`; JSON deserialisation keeps the
  backend's `[JsonPropertyName]` attributes verbatim.

## New types in `Agents/` (one public type per file)

Each type is a direct port from the backend source. Fields and defaults
match the backend definitions exactly. The OMDb result types are C#
`record`s (positional); the Wikipedia / Wikidata types are mutable
`class`es with `{ get; set; }` properties — the backend declares them that
way and the port does not change the kind.

| Entity | Kind | Source (backend path:line) | Shape |
|---|---|---|---|
| `MovieRatingResult` | record | `Agents/OpenMovieDbAgent.cs:30-49` | `(string Title, string Year, string ImdbRating, string RottenTomatoesRating, string MetacriticRating, string BoxOffice, bool IsSuccess, string ErrorMessage = "", MovieTrailerInfo? Trailer = null, string Rated = "", string Runtime = "", string Genre = "", string Director = "", string Writer = "", string Actors = "", string Awards = "", string Plot = "", string ImdbVotes = "")`. `Trailer` references slice 003's `Chat/MovieTrailerInfo.cs` — the backend's duplicate declaration is NOT reproduced (one less file). |
| `RatingComparisonResult` | record | `Agents/OpenMovieDbAgent.cs:51-57` | `(string HighestRatedTitle, string HighestRating, List<MovieRatingResult> AllMovies, bool IsSuccess, string ErrorMessage = "")`. |
| `WikipediaResult` | class | `Agents/WikipediaSearchAgent.cs:284-290` | `string EntityName = ""; WikipediaContent? WikipediaContent; WikidataInfo? StructuredData; double ConfidenceScore;` — note the public property on this class is `StructuredData` (not `WikidataInfo`); `ChatPlanner` reads `wikipediaInfo.StructuredData?.StructuredFacts`. |
| `WikipediaContent` | class | `:292-296` | `string? Summary; string? Thumbnail;`. |
| `WikidataInfo` | class | `:298-302` | `Dictionary<string, object>? StructuredFacts; List<string>? RelatedEntities;`. |
| `WikipediaSummary` | class | `:320-330` | `[JsonPropertyName]`-decorated: `title`, `extract`, `thumbnail`. **Attributes are load-bearing** — the Function App's history records them as the fix that made the whole grounded path work (`:304-319`). |
| `WikipediaThumbnail` | class | `:332-342` | `[JsonPropertyName]` for `source`, `width`, `height`. |
| `WikidataResponse` | class | `:344-348` | `Head?`, `Results?`. |
| `WikidataHead` | class | `:350-353` | `Vars?` (`List<string>?`). |
| `WikidataResults` | class | `:355-358` | `Bindings?` (`List<Dictionary<string, WikidataValue>>?`). |
| `WikidataValue` | class | `:360-365` | `Type?`, `Value?`, `DataType?`. |

## Reused slice 003 entities (unchanged)

| Entity | Role in this slice |
|---|---|
| `MoviceTrackerChatSession` (`ChatSessions/MoviceTrackerChatSession.cs`) | `FunnyFact` nullable string property, declared by slice 002, written by this slice's `RunTurnAsync` when the funny-fact task returns a non-null string. No schema change. |
| `ChatMessageResponse` (`Chat/ChatMessageResponse.cs`) | Carries `session.FunnyFact` into the `/ask` envelope's top-level `funnyFact` field (previously always-null — slice 003 FR-004). |
| `ChatTurnResponse` (`Chat/ChatTurnResponse.cs`) | Same for `/ask/v2`. |
| `MovieTrailerInfo` (`Chat/MovieTrailerInfo.cs`) | Referenced by `MovieRatingResult.Trailer`. Reused unchanged. |
| `TMDbClient` (singleton registered in slice 003's `Program.cs`) | Injected into `TrailerAgent`. |
| `AIAgent` (slice 003's single registration) | Its tool list gets `.. chatPlanner.CreateTools()` appended. Its DI lifetime changes from `AddSingleton` to `AddScoped` as part of this slice (research Decision 6). The spec's "single AIAgent" boundary (FR-018) is lifetime-agnostic. |
| `IChatClient` (slice 003's registration) | Injected into `ChatPlanner` for the three single-shot completions. Its DI lifetime also changes from `AddSingleton` to `AddScoped` for consistency with the Scoped `AIAgent` (research Decision 6). |
| `MovieListResponse` structured-output DTO | Unchanged. `FunnyFact` remains absent (FR-016). |

## Removed / not-added

- No new entity for the funny-fact flow. The `string?` returned by
  `ChatPlanner.GenerateEnhancedFunnyFact` is stored on
  `MoviceTrackerChatSession.FunnyFact` and read into the two existing
  response envelopes.
- No new interface or abstraction over any ported class.
- No new health-probe response entity.
- No ChatPlanner-side cache entity (the Function App has none).
