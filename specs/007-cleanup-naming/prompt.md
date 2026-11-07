# 007 Cleanup and Naming: Spec Kit Prompt

Prompt 1 of the plan: cleanup before phase A.
Run in Claude CLI from `C:\projects\dev\movie-tracker-api`.

Goal: correct naming, remove leftovers, organize folders. Pure refactor. Keep the code simple.
No new features. No behavior change. Every endpoint path, request body and response body stays identical.

---

## Rules for every stage

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly.
- Feature folder is `specs/007-cleanup-naming/` (this file lives in it). Do not create any other spec folder.
- Do not add any feature, config key, appsettings value, option, switch or endpoint.
- Do not change any text the model sees: system prompts (`ChatStartSystemPrompt`), `[Description]` strings, agent instructions.
- Do not rename any method that is registered as a tool with `AIFunctionFactory.Create`. The method name is the tool name the model sees.
- Keep wire and storage names as they are: `MovieTrackerChatSession.id`, `ChatMessage.role`, JSON type discriminators, and all record property names.
- Keep telemetry names as they are: agent `Name = "MovieTracker.Api"`, logger category `MovieTracker.Api.ToolCalls`, span names, meter and source names.
- Keep `/health/openai` and every other health endpoint unchanged.
- No commit, no push, no git commands. The owner commits.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.

## Code style

- PascalCase for types, methods and properties. camelCase for locals and parameters.
- Private fields: plain camelCase, no underscore prefix.
- `I` prefix on interfaces. `Async` suffix on async methods that are not registered as tools.
- One type per file. File name equals type name.
- File-scoped namespaces that match the folder (`MovieTracker.Api.Tools`, `MovieTracker.Api.Models`, ...).
- Explicit types, never `var`. Braces on every `if`.
- No code comments. Remove all existing `///` doc comments.
- Update namespaces and `using` statements for every moved file.

---

## Scope

### 1. Agent vs tool naming

An agent is the LLM (large language model) that decides. A tool is code the agent calls.

| Current | New | Folder |
|---|---|---|
| `Agents/OpenMovieDbAgent` | `OmdbTools` | `Tools/` |
| `Agents/TrailerAgent` | `TrailerTools` | `Tools/` |
| `Agents/WikipediaSearchAgent` | `WikipediaTools` | `Tools/` |
| `Prompts/TheMovieDBKernelFunctions` | `TmdbTools` | `Tools/` |
| `Prompts/DateTimeKernelFunctions` | `DateTimeTools` | `Tools/` |
| `Prompts/ChatPlanner` | `ChatPlanner` (same name) | `Tools/` |

- Rename the matching fields and parameters (for example `wikipediaAgent` becomes `wikipediaTools`).
- The `AIAgent` built inline in `Program.cs` is the only real agent. Move it to `Agents/MovieAgent.cs` as the `AddMovieAgent` registration.
  Same scoped lifetime, same tools in the same order (TMDb, DateTime, ChatPlanner), same `InstrumentedAIFunction` wrapping, same `ChatClientAgentOptions`.

### 2. Other renames

| Current | New |
|---|---|
| `ChatSessions/MoviceTrackerChatSession` | `ChatSessions/MovieTrackerChatSession` |

### 3. Layout (flat, one level, no subfolders)

```
MovieTracker.Api/
  Program.cs
  HealthEndpoints.cs
  Agents/        MovieAgent
  Tools/         TmdbTools, OmdbTools, TrailerTools, WikipediaTools, DateTimeTools, ChatPlanner
  Chat/          ChatTurnEndpoints, chat endpoint registration
  Models/        every record, DTO and view model
  ChatSessions/  ChatSessionRepository, MovieTrackerChatSession, ChatStartSystemPrompt, CosmosSystemTextJsonSerializer
  Telemetry/     InstrumentedAIFunction, ModelPricing, Telemetry, telemetry registration
```

