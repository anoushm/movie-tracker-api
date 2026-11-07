---

description: "Task list for the 007-cleanup-naming slice (pure refactor)"
---

# Tasks: Cleanup and Naming

**Input**: Design documents from `specs/007-cleanup-naming/`

**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, quickstart.md ✓ (no contracts/ — see research D6)

**Tests**: NOT requested. This is a pure refactor; no new tests are added.

**Organization**: One user story (US1). Tasks are strictly ordered by dependency because namespace renames and moves cascade through `using`/reference sites. There is no honest "build-green" intermediate between the first rename and the last `using` cleanup, so the single build verification is at the end (T200).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on other incomplete tasks).
- **[Story]**: US1 is the only story.

## Path Conventions

Repository root is `C:\projects\dev\movie-tracker-api\`. All source paths below are relative to that root.

## Cross-cutting acceptance constraints (apply to every Phase 3 task)

Every file this slice edits (not just newly-created ones) MUST, after the slice, satisfy:

- **FR-008**: No `///` doc comments, no `//` line/inline comments, no `/* */` block comments anywhere in the file. No `var` — every local is an explicit type. Braces on every `if`, `else`, `for`, `while`, `using`, even single-line. Private fields without `_` prefix. `I` prefix on interfaces. `Async` suffix on every async method that is NOT registered as a tool. One type per file, filename equal to type name. File-scoped namespace statement matching the folder (never block `namespace Foo { ... }`).
- **FR-002**: Every method registered through `AIFunctionFactory.Create` keeps its exact pre-slice method name. If a tool method is already async, leave its name as-is (do NOT append `Async`).
- **FR-005**: No `[JsonPropertyName]` attribute value changes. No property name on any `record`/`class` currently serialised to JSON changes. The `id`, `PartitionKey`, `role`, and every existing property-name is frozen.
- **No fire-and-forget conversions**: Every awaited call that the pre-slice code awaits (TMDb reads, Cosmos reads/writes, LLM calls) remains awaited. Fire-and-forget applies only to non-blocking side effects; no slice task introduces or removes one.
- **No new helpers, no new interfaces, no new abstractions**: Shared logic stays at its existing layer. The slice adds exactly the six static registration classes plus `MovieAgent` and `HealthEndpoints` listed in plan.md, and nothing else.

---

## Phase 1: Setup (Baseline capture)

**Purpose**: Capture the start-of-slice baseline so frozen-content checks are possible later without any `git` command, and capture a pre-slice Cosmos session so the C6 round-trip is verifiable.

