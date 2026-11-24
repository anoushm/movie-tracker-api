# Data Model: Chat Pipeline

**Branch**: `013-chat-pipeline` | **Date**: 2026-11-24

This slice is a refactor. No persisted schema changes, no DTO changes. The pipeline-boundary record shapes below describe observable behavior; exact names, method signatures, and whether the ask paths share one method or two (delegating to a shared private helper) are `/speckit-tasks` / `/speckit-implement` decisions.

## Pipeline-boundary results (observable behavior)

### Chat start outcome

Produced by the chat-start pipeline entry point. Carries the new chat session id. The endpoint maps a success case to `200 OK { "chatId": "<id>" }` and any thrown exception to `503 {"error":"Session creation failed."}` with log `Chat-Start session creation failed.`.

### Chat ask outcome (`/ask`)

The pipeline returns a result the endpoint can map to one of:

- Success → `200 OK` with `ChatMessageResponse { FunnyFact: string?, Messages: List<ChatMessage> }`. `Messages` is the **full hydrated chat history** (user + assistant messages, with system messages and empty-text messages filtered — same filter as `main`).
- Chat-not-found → `404 {"error":"Chat not found"}`. Produced when the session load caught `CosmosException { StatusCode: NotFound }`. **Not an exception** — a distinct result case so the endpoint has no try/catch around a status check.
- Any other exception bubbles out; the endpoint's outer try/catch maps to `503 {"error":"Chat request failed."}` with log `Chat-Ask failed for session {ChatId}`.

### Chat ask outcome (`/ask/v2`)

Same three cases as `/ask`, with these differences:

- Success → `200 OK` with `ChatTurnResponse { FunnyFact: string?, Turn: ChatMessage }`. `Turn` is the single newly hydrated assistant message (or `new AssistantChatMessage("No movies were found", new List<MovieViewModel>())` fallback — exact `main` behavior).
- 503 log line is `Chat-Ask-V2 failed for session {ChatId}`.

Hydration contract (matches `main` exactly):

- `/ask` hydrates every assistant message in the history; one assistant message may contribute multiple movies. Each movie lookup follows the path session cache → distributed cache → TMDb; on a cache miss both caches are populated. Movies inside one message are processed in parallel (`Task.WhenAll` over the slots). A per-movie failure is caught and logged (`Failed to hydrate a movie entry; skipping it`); other movies in the same message continue.
- `/ask/v2` hydrates **only the new assistant message**, under the same lookup, parallelism, and per-movie failure-isolation rules as `/ask`.

Whether these two outcomes share a base type or are modeled as separate types is a `/speckit-tasks` / `/speckit-implement` decision, provided the endpoint-side mapping above is preserved.

## Unchanged DTOs (externally visible)

The three endpoint request + response types stay byte-for-byte identical to `main`:

- `MovieTracker.Api/Models/Ask.cs` — `public class Ask { public string Input { get; set; } = string.Empty; }`
- `MovieTracker.Api/Models/ChatMessageResponse.cs` — `public record ChatMessageResponse(string? FunnyFact, List<ChatMessage> Messages);`
- `MovieTracker.Api/Models/ChatTurnResponse.cs` — `public record ChatTurnResponse(string? FunnyFact, ChatMessage Turn);`
- `MovieTracker.Api/Models/ChatMessage.cs`, `AssistantChatMessage.cs`, `UserChatMessage.cs`, `MovieViewModel.cs`, `MovieTrailerInfo.cs`, `MovieListResponse.cs`, `MovieListItem.cs` — all unchanged.

## Session state (unchanged shape)

- `MovieTracker.Api/ChatSessions/MovieTrackerChatSession` — `id`, `ChatHistory`, `FunnyFact`, `HydratedMovies`. Shape unchanged; the pipeline reads and writes the same fields `ChatTurnEndpoints` does today.
- Cosmos partition / indexing — unchanged.

## Eval records touched by the rating fix

The fix adds a new TMDb call path and a new title selection path. It does not add a persisted record; `results/last-run.json` schema is unchanged.

### Unchanged

- `MovieTracker.Evals/Cases/RunTitle` — the same record (`TmdbId`, `Title`, `Slot`) is reused for rating titles. No new field.
- `MovieTracker.Evals/Reporting/RunResult`, `CaseResult`, `PerCheckCount` — schema unchanged. Both rating cases now resolve to `passed` or `failed` + `skipped: false` in the common case instead of `skipped: true`.
- `MovieTracker.Evals/Checks/RatingCheck`, `RatingExpected` — unchanged (FR-014).
- Rating question template in `eval-movie-templates.json` — unchanged (FR-014).

### Added

- `MovieTracker.Evals/Tmdb/TmdbClient.GetTopRatedAsync()` — parallel to `GetPopularAsync`; path `movie/top_rated?api_key=...&page=1`; same `TryParseSummary` body; same return type.
- A rating title selection path (one of the three alternatives listed in `research.md` Decision 6; picked in `/speckit-tasks`). Returns an `IReadOnlyList<RunTitle>` with `Slot = 1, 2` identical in shape to the popular run titles.

### Changed

- `MovieTracker.Evals/Cases/GeneratedCaseBuilder.BuildAsync` — for the `RatingCheck.CheckName` branch, uses the rating titles' slot instead of the popular `selected` run title. Every other branch is unchanged.

## Preserved invariants

- `MaxModelTurns = 12`, `CollapsedToolResultChars = 160`, `CollapsedArgumentChars = 60`, `CollapsedResultNames = 6`, `HydratedMovieLimit = 150` — values and effective behavior unchanged after the move.
- `StructuredOutputJsonOptions` and `MovieListResponseFormat` — same JSON schema and the same `RequireAllProperties = true`, `DisallowAdditionalProperties = true` inference options.
- Cache keys (`cache.GetAsync(movieId)` / `cache.SetAsync(movieId, bytes)`) — unchanged. No new cache key, no changed serialization. The hydration path's lookup order, cache population, parallelism, and per-movie failure-isolation semantics stay as `main` describes above.
- Telemetry metrics — `TelemetryHelpers.ContextRetainedRatio`, `TelemetryHelpers.ToolCallsPerTurn`, `TelemetryHelpers.RecordCost` are called from the moved code with identical arguments.
- Activity tags — `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read.input_tokens`, `gen_ai.usage.reasoning.output_tokens` set on `Activity.Current` from the pipeline on the same conditions.
