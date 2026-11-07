# Feature Specification: Cleanup and Naming

**Feature Branch**: `007-cleanup-naming`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: see `specs/007-cleanup-naming/prompt.md` (the prompt is the authoritative feature description; this spec restates it in Spec Kit form).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The repository reflects the agent/tool distinction, with no behavior change on retained endpoints (Priority: P1)

A developer opens `MovieTracker.Api/` and sees a flat layout that matches the mental model the code already implements: one `Agents/` folder containing one real agent class (the LLM wiring), one `Tools/` folder containing every tool class the agent invokes, plus `Chat/`, `Models/`, `ChatSessions/`, and `Telemetry/` folders holding what their names say. Every **retained** endpoint path, every request and response body shape, every Cosmos document field, every telemetry name, every system prompt string, and every registered tool name is byte-identical to the state before this slice. The only deliberate HTTP-surface changes are the two legacy removals called out in the prompt: `POST /Ask` (removed with `AskController`) and `GET /weatherforecast` (removed with `WeatherForecastController`), which MUST now return 404.

**Why this priority**: The whole slice is a refactor whose only user-visible value is the next developer's ability to reason about the code. There is no second story — if the naming and layout change is done and nothing else moves, the slice is complete. P1 because this is the entire slice; there are no P2 or P3 stories.

**Independent Test**: Manual. Run the API locally and exercise the smoke paths from the prompt's "Done when" section (`GET /health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai`; `GET /api/chat/start`; `POST /api/chat/{chatId}/ask` with a sci-fi recommendation input; `POST /api/chat/{chatId}/ask/v2` with a follow-up; and confirm `POST /Ask` and `GET /weatherforecast` now return 404). Each **retained** endpoint's response body MUST have the same JSON shape and field names as before the slice. `dotnet build` MUST succeed with no new warnings. If a test project exists, `dotnet test` MUST pass.

**Acceptance Scenarios**:

1. **Given** the refactor is complete, **When** a developer lists `MovieTracker.Api/`, **Then** the application-source subfolders are exactly `Agents/`, `Tools/`, `Chat/`, `Models/`, `ChatSessions/`, `Telemetry/`. Non-source folders that the framework or build owns (`Properties/`, `bin/`, `obj/`) and loose root files (`Program.cs`, `HealthEndpoints.cs`, `Dockerfile`, the two `appsettings*.json` files, the `.csproj`, the `.http` file) remain. `Prompts/`, `Core/`, and `Controllers/` no longer exist (FR-003, FR-008).
2. **Given** the refactor is complete, **When** a developer runs a ripgrep for `class\s+\w+Agent\b` across `MovieTracker.Api/` excluding `bin/` and `obj/`, **Then** the only match is the production declaration of `MovieAgent` (FR-001).
3. **Given** the refactor is complete, **When** a developer inspects every `AIFunctionFactory.Create` call site, **Then** every registered method name the model sees is unchanged from the previous slice, and every class-rename call site (for example `wikipediaAgent` → `wikipediaTools`) has its injected field/parameter renamed to match the new class name (FR-002).
4. **Given** the refactor is complete, **When** `GET /api/chat/start` is called, **Then** the response is `{ "chatId": "..." }` with the same property name as before (FR-004).
5. **Given** the refactor is complete, **When** `POST /api/chat/{chatId}/ask` is called with `{ "input": "Recommend a sci-fi movie from the 90s" }`, **Then** the response body contains `funnyFact` and `messages` fields with the same shape as before (FR-004).
6. **Given** the refactor is complete, **When** `POST /api/chat/{chatId}/ask/v2` is called with `{ "input": "Show me its trailer" }`, **Then** the response body contains `funnyFact` and `turn` fields with the same shape as before (FR-004).
7. **Given** the refactor is complete, **When** a Cosmos session document is loaded for a session created before the slice, **Then** every stored field (session `id`, `ChatMessage.role`, JSON type discriminators, record property names) deserialises without migration (FR-005).
8. **Given** the refactor is complete, **When** a developer ripgreps `MovieTracker.Api/` (excluding `bin/` and `obj/`) over `*.cs` files only for declarations matching `class\s+WeatherForecast\b`, `class\s+WeatherForecastController\b`, `class\s+AskController\b`, `class\s+AskRequestDot\b`, `class\s+AskResponseDot\b`, `class\s+AppError\b`, `class\s+Result\b`, or `class\s+ResultExtensions\b`, **Then** zero matches remain (FR-006).
9. **Given** the refactor is complete, **When** `POST /Ask` and `GET /weatherforecast` are called, **Then** both return 404 (FR-006).
10. **Given** the refactor is complete, **When** a developer opens `Program.cs`, **Then** it is a short composition root that calls `AddTelemetry`, `AddMovieTools`, `AddMovieAgent`, `AddChatSessions`, `MapHealthEndpoints`, and `MapChatEndpoints`, each defined in a static class living in the folder of what it registers (FR-007).
11. **Given** the refactor is complete, **When** a developer opens `CLAUDE.md`, **Then** only the "Project Structure" section has changed and it reflects the new flat layout (FR-009).