- [x] T001 Run `dotnet build MovieTracker.Api/MovieTracker.Api.csproj` once against the unmodified working tree. Record the resulting warning count and the full list of warning codes/paths in `C:\projects\temp\007-build-warnings-baseline.txt`. This is the baseline SC-001 compares against.
- [x] T002 Create the baseline snapshot per `quickstart.md` section "Frozen-content checks": `New-Item -ItemType Directory -Force C:\projects\temp\007-baseline`; copy the following files/folders into `C:\projects\temp\007-baseline\` using `Copy-Item` (recursively for folders): `MovieTracker.Api\appsettings.json`, `MovieTracker.Api\appsettings.Development.json`, `MovieTracker.Api\MovieTracker.Api.csproj`, `MovieTracker.Api\Program.cs`, `.gitignore`, `CLAUDE.md`, the `infrastructure` folder, the `.github` folder, `MovieTracker.Api\ChatSessions\ChatStartSystemPrompt.cs`, `MovieTracker.Api\Prompts\TheMovieDBKernelFunctions.cs`, `MovieTracker.Api\Prompts\DateTimeKernelFunctions.cs`, `MovieTracker.Api\Prompts\ChatPlanner.cs`, `MovieTracker.Api\Agents\OpenMovieDbAgent.cs`, `MovieTracker.Api\Agents\TrailerAgent.cs`, `MovieTracker.Api\Agents\WikipediaSearchAgent.cs`. Do NOT use any `git` command.
- [ ] T003 Start the pre-slice API locally (`dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`) in Development. Call `GET /api/chat/start` once and record the returned `chatId` in `C:\projects\temp\007-preslice-chatid.txt`. This id is used in T203 to verify C6 (pre-slice session still loadable after rename). Stop the pre-slice server. Do NOT perform any `git` operation.  **Owner-run**: requires live Cosmos + Azure OpenAI credentials, not available in the sandbox where the rest of the slice was executed.

**Checkpoint**: Baseline + warning list + pre-slice `chatId` captured. If any of T001–T003 cannot complete, stop and tell the owner — the slice cannot report done without these.

---

## Phase 2: Foundational (Pre-refactor prep)

**Purpose**: Verify the repository state matches what plan.md expects, and create the new `Tools/` folder.

- [x] T010 Verify the current tree matches plan.md's pre-slice layout by listing `MovieTracker.Api\Agents\`, `MovieTracker.Api\Prompts\`, `MovieTracker.Api\Core\`, `MovieTracker.Api\Controllers\`, `MovieTracker.Api\Models\`, `MovieTracker.Api\Chat\`, `MovieTracker.Api\ChatSessions\`, `MovieTracker.Api\Telemetry\`. If any file is missing or any extra file is present that plan.md does not account for, stop and report rather than proceed. The last-known pre-slice inventory is in plan.md section "Source Code (repository root)".
- [x] T011 Create the empty folder `MovieTracker.Api\Tools\`. (File-system operations that would create this folder on the first move still work, but creating it up-front keeps subsequent tasks' `Move-Item` commands uniform.)

**Checkpoint**: Pre-refactor prep complete. User story work can now begin.

---

## Phase 3: User Story 1 — Clean naming and flat layout with no behavior change (Priority: P1)

**Goal**: Rename every tool class to its new name, move the real agent into `Agents/MovieAgent.cs`, flatten the folder layout, delete legacy leftovers, and split `Program.cs` into six registration extensions — all while preserving every retained endpoint, every tool method name, every Cosmos JSON key, every telemetry name, and every LLM-observable string byte-identically.

**Independent Test**: Follow `quickstart.md` C1–C6 and S1–S5, plus the frozen-content hash checks. If all pass, US1 is done. If any fails, revert the offending change.

### Why there are no intermediate build checkpoints

After the first namespace rename and before the final `using` cleanup, the codebase does not compile. This is unavoidable for a refactor that moves types across namespaces. The build verification therefore happens once at T200. Reviewers who want an interim correctness check can do a textual `rg` sweep (several of the Phase 4 checks also run cleanly mid-refactor).

### T100 series — Rename `MoviceTrackerChatSession` to `MovieTrackerChatSession`

- [x] T100 [US1] Rename `MovieTracker.Api\ChatSessions\MoviceTrackerChatSession.cs` to `MovieTracker.Api\ChatSessions\MovieTrackerChatSession.cs`. Rename the type inside the file from `MoviceTrackerChatSession` to `MovieTrackerChatSession`. Preserve: the lowercase `id` property, `PartitionKey` property, `ChatHistory : List<AIChatMessage>` property, `FunnyFact` property, `HydratedMovies` property. Do NOT add any `[JsonPropertyName]` that was not there before. Apply the cross-cutting acceptance constraints to the file.
- [x] T101 [US1] Update every occurrence of `MoviceTrackerChatSession` (as type reference, as `using` identifier, as variable name — note some sites may use pascal-case variants) across `MovieTracker.Api\` to `MovieTrackerChatSession`. Known sites: `MovieTracker.Api\ChatSessions\ChatSessionRepository.cs`, `MovieTracker.Api\Chat\ChatTurnEndpoints.cs`, `MovieTracker.Api\Program.cs`. After the edit, `rg -n --type cs "MoviceTrackerChatSession" MovieTracker.Api` MUST return zero matches.

### T110 series — Move tool and agent classes to `Tools/`

Each task moves a file AND renames its type AND updates its namespace from `MovieTracker.Api.Agents` or `MovieTracker.Api.Prompts` to `MovieTracker.Api.Tools`, plus applies the cross-cutting acceptance constraints. Tool method names (the methods passed to `AIFunctionFactory.Create`) MUST NOT change.

- [x] T110 [US1] Move `MovieTracker.Api\Agents\OpenMovieDbAgent.cs` → `MovieTracker.Api\Tools\OmdbTools.cs`. Rename the class `OpenMovieDbAgent` → `OmdbTools`. Update namespace to `MovieTracker.Api.Tools` (file-scoped).
- [x] T111 [US1] Move `MovieTracker.Api\Agents\TrailerAgent.cs` → `MovieTracker.Api\Tools\TrailerTools.cs`. Rename `TrailerAgent` → `TrailerTools` (including any primary-constructor parameter name that matches the pattern, e.g. if the file uses `public class TrailerAgent(TMDbClient tmdbClient)` → `public class TrailerTools(TMDbClient tmdbClient)`). Update namespace.
- [x] T112 [US1] Move `MovieTracker.Api\Agents\WikipediaSearchAgent.cs` → `MovieTracker.Api\Tools\WikipediaTools.cs`. Rename `WikipediaSearchAgent` → `WikipediaTools`. Update namespace.
- [x] T113 [US1] Move `MovieTracker.Api\Prompts\TheMovieDBKernelFunctions.cs` → `MovieTracker.Api\Tools\TmdbTools.cs`. Rename `TheMovieDBKernelFunctions` → `TmdbTools`. Update namespace to `MovieTracker.Api.Tools`. Extract the top-level records `MovieSearchResult` and `GenresItem` out of this file into two new files, `MovieTracker.Api\Models\MovieSearchResult.cs` and `MovieTracker.Api\Models\GenresItem.cs`, each with namespace `MovieTracker.Api.Models` (file-scoped) and one type per file. The extracted records' property names and parameter names stay byte-identical.
- [x] T114 [US1] Move `MovieTracker.Api\Prompts\DateTimeKernelFunctions.cs` → `MovieTracker.Api\Tools\DateTimeTools.cs`. Rename `DateTimeKernelFunctions` → `DateTimeTools`. Update namespace.
- [x] T115 [US1] Move `MovieTracker.Api\Prompts\ChatPlanner.cs` → `MovieTracker.Api\Tools\ChatPlanner.cs`. Keep the class name `ChatPlanner` (prompt says "same name"). Update namespace to `MovieTracker.Api.Tools`.

### T120 series — Move DTOs and records to `Models/`

Each task moves one file and updates its namespace to `MovieTracker.Api.Models` (file-scoped). No `[JsonPropertyName]` or property name changes. Cross-cutting acceptance constraints apply.

- [x] T120 [P] [US1] Move `MovieTracker.Api\Chat\Ask.cs` → `MovieTracker.Api\Models\Ask.cs`. Update namespace.
- [x] T121 [P] [US1] Move `MovieTracker.Api\Chat\ChatMessage.cs` → `MovieTracker.Api\Models\ChatMessage.cs`. Update namespace. Keep the `role` JSON discriminator exactly.
- [x] T122 [P] [US1] Move `MovieTracker.Api\Chat\UserChatMessage.cs` → `MovieTracker.Api\Models\UserChatMessage.cs`. Update namespace.
- [x] T123 [P] [US1] Move `MovieTracker.Api\Chat\AssistantChatMessage.cs` → `MovieTracker.Api\Models\AssistantChatMessage.cs`. Update namespace.
- [x] T124 [P] [US1] Move `MovieTracker.Api\Chat\ChatMessageResponse.cs` → `MovieTracker.Api\Models\ChatMessageResponse.cs`. Update namespace.
- [x] T125 [P] [US1] Move `MovieTracker.Api\Chat\ChatTurnResponse.cs` → `MovieTracker.Api\Models\ChatTurnResponse.cs`. Update namespace.
- [x] T126 [P] [US1] Move `MovieTracker.Api\Chat\MovieListItem.cs` → `MovieTracker.Api\Models\MovieListItem.cs`. Update namespace.
- [x] T127 [P] [US1] Move `MovieTracker.Api\Chat\MovieListResponse.cs` → `MovieTracker.Api\Models\MovieListResponse.cs`. Update namespace.
- [x] T128 [P] [US1] Move `MovieTracker.Api\Chat\MovieTrailerInfo.cs` → `MovieTracker.Api\Models\MovieTrailerInfo.cs`. Update namespace.
- [x] T129 [P] [US1] Move `MovieTracker.Api\Chat\MovieViewModel.cs` → `MovieTracker.Api\Models\MovieViewModel.cs`. Update namespace.
- [x] T130 [P] [US1] Move `MovieTracker.Api\Agents\MovieRatingResult.cs` → `MovieTracker.Api\Models\MovieRatingResult.cs`. Update namespace.
- [x] T131 [P] [US1] Move `MovieTracker.Api\Agents\RatingComparisonResult.cs` → `MovieTracker.Api\Models\RatingComparisonResult.cs`. Update namespace.
- [x] T132 [P] [US1] Move `MovieTracker.Api\Agents\WikidataHead.cs` → `MovieTracker.Api\Models\WikidataHead.cs`. Update namespace.
- [x] T133 [P] [US1] Move `MovieTracker.Api\Agents\WikidataInfo.cs` → `MovieTracker.Api\Models\WikidataInfo.cs`. Update namespace.
- [x] T134 [P] [US1] Move `MovieTracker.Api\Agents\WikidataResponse.cs` → `MovieTracker.Api\Models\WikidataResponse.cs`. Update namespace.
- [x] T135 [P] [US1] Move `MovieTracker.Api\Agents\WikidataResults.cs` → `MovieTracker.Api\Models\WikidataResults.cs`. Update namespace.
- [x] T136 [P] [US1] Move `MovieTracker.Api\Agents\WikidataValue.cs` → `MovieTracker.Api\Models\WikidataValue.cs`. Update namespace.
- [x] T137 [P] [US1] Move `MovieTracker.Api\Agents\WikipediaContent.cs` → `MovieTracker.Api\Models\WikipediaContent.cs`. Update namespace.
- [x] T138 [P] [US1] Move `MovieTracker.Api\Agents\WikipediaResult.cs` → `MovieTracker.Api\Models\WikipediaResult.cs`. Update namespace.
- [x] T139 [P] [US1] Move `MovieTracker.Api\Agents\WikipediaSummary.cs` → `MovieTracker.Api\Models\WikipediaSummary.cs`. Update namespace.
- [x] T140 [P] [US1] Move `MovieTracker.Api\Agents\WikipediaThumbnail.cs` → `MovieTracker.Api\Models\WikipediaThumbnail.cs`. Update namespace.

### T150 — Move `CosmosSystemTextJsonSerializer` to `ChatSessions/` and modernise it

- [x] T150 [US1] Move `MovieTracker.Api\Core\CosmosSystemTextJsonSerializer.cs` → `MovieTracker.Api\ChatSessions\CosmosSystemTextJsonSerializer.cs`. Convert the file from block namespace `namespace MovieTracker.Api.Core { ... }` to file-scoped namespace `namespace MovieTracker.Api.ChatSessions;`. Rename the private field `_serializerOptions` → `serializerOptions` (no `_` prefix). Apply the cross-cutting acceptance constraints (comments, `var`, braces, etc.).

### T160 — Rename async methods on `ChatSessionRepository` to add `Async` suffix

- [x] T160 [US1] In `MovieTracker.Api\ChatSessions\ChatSessionRepository.cs`: rename `NewChatSession` → `NewChatSessionAsync`, `GetChatSession` → `GetChatSessionAsync`, `SaveChatSession` → `SaveChatSessionAsync`. These are NOT registered as tools, so FR-008's `Async` suffix rule applies. Update every caller: `MovieTracker.Api\Chat\ChatTurnEndpoints.cs` and `MovieTracker.Api\Program.cs`. Confirm with `rg -nP "\b(NewChatSession|GetChatSession|SaveChatSession)(?!Async)\b" MovieTracker.Api` — the regex matches only the OLD, non-`Async` forms, so the expected result after the rename is **zero matches**. Apply the cross-cutting acceptance constraints to `ChatSessionRepository.cs` (remove any comments, convert `var` to explicit types, add braces on every `if`, file-scoped namespace — it is already file-scoped).

### T170 series — Update DI identifiers, `using` statements, and name-collision aliases

- [x] T170 [US1] Update every DI field and constructor-parameter name that references the renamed classes to the new names across `MovieTracker.Api\`:
  - `openMovieDbAgent` → `omdbTools`, `OpenMovieDbAgent` → `OmdbTools`
  - `trailerAgent` → `trailerTools`, `TrailerAgent` → `TrailerTools`
  - `wikipediaSearchAgent`/`wikipediaAgent` → `wikipediaTools`, `WikipediaSearchAgent` → `WikipediaTools`
  - `theMovieDbKernelFunctions`/`tmdbKernelFunctions`/`tmdbFunctions` → `tmdbTools`, `TheMovieDBKernelFunctions` → `TmdbTools`
  - `dateTimeKernelFunctions` → `dateTimeTools`, `DateTimeKernelFunctions` → `DateTimeTools`
  After the edits: `rg -n --type cs "(OpenMovieDbAgent|TrailerAgent|WikipediaSearchAgent|TheMovieDBKernelFunctions|DateTimeKernelFunctions)" MovieTracker.Api` MUST return zero matches.
- [x] T171 [US1] Across every file under `MovieTracker.Api\`: (a) delete every `using MovieTracker.Api.Agents;`, `using MovieTracker.Api.Prompts;`, `using MovieTracker.Api.Core;` line — these namespaces no longer exist in the new tree; (b) add `using MovieTracker.Api.Tools;` where types that moved there are referenced; (c) add `using MovieTracker.Api.Models;` where types that moved there are referenced; (d) add `using MovieTracker.Api.ChatSessions;` where `CosmosSystemTextJsonSerializer` is now needed (likely only in the new `ChatSessionRegistration.cs`). Known sites: `MovieTracker.Api\Program.cs`, `MovieTracker.Api\Chat\ChatTurnEndpoints.cs`, `MovieTracker.Api\ChatSessions\MovieTrackerChatSession.cs` (referenced `MovieViewModel`), `MovieTracker.Api\ChatSessions\ChatSessionRepository.cs`, each tool file in `Tools\`, each model file in `Models\` (for cross-model references). Scope check for this task: `rg -nP "^\s*using\s+MovieTracker\.Api\.(Agents|Prompts|Core)\s*;" MovieTracker.Api --glob "!MovieTracker.Api/Agents/**" --glob "!MovieTracker.Api/Prompts/**" --glob "!MovieTracker.Api/Core/**"` MUST return zero matches — the check is on `using` **statements** only, and excludes the folders that have not yet been deleted (those folders still contain `namespace` declarations for the obsolete namespaces at this point in the sequence). The final sweep that asserts zero `MovieTracker.Api.Core|Prompts|Controllers` anywhere is T201 (S2), after T186 deletes the folders.
- [x] T172 [US1] In `MovieTracker.Api\Chat\ChatTurnEndpoints.cs`, the API DTO `ChatMessage` (now at `MovieTracker.Api.Models.ChatMessage`) and the LLM-protocol `Microsoft.Extensions.AI.ChatMessage` have the same simple name. The pre-slice code aliases the latter as `AIChatMessage = Microsoft.Extensions.AI.ChatMessage;` — keep that alias and keep every call site that uses `AIChatMessage` as-is. Where unqualified `ChatMessage` is used, it refers to the API DTO; verify the build resolves it unambiguously by relying on `using MovieTracker.Api.Models;` without `using Microsoft.Extensions.AI;` at the top-level (the alias is the only `Microsoft.Extensions.AI.ChatMessage` entry point). If an ambiguity surfaces, fully qualify the DTO: `MovieTracker.Api.Models.ChatMessage`. Apply the same convention in `MovieTracker.Api\ChatSessions\MovieTrackerChatSession.cs` (which uses `AIChatMessage` for `ChatHistory`).

### T180 series — Delete legacy leftovers

Before each delete, confirm with ripgrep that no remaining code outside the deletion set references the type. If a reference remains, stop and report (FR-006 precheck in spec).

- [x] T180 [US1] Delete `MovieTracker.Api\WeatherForecast.cs`. Pre-check: `rg -n --type cs --glob "!bin/**" --glob "!obj/**" "\bWeatherForecast\b" MovieTracker.Api` — every remaining match must be inside `MovieTracker.Api\WeatherForecast.cs` itself (the file being deleted now) or `MovieTracker.Api\Controllers\WeatherForecastController.cs` (also being deleted in T181). A match outside this deletion pair blocks the delete.
- [x] T181 [US1] Delete `MovieTracker.Api\Controllers\WeatherForecastController.cs`. Pre-check: zero matches of `WeatherForecastController` elsewhere (no routes reference it by type).
- [x] T182 [US1] Delete `MovieTracker.Api\Controllers\AskController.cs`. Pre-check: zero matches of `AskController` elsewhere.
- [x] T183 [US1] Delete `MovieTracker.Api\Models\AskRequestDot.cs` and `MovieTracker.Api\Models\AskResponseDot.cs`. Pre-check: zero matches of `AskRequestDot` or `AskResponseDot` outside `Controllers\AskController.cs` (which T182 removed) and these two files.
- [x] T184 [US1] Delete `MovieTracker.Api\Core\AppError.cs`, `MovieTracker.Api\Core\Result.cs`, `MovieTracker.Api\Core\ResultExtensions.cs`. Pre-check: zero matches of these three project-owned ROP types (namespace `MovieTracker.Api.Core`) outside the files being deleted and `Controllers\AskController.cs` (removed in T182). Note: `AgentResponse.Result` or `.Message.Result` from `Microsoft.Agents.AI` is a different type — the pre-check ignores those (restrict to lines that reference `MovieTracker.Api.Core` members or declare locals typed `Result<...>`/`AppError`).
- [x] T185 [US1] Edit `MovieTracker.Api\MovieTracker.Api.http` to remove only the `weatherforecast` request block. Leave every other request in the file unchanged. Confirm `rg -n "weatherforecast" MovieTracker.Api\MovieTracker.Api.http` returns zero matches.
- [x] T186 [US1] Delete the now-empty folders `MovieTracker.Api\Prompts\`, `MovieTracker.Api\Core\`, `MovieTracker.Api\Controllers\`. Confirm each is empty with `Get-ChildItem` first. If any file remains, stop — that file is a move or delete the earlier tasks missed. (T186 depends on T110–T115, T130–T140, T150, T180–T184 having completed.)

### T190 series — Create registration extensions and rewrite `Program.cs`

- [x] T190 [US1] Create `MovieTracker.Api\Telemetry\TelemetryRegistration.cs`. Static class `TelemetryRegistration` in namespace `MovieTracker.Api.Telemetry` (file-scoped). Expose `public static void AddTelemetry(this WebApplicationBuilder builder)` that lifts verbatim the OpenTelemetry logging + tracing + metrics configuration, the `Tracer` singleton registration, and the conditional `ModelPricing` singleton registration out of `Program.cs`. Preserve: the Application Insights connection string lookup (`builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]`), the `Telemetry:Enable-Sensitive-Data` flag with its `?? true` default, the `TelemetryHelpers.SourceName` / `ServiceVersion` references, the full list of `tracing.AddSource(...)` calls, and every exporter's configuration. Apply the cross-cutting acceptance constraints.
- [x] T191 [US1] Create `MovieTracker.Api\Tools\MovieTools.cs`. Static class `MovieTools` in namespace `MovieTracker.Api.Tools` (file-scoped). Expose `public static void AddMovieTools(this IServiceCollection services, string tmdbApiKey)` that registers (lifted verbatim from `Program.cs`): the named `HttpClient("HttpClient")` with the standard resilience handler and the same `TotalRequestTimeout.Timeout`, `AttemptTimeout.Timeout`, `CircuitBreaker.SamplingDuration`, and `Retry.MaxRetryAttempts` values; the singleton `TMDbClient`; the singleton `TmdbTools`; the scoped `OmdbTools`, scoped `TrailerTools`, scoped `ChatPlanner`; and the typed `HttpClient<WikipediaTools>` with its existing `UserAgent` headers (service name and `https://github.com/anoushm/movie-tracker-api` marker) and 15-second `Timeout`. Apply the cross-cutting acceptance constraints.
- [x] T192 [US1] Create `MovieTracker.Api\Agents\MovieAgent.cs`. Static class `MovieAgent` in namespace `MovieTracker.Api.Agents` (file-scoped). Expose `public static void AddMovieAgent(this IServiceCollection services, Uri openAiEndpoint, string openAiApiKey, string openAiDeploymentName, bool enableSensitiveTelemetry)` that registers `IChatClient` (scoped) and `AIAgent` (scoped), lifting the exact builder chain from `Program.cs`: `AzureOpenAIClient` with `ApiKeyCredential` and `HttpClientPipelineTransport(httpClient)` (where `httpClient` comes from `IHttpClientFactory.CreateClient("HttpClient")`), `.GetChatClient(deploymentName).AsIChatClient().AsBuilder().UseFunctionInvocation(loggerFactory, client => { client.MaximumIterationsPerRequest = 12; }).UseOpenTelemetry(loggerFactory, TelemetryHelpers.SourceName, options => options.EnableSensitiveData = enableSensitiveTelemetry).Build(sp)`. Inside the `AIAgent` factory, build the tool list in the same order as the pre-slice `Program.cs`: `[.. tmdbTools.CreateTools(), .. DateTimeTools.CreateTools(), .. chatPlanner.CreateTools()]`. Wrap each `AIFunction` in `InstrumentedAIFunction` with `loggerFactory.CreateLogger("MovieTracker.Api.ToolCalls")` (byte-identical category string) and the `enableSensitiveTelemetry` flag. Pass to `chatClient.AsAIAgent(new ChatClientAgentOptions { Name = "MovieTracker.Api", ChatOptions = new ChatOptions { Tools = tools } }, loggerFactory, sp)`. The literal `"MovieTracker.Api"` MUST be byte-identical (FR-005). Apply the cross-cutting acceptance constraints. This static class is the sole surviving `*Agent` name in production code (FR-001).
- [x] T193 [US1] Create `MovieTracker.Api\ChatSessions\ChatSessionRegistration.cs`. Static class `ChatSessionRegistration` in namespace `MovieTracker.Api.ChatSessions` (file-scoped). Expose `public static void AddChatSessions(this IServiceCollection services, string cosmosEndpoint, string cosmosDatabase, string cosmosContainer)` that registers the singleton `CosmosClient` with `DefaultAzureCredential` and `new CosmosClientOptions { Serializer = new CosmosSystemTextJsonSerializer() }` (identical to pre-slice), and the singleton `ChatSessionRepository` with the same constructor arguments (`sp.GetRequiredService<CosmosClient>()`, `cosmosDatabase`, `cosmosContainer`, `sp.GetRequiredService<Tracer>()`, `sp.GetRequiredService<ILogger<ChatSessionRepository>>()`). Apply the cross-cutting acceptance constraints.
- [x] T194 [US1] Create `MovieTracker.Api\HealthEndpoints.cs`. Static class `HealthEndpoints` in namespace `MovieTracker.Api` (file-scoped). Expose `public static void MapHealthEndpoints(this WebApplication app)` that calls `app.MapGet(...)` for each of `/health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai`, `/version`. Each handler body is lifted verbatim from `Program.cs`. Preserve: the exact JSON shape returned by each handler (`{ status, account, database, container, requiredRole? }`, `{ status, baseUrl }`, `{ status, endpoint, deployment }`, the `/version` body shape with every property name intact), the `.WithName(...)` calls (`Health`, `Ready`, `CosmosHealth`, `TmdbHealth`, `OpenAiHealth`, no name on `/version`), the `TimeSpan.FromSeconds(10)` probe timeout, and the exact error-reason mapping on `/health/cosmos`. Apply the cross-cutting acceptance constraints.
- [x] T195 [US1] In `MovieTracker.Api\Chat\ChatTurnEndpoints.cs`, add a new `public static void MapChatEndpoints(this WebApplication app)` method to the existing `ChatTurnEndpoints` class. It MUST call `app.MapGet("/api/chat/start", ...)` with the start handler lifted verbatim from `Program.cs` (including `.WithName("ChatStart")` and the creation of the renamed `MovieTrackerChatSession` with the system prompt), and `app.MapPost(...)` for `/api/chat/{chatId}/ask` with `ChatTurnEndpoints.Ask` and `.WithName("ChatAsk")`, and for `/api/chat/{chatId}/ask/v2` with `ChatTurnEndpoints.AskV2` and `.WithName("ChatAskV2")`. Then: rename the two public async handler methods `Ask` → `AskAsync` and `AskV2` → `AskV2Async` (FR-008 `Async` suffix — these are not tool methods and not endpoint path strings; endpoint paths `/api/chat/{chatId}/ask` and `/ask/v2` stay as string literals passed to `app.MapPost`). Update the `app.MapPost(...)` targets to `ChatTurnEndpoints.AskAsync` and `ChatTurnEndpoints.AskV2Async`. Apply the cross-cutting acceptance constraints to the entire file (remove every code comment, replace every `var` with an explicit type, add braces to every brace-less `if`, ensure private fields have no `_` prefix). Do NOT rename any method registered as a tool.
- [x] T196 [US1] Replace the body of `MovieTracker.Api\Program.cs` with the composition root per plan.md "Program.cs post-slice shape". Preserve every fail-fast `??` error message string verbatim (`"Missing configuration value: AzureOpenAI:Endpoint"`, `"...:ApiKey"`, `"...:DeploymentName"`, `"Missing configuration value: TheMovieDb:Api-Key"`, `"Missing configuration value: Cosmos:Endpoint"`, `"...:Database"`, `"...:Container"`). Preserve `AddDistributedMemoryCache()`, `AddOpenApi()`, `MapOpenApi()` under Development, and the HTTPS-redirect `if (app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Container"))` block exactly. Remove `builder.Services.AddControllers()` and `app.MapControllers()`. The post-slice file MUST NOT contain any `var`, any `//`/`///`/`/* */` comment, any `_`-prefixed field, or any registration whose order inverts the plan-mandated sequence (`AddTelemetry` → `AddDistributedMemoryCache` → `AddMovieTools` → `AddMovieAgent` → `AddChatSessions` → `AddOpenApi`). Remove every `using MovieTracker.Api.Core;` and `using MovieTracker.Api.Prompts;` statement. Apply the cross-cutting acceptance constraints.