- Move to `Models/`: `MovieRatingResult`, `RatingComparisonResult`, all `Wikidata*`, all `Wikipedia*` records (from `Agents/`), and `Ask`, `ChatMessage`, `UserChatMessage`, `AssistantChatMessage`, `ChatMessageResponse`, `ChatTurnResponse`, `MovieListItem`, `MovieListResponse`, `MovieTrailerInfo`, `MovieViewModel` (from `Chat/`).
- Any other top-level type that shares a file with another type moves to its own file in `Models/`.
- Move `Core/CosmosSystemTextJsonSerializer` to `ChatSessions/`.
- Delete the empty `Prompts/`, `Core/` and `Controllers/` folders.

### 4. Remove leftovers

- `WeatherForecast.cs`
- `Controllers/WeatherForecastController.cs`
- `Controllers/AskController.cs` (the frontend uses `/api/chat/...` only)
- `Models/AskRequestDot.cs`, `Models/AskResponseDot.cs` (used only by `AskController`)
- `Core/AppError.cs`, `Core/Result.cs`, `Core/ResultExtensions.cs` (no callers after `AskController` is gone)
- `AddControllers()` and `MapControllers()` in `Program.cs`
- The `weatherforecast` request in `MovieTracker.Api.http`

Before deleting each item, confirm with a search that nothing else references it. If something does, stop and report it.

### 5. Split Program.cs

Program.cs becomes a short composition root. Each extension method lives in a static class in the folder of what it registers:

| Method | Registers |
|---|---|
| `AddTelemetry` | OpenTelemetry logging, tracing, metrics, `Tracer`, `ModelPricing` |
| `AddMovieTools` | `TMDbClient`, the tool classes, the Wikipedia typed `HttpClient` |
| `AddMovieAgent` | `IChatClient` and the `AIAgent` |
| `AddChatSessions` | `CosmosClient`, `ChatSessionRepository` |
| `MapHealthEndpoints` | `/health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai`, `/version` |
| `MapChatEndpoints` | `/api/chat/start`, `/api/chat/{chatId}/ask`, `/api/chat/{chatId}/ask/v2` |

- Keep the fail-fast config checks and their exact error messages.
- Keep the registration order where it matters (the named `HttpClient` before `IChatClient`).
- Keep `AddOpenApi`, `MapOpenApi` and the HTTPS redirection rules as they are.

### 6. CLAUDE.md

Update only the "Project Structure" section to match the new layout. Change nothing else.

## Out of scope

- Any change to behavior, endpoints, JSON shapes, Cosmos documents, config keys, Bicep or workflows.
- Edits to older specs (001 to 006), even where they name old classes or `/health/openai`.
- New tests, new packages, new abstractions or interfaces.
- Any change to prompt or tool description text.

---

## Steps

Each Spec Kit stage ends with a Codex cross-review (see the next section).

1. `/speckit-specify` using this file as the feature description.
   Create the feature with `-Number 7 -ShortName cleanup-naming -AllowExistingBranch` so it reuses `specs/007-cleanup-naming/`.
   The spec must list what this slice changes and the out-of-scope list above (Constitution III).
2. `/speckit-plan`
3. `/speckit-tasks`
4. `/speckit-implement`
5. Verify (see "Done when").

## Codex cross-review

After each of specify, plan, tasks and implement:

- Run a Codex review of that stage's output.
- Guardrails for Codex: stay focused on this cleanup, cut inflated or overkill work, keep it simple without losing any functionality.
- Fix the findings and run the review again. Repeat until Codex approves, up to 6 rounds.
- If round 6 is not approved, stop and report the open findings to the owner.

## Done when

- `dotnet build MovieTracker.Api/MovieTracker.Api.csproj` succeeds with no new warnings.
- `dotnet test` passes, if a test project exists.
- No class named `*Agent` remains except `MovieAgent`.
- No `var`, no `_` field prefix, no code comments in changed files.
- Manual smoke test (local run), with responses identical in shape to before the change:
  1. `GET /health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai` return 200 with the same body shape.
  2. `GET /api/chat/start` returns `{ "chatId": "..." }`.
  3. `POST /api/chat/{chatId}/ask` with `{ "input": "Recommend a sci-fi movie from the 90s" }` returns `funnyFact` and `messages`.
  4. `POST /api/chat/{chatId}/ask/v2` with `{ "input": "Show me its trailer" }` returns `funnyFact` and `turn`.
  5. `POST /Ask` and `GET /weatherforecast` now return 404 (expected, removed).