---

### Edge Cases

- **Tool method names registered via `AIFunctionFactory.Create` are the tool names the model sees**: Renaming a method that is wrapped in `AIFunctionFactory.Create(...)` changes the agent's tool surface, which is a behavior change this slice forbids. Any class rename MUST leave the registered method names intact.
- **The `Name` on the `ChatClientAgentOptions` (`Name = "MovieTracker.Api"`) is telemetry**: This agent name is emitted on spans and logs. It is not renamed by this slice.
- **The logger category `MovieTracker.Api.ToolCalls` is referenced from `appsettings*.json` and telemetry**: The string stays byte-identical even though the implementing class moves.
- **The `Prompts/`, `Core/`, and `Controllers/` folders are only deleted after every file in them has moved or been removed**: An empty folder deletion that silently leaves an undeleted file somewhere else is a slice defect.
- **`MoviceTrackerChatSession` is misspelled in storage-adjacent code**: The class rename to `MovieTrackerChatSession` MUST NOT change the Cosmos `id` field name, the JSON serialisation shape, or any other wire or storage name. Only the C# type name changes.
- **`MovieTracker.Api.http` contains a `weatherforecast` request**: Remove just that request; keep the rest of the file.
- **A deletion target is still referenced somewhere**: Before deleting `WeatherForecast.cs`, `WeatherForecastController.cs`, `AskController.cs`, `AskRequestDot.cs`, `AskResponseDot.cs`, `AppError.cs`, `Result.cs`, or `ResultExtensions.cs`, confirm no reference remains anywhere in the working tree — including any test projects, `MovieTracker.Api.http`, and any other project source — except in the same set of files being removed together in this slice (references among the deletion set are expected and acceptable). If a reference from outside the deletion set is found, stop and report rather than delete and patch.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain exactly one class whose name ends in `Agent` in production code, and that class MUST be `MovieAgent` in `Agents/MovieAgent.cs`, registered through an `AddMovieAgent` extension method. The inline `AIAgent` currently built in `Program.cs` MUST move to this file unchanged in every observable respect (same scoped lifetime, same tools in the same order — TMDb, DateTime, ChatPlanner — same `InstrumentedAIFunction` wrapping, same `ChatClientAgentOptions`).

- **FR-002**: Every method registered with `AIFunctionFactory.Create` MUST keep its exact C# method name across the slice, because that name is the tool name surfaced to the model. Class renames (`Agents/OpenMovieDbAgent` → `Tools/OmdbTools`; `Agents/TrailerAgent` → `Tools/TrailerTools`; `Agents/WikipediaSearchAgent` → `Tools/WikipediaTools`; `Prompts/TheMovieDBKernelFunctions` → `Tools/TmdbTools`; `Prompts/DateTimeKernelFunctions` → `Tools/DateTimeTools`; `Prompts/ChatPlanner` → `Tools/ChatPlanner`) MUST NOT rename any registered method. The dependency-injection identifiers that reference these classes (fields and constructor parameters such as `wikipediaAgent`, `openMovieDbAgent`, `trailerAgent`, and any other same-pattern identifier) MUST be renamed consistently to the new class names (for example `wikipediaTools`, `omdbTools`, `trailerTools`).