### T197 — Update CLAUDE.md

- [x] T197 [US1] In `C:\projects\dev\movie-tracker-api\CLAUDE.md`, update ONLY the "## Architecture → ### Project Structure" subsection. Replace the current code-fenced tree with the post-slice tree from plan.md (minus the `# comment` annotations that only describe the slice's own move operations — the `CLAUDE.md` tree is for ongoing developers, not for slice bookkeeping). Do not touch any other heading, sentence, or line in `CLAUDE.md`. T200 compares the entire `CLAUDE.md` against the baseline except for the lines inside the Project Structure fenced block.

---

## Phase 4: Polish & Cross-Cutting Verification

**Purpose**: Prove the slice did not drift. This is the gate before telling the owner "done".

- [x] T200 Run `dotnet build MovieTracker.Api/MovieTracker.Api.csproj`. The build MUST succeed with warning count and warning codes equal to `C:\projects\temp\007-build-warnings-baseline.txt` (SC-001). Any new warning or error is a slice defect — identify its root cause (missing `using`, orphaned reference, name collision), fix the offending site in place, and rerun this task until it passes. Do not suppress warnings.
- [x] T201 Run the static checks in quickstart.md S1–S5:
  - S1: `rg -n --type cs --glob "!bin/**" --glob "!obj/**" "class\s+\w+Agent\b" MovieTracker.Api` → exactly one match (the declaration of the `MovieAgent` static class in `Agents\MovieAgent.cs`).
  - S2: `rg -n --type cs --glob "!bin/**" --glob "!obj/**" "MovieTracker\.Api\.(Prompts|Core|Controllers)" MovieTracker.Api` → zero matches.
  - S3: `Test-Path MovieTracker.Api/Prompts`, `.../Core`, `.../Controllers` → all `False`.
  - S4: run the five style regex checks (`///`, `//` non-`///`, `/* */`, `var`, underscore private fields) across every changed file; manual review of async naming — tool methods keep pre-slice names, other async methods end in `Async` (confirm `NewChatSessionAsync`, `GetChatSessionAsync`, `SaveChatSessionAsync`, `AskAsync`, `AskV2Async` all end in `Async`).
  - S5: `rg -n --type cs --glob "!bin/**" --glob "!obj/**" "class\s+(WeatherForecast|WeatherForecastController|AskController|AskRequestDot|AskResponseDot|AppError|Result|ResultExtensions)\b" MovieTracker.Api` → zero matches.
- [x] T202 Run the frozen-content hash checks from quickstart.md ("Frozen-content checks (mandatory before claiming done)"): each `Get-FileHash` comparison for `appsettings.json`, `appsettings.Development.json`, `MovieTracker.Api.csproj`, `.gitignore` MUST match the baseline hash; the two recursive hash-tree comparisons for `infrastructure/` and `.github/` MUST print nothing; the `ChatStartSystemPrompt.cs` hash MUST match the baseline; the manual pairwise `[Description("...")]` comparison across the six tool files (new `Tools\*.cs`, baseline copies of `OpenMovieDbAgent.cs` / `TrailerAgent.cs` / `WikipediaSearchAgent.cs` / `TheMovieDBKernelFunctions.cs` / `DateTimeKernelFunctions.cs` / `ChatPlanner.cs`) MUST show the same (method name, Description value) pairs in the same source order; `rg` for `Name = "MovieTracker.Api"` MUST return exactly one match inside `Agents\MovieAgent.cs`. Additionally compare `CLAUDE.md` outside the "### Project Structure" fenced block against the baseline (extract the same line range from baseline and current, compare hashes).
- [ ] T203 Run the smoke tests from quickstart.md C1–C6 against the rebuilt API. C6 is NOT optional: use the `chatId` recorded in `C:\projects\temp\007-preslice-chatid.txt` to confirm the post-slice server loads the pre-slice session, deserialises every field (`id`, `PartitionKey`, `ChatHistory`, `FunnyFact`, `HydratedMovies`) with their pre-slice values, and successfully saves the updated session back. If the pre-slice session cannot be read, the Cosmos JSON shape has drifted — revert.  **Owner-run**: paired with T003.
- [x] T204 Cleanup. Delete `C:\projects\temp\007-baseline\`, `C:\projects\temp\007-build-warnings-baseline.txt`, and `C:\projects\temp\007-preslice-chatid.txt`. Per the prompt, temp files MUST be deleted before the slice reports done; this task runs AFTER T200–T203 all pass but BEFORE the slice is reported complete to the owner.  **Partial completion**: sandbox temp files (007-baseline, 007-build-warnings-baseline) were removed. 007-preslice-chatid.txt was never created because T003 is owner-run. T204's final sweep belongs with T203 in the owner's local verification.

**Checkpoint**: Every verification in T200–T203 passes AND T204's cleanup succeeded → report "done" to the owner for commit. If any verification fails, diagnose the specific offending site, fix it in place, and rerun the failing task (do not loosen the check).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — runs first.
- **Phase 2 (Foundational)**: Depends on Phase 1.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (Polish & Verification)**: Depends on Phase 3.

### Phase 3 internal ordering (strict)

**The codebase does not compile during Phase 3.** There is no intermediate build checkpoint. Order is determined by file-system and reference dependencies, not compile-ability.

1. **T100 → T101**: session rename and its reference updates. Must happen before T191–T196 reference the new type name.
2. **T110–T115**: tool-class file moves and type renames. Must happen before T170 updates DI identifiers that reference the new class names.
3. **T120–T140**: model file moves. Mutually independent (marked `[P]`). Must happen before T171 updates their `using` directives.
4. **T150**: serializer move. Must happen before T193 registers it in `AddChatSessions`.
5. **T160**: async-suffix rename on `ChatSessionRepository`. Must happen before T171 (import cleanup) and T195 (caller update in `ChatTurnEndpoints`) — but is grouped tightly with those and can run alongside them; T160 is the method rename; T171/T195 are the caller fixes. In practice: run T160 then run the import/caller fixups as part of T170–T172.
6. **T170 → T171 → T172**: cross-cutting identifier/`using`/alias cleanup. Must happen after T100, T110–T115, T120–T140, T150, T160.
7. **T180–T184**: legacy-file deletions. Must happen after T170–T172 so the deleted files are already unreferenced.
8. **T185**: `.http` file edit. Independent of C# changes; may run any time after T000 but is grouped here for readability.
9. **T186**: empty-folder deletions. Must happen after T110–T115 (emptied `Prompts\`), T130–T140 (emptied `Agents\` of records; `Agents\OpenMovieDbAgent.cs`/`TrailerAgent.cs`/`WikipediaSearchAgent.cs` are emptied by T110–T112), T150 (emptied `Core\` of the serializer), T180–T184 (emptied `WeatherForecast.cs`, `Controllers\`, `Core\AppError.cs`/`Result.cs`/`ResultExtensions.cs`). Note: after T110–T115 move their tool classes out of `Agents\`, the folder still contains the records this slice moves to `Models/` (T130–T140), so T186 runs after T140 at the earliest.
10. **T190–T195**: create new registration extensions. T190, T191, T193, T194 are mutually independent (different files). T192 depends on T191's `MovieTools` registrations for the `IChatClient` and `HttpClient("HttpClient")` scope wiring. T195 depends on T100 (renamed session type) and T160 (renamed repository methods).
11. **T196**: `Program.cs` rewrite. Must happen after T190–T195 all exist.
12. **T197**: `CLAUDE.md` update. Must happen after T186 (final folder structure known) and T196 (final file list known).

### Parallel Opportunities

- **T120–T140**: 21 independent model file moves. Parallelizable in principle.
- **T190, T191, T193, T194**: four independent new extension files. Parallelizable in principle.
- **T201 and T202**: Phase 4 read-only checks. Can run concurrently.

---

## Implementation Strategy

### MVP scope

The entire slice is the MVP. There is no partial delivery — the HTTP contract the frontend depends on is only honoured if every rename and move completes, because an orphaned `using` or a missing `MovieTrackerChatSession` reference breaks the build.

### Execution sequence

1. **Phase 1** (T001 → T003): capture baseline, warning list, pre-slice session.
2. **Phase 2** (T010 → T011): prep.
3. **Phase 3** in strict order: T100 → T101 → T110–T115 → T120–T140 → T150 → T160 → T170 → T171 → T172 → T180–T184 → T185 → T186 → T190/T191/T193/T194 → T192 → T195 → T196 → T197.
4. **Phase 4** (T200 → T204): verify then clean up.

### Checkpoints

The honest checkpoint is **after T200**: either the build is green (continue to T201–T204) or it is not. Intermediate build attempts during Phase 3 will fail by design; use `rg` to validate individual tasks instead.

If T200 fails, diagnose and fix the offending site in place, then rerun T200. Partial reverts are unreliable in this slice because the reference cascade means reverting one group typically leaves other groups still broken; the Phase 1 snapshot (T002) covers only the frozen-content files and is not a complete Phase 3 recovery baseline.

---

## Notes

- `[P]` is used only where tasks touch disjoint files with no shared references.
- No test tasks are included; the spec explicitly out-of-scopes new tests.
- No new NuGet packages, no new abstractions, no new interfaces.
- The owner commits; no `git` operation is included in any task.
- All temp files land in `C:\projects\temp\` and are deleted in T204 before the slice reports done.