- **FR-003**: Under `MovieTracker.Api/`, the application-source subfolders MUST be exactly `Agents/`, `Tools/`, `Chat/`, `Models/`, `ChatSessions/`, `Telemetry/`, each one level deep (no nested subfolders). Framework-owned folders that are not application source (`Properties/`, `bin/`, `obj/`) are unaffected. The old `Prompts/`, `Core/`, and `Controllers/` folders MUST be removed. Every file in those folders MUST either move to the folder dictated by the prompt or be deleted per FR-006. In particular: `Agents/MovieRatingResult`, `Agents/RatingComparisonResult`, every `Agents/Wikidata*` and `Agents/Wikipedia*` record, and `Chat/Ask`, `Chat/ChatMessage`, `Chat/UserChatMessage`, `Chat/AssistantChatMessage`, `Chat/ChatMessageResponse`, `Chat/ChatTurnResponse`, `Chat/MovieListItem`, `Chat/MovieListResponse`, `Chat/MovieTrailerInfo`, `Chat/MovieViewModel` MUST move to `Models/`. Any other top-level type that currently shares a file with another type MUST move to its own file under `Models/` (one type per file, filename equal to type name). `Core/CosmosSystemTextJsonSerializer` MUST move to `ChatSessions/`.

- **FR-004**: Every retained endpoint — the chat endpoints (`GET /api/chat/start`, `POST /api/chat/{chatId}/ask`, `POST /api/chat/{chatId}/ask/v2`) and the health/version endpoints (`GET /health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai`, `/version`) — MUST return the same HTTP status, the same response body shape, and the same field names as before the slice. No retained endpoint path changes. The only HTTP-surface delta is the deliberate removal of the two legacy endpoints `POST /Ask` and `GET /weatherforecast` per FR-006.

- **FR-005**: Cosmos storage names and wire shapes MUST NOT change. `MovieTrackerChatSession.id`, `ChatMessage.role`, every JSON type discriminator, and every record property name stay byte-identical. The system prompt text (`ChatStartSystemPrompt`), every `[Description]` attribute value, and every agent instruction string MUST stay byte-identical.

- **FR-006**: The following files MUST be deleted: `WeatherForecast.cs`, `Controllers/WeatherForecastController.cs`, `Controllers/AskController.cs`, `Models/AskRequestDot.cs`, `Models/AskResponseDot.cs`, `Core/AppError.cs`, `Core/Result.cs`, `Core/ResultExtensions.cs`. The `AddControllers()` and `MapControllers()` calls MUST be removed from `Program.cs`. The `weatherforecast` request MUST be removed from `MovieTracker.Api.http`; every other request in that file stays.

- **FR-007**: `Program.cs` MUST be a short composition root. Each of `AddTelemetry`, `AddMovieTools`, `AddMovieAgent`, `AddChatSessions`, `MapHealthEndpoints`, and `MapChatEndpoints` MUST be a static extension method defined in a static class under the folder of what it registers (`Telemetry/`, `Tools/`, `Agents/`, `ChatSessions/`, root `HealthEndpoints.cs`, and `Chat/` respectively). Registration order that matters for correctness (named `HttpClient` before `IChatClient`) MUST be preserved. The fail-fast config checks and their error messages MUST be preserved verbatim. `AddOpenApi`, `MapOpenApi`, and the HTTPS redirection rules MUST stay as they are.

- **FR-008**: Code style for every changed file MUST be: PascalCase types/methods/properties, camelCase locals/parameters, plain camelCase private fields with no underscore prefix, `I` prefix on interfaces, `Async` suffix on async methods that are not registered as tools, one type per file with filename matching the type name, file-scoped namespaces matching the folder, explicit types (never `var`), braces on every `if`. All existing `///` doc comments and any code comments MUST be removed from changed files. Every `using` and namespace statement MUST be updated for moved files.

- **FR-009**: Only the "Project Structure" section of `CLAUDE.md` MUST change, and it MUST describe the new flat layout. Every other section of `CLAUDE.md` stays byte-identical.

### Key Entities

- **Agent**: The LLM wiring built from `IChatClient` and a set of `AIFunction` tools. There is exactly one in this codebase: `MovieAgent`.
- **Tool**: A C# method the agent invokes, registered through `AIFunctionFactory.Create`. The method name is the tool name the model sees. Tool classes live in `Tools/`.
- **Session**: `MovieTrackerChatSession`, persisted in Cosmos with its `id`, `ChatMessage`s (each with a `role` discriminator), and other records. Storage shape is frozen for this slice.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `dotnet build MovieTracker.Api/MovieTracker.Api.csproj` succeeds with zero new warnings compared to the pre-slice build.
- **SC-002**: If a test project exists, `dotnet test` passes with the same number of passing tests as before the slice (zero regressions). If no test project exists, this criterion is vacuously satisfied.
- **SC-003**: All smoke tests from the prompt's "Done when" section pass locally: the five health endpoints return 200 with the same body shape; `GET /api/chat/start` returns `{ "chatId": "..." }`; both `POST /api/chat/{chatId}/ask` variants return bodies with the expected top-level fields (`funnyFact` + `messages`, and `funnyFact` + `turn` respectively); `POST /Ask` and `GET /weatherforecast` return 404.
- **SC-004**: A ripgrep for `class\s+\w+Agent\b` restricted to `*.cs` files under `MovieTracker.Api/` and excluding `bin/` and `obj/` returns exactly one match, the declaration of `MovieAgent`. A ripgrep for `MovieTracker\.Api\.(Prompts|Core|Controllers)` across the same `*.cs` scope returns zero matches, and the folders `MovieTracker.Api/Prompts`, `MovieTracker.Api/Core`, `MovieTracker.Api/Controllers` do not exist on disk.
- **SC-005**: A ripgrep of the post-slice tree (`MovieTracker.Api/` excluding `bin/`, `obj/`) for `^\s*///` and for `\bvar\s` returns zero matches in files changed by this slice.
- **SC-006**: A file-by-file comparison of the pre-slice and post-slice contents of `appsettings.json`, `appsettings.Development.json`, `MovieTracker.Api.csproj`, every file under `infrastructure/`, every file under `.github/workflows/`, and every extracted system-prompt / tool-description / agent-instruction string (`ChatStartSystemPrompt`, every `[Description]` attribute value, every agent-instruction string literal) shows zero differences. The comparison baseline is the state of each file at the start of this slice; it does not depend on any `git` operation.

## Assumptions

- The slice is executed on a fresh working tree where only `specs/007-cleanup-naming/` and the files listed in `prompt.md` are modified. Older specs (001-006) are not edited, even where they reference old class names or `/health/openai`.
- No new NuGet packages, no new test projects, no new abstractions, no new interfaces are introduced.
- The Docker image, Bicep infrastructure, GitHub Actions workflows, and Cosmos documents in Azure are not touched by this slice.
- `/health/openai` is left as-is (name included), consistent with the prompt's "Keep every other health endpoint unchanged."
- The developer running this slice has .NET 10 SDK installed and can run `dotnet build` / `dotnet run` locally to perform the manual smoke tests.
- The temp scratch directory for anything this slice may write is `C:\projects\temp`, and any file written there is deleted before the slice reports done.

## Out of Scope *(Constitution III)*

**In-scope exceptions called out here so no reviewer or implementer reads the out-of-scope list as blocking them.** The HTTP surface is unchanged **except** for the two deliberate legacy removals: `POST /Ask` and `GET /weatherforecast` go from 2xx/4xx answers to 404. Files **are** edited and removed under `MovieTracker.Api/` per FR-003, FR-006, and FR-007. The repository-root `CLAUDE.md` **is** edited, limited to its "Project Structure" section per FR-009. The feature-folder `specs/007-cleanup-naming/` **is** edited.

Out of scope (reviewers reject work that crosses this):

- Any change to runtime behavior of **retained** endpoints, any change to HTTP endpoint paths other than the two deliberate removals above, any change to request or response JSON shapes on retained endpoints, any change to Cosmos document shapes, config keys, Bicep templates, GitHub Actions workflows, or Docker image contents.
- Any edit to older feature specs under `specs/001-*` through `specs/006-*`, even where those specs name classes that this slice renames or endpoints that this slice removes.
- Any edit to any file outside this feature folder, `MovieTracker.Api/`, the repository-root `CLAUDE.md`, and the temp scratch directory `C:\projects\temp`. Any temp file written to `C:\projects\temp` MUST be deleted before the slice reports done.
- New tests, new NuGet packages, new abstractions, new interfaces, or new configuration keys.
- Any change to system prompt text (`ChatStartSystemPrompt`), `[Description]` attribute strings on tool methods, agent instruction text, or any other string surface the LLM observes.
- Renaming any method that is registered as a tool via `AIFunctionFactory.Create`.
- Renaming telemetry names: agent `Name = "MovieTracker.Api"`, logger category `MovieTracker.Api.ToolCalls`, span names, meter names, source names.
- Renaming or restructuring Cosmos storage keys (`MovieTrackerChatSession.id`, `ChatMessage.role`, JSON type discriminators, record property names).
- Renaming the `/health/openai` endpoint or any other health endpoint.
- Any `git` operation: commits, pushes, branch creation, tagging. The owner commits.
- Any edit to `.gitignore`.
