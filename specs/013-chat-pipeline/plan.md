# Implementation Plan: Chat Pipeline

**Branch**: `013-chat-pipeline` | **Date**: 2026-11-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification at `specs/013-chat-pipeline/spec.md` and owner prompt at `specs/013-chat-pipeline/prompt.md`.

## Summary

Move the answer-building logic out of `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` into a new `IChatPipeline` / `ChatPipeline` pair under `MovieTracker.Api/Chat/`, so the endpoints only do HTTP work (routes, request validation, span starts, pipeline call, HTTP result mapping). No behavior change: same URLs, same request and response shapes, same status codes, same answers. Both `/api/chat/{chatId}/ask` (full history) and `/api/chat/{chatId}/ask/v2` (new turn only) run the same code path inside the pipeline — session load, `agent.RunAsync`, telemetry, funny fact, save — and differ only in the hydration branch inside the pipeline. The pipeline owns hydration; the endpoints never hydrate. Alongside, fix the `MovieTracker.Evals` rating cases so both are graded (not `SKIPPED`): investigate the root cause in `Cases/GeneratedCaseBuilder.BuildRatingAsync` + `Tmdb/OmdbClient`, record it in this plan, propose the recommended fix (rating case titles from TMDb `movie/top_rated?page=1`, selected the same way `RunTitleSelector` picks popular titles), and wait for owner approval before `/speckit-implement`.

## Technical Context

**Language/Version**: C# on .NET 10 (ASP.NET Core Web API).

**Primary Dependencies**: `Microsoft.Agents.AI` (AIAgent, `PipelineCompactionStrategy`), `Microsoft.Extensions.AI` (`AIJsonUtilities`, `ChatMessage`, `FunctionCallContent`, `FunctionResultContent`), `Microsoft.Azure.Cosmos` (`CosmosClient`, `CosmosException`), `Microsoft.Extensions.Caching.Distributed` (`IDistributedCache`), `TMDbLib` (`TMDbClient`), `OpenTelemetry` (`Tracer`, `TelemetrySpan`, `Activity`).

**Storage**: Cosmos DB NoSQL (serverless, westus3) for chat sessions. `IDistributedCache` (`AddDistributedMemoryCache`) for hydrated movie view models.

**Testing**: `MovieTracker.Evals` harness (`dotnet run --project MovieTracker.Evals`, needs `az login`). No unit test project today; the eval harness is the integration surface for the refactor.

**Target Platform**: Linux container on Azure Container Apps (Consumption workload profile).

**Project Type**: Web service (ASP.NET Core Web API) with a sibling console eval harness.

**Performance Goals**: Preserve current chat latency and cost; the refactor MUST NOT add RPCs, add hydration side effects, or change telemetry cardinality. `HydratedMovieLimit = 150` session-cache trim unchanged.

**Constraints**: Three chat endpoints are byte-for-byte compatible with `main` on request + response shape, status codes, error bodies, log messages, and span names. `dotnet build movie-tracker-api.slnx` MUST succeed with zero new warnings.

**Scale/Scope**: One API project (`MovieTracker.Api`), one eval harness (`MovieTracker.Evals`). The refactor touches `MovieTracker.Api/Chat/` (new files + edit `ChatTurnEndpoints.cs`), `MovieTracker.Api/Program.cs` (one `Add...` call), optionally one file under `MovieTracker.Api/Telemetry/` for usage logging, two folders in `MovieTracker.Evals` (`Cases/`, `Tmdb/`), one line in `CLAUDE.md`, and `specs/013-chat-pipeline/`. Everything else is untouched.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Managed Identity Over Secrets | No new Azure dependency; no new secret. Reuses existing `CosmosClient` (managed identity) and existing `AzureCliCredential` locally. | PASS |
| II. Infrastructure Is Bicep, Not Clicks | No infrastructure change. `infrastructure/` is explicitly out of scope. | PASS |
| III. Incremental Migration With Declared Boundaries | Spec names what is imported (the chat answer logic) and what is explicitly not (agents, prompts, tools, resolver, critic, Copilot SDK, new endpoints, new config). Slice is deployable and independently verifiable through the eval harness. | PASS |
| IV. Dependencies Prove Themselves | No new dependency. The three existing endpoints keep their behavior; `GET /health/cosmos` stays the real round-trip probe for Cosmos. | PASS |
| V. No Secret Material In Source | No config, no secrets touched. | PASS |

Re-check post Phase 1: still PASS. The chat refactor adds no new RPC, no new secret, no new infra, and no new config value. The proposed eval rating fix adds one new TMDb `movie/top_rated?page=1` request in `MovieTracker.Evals` only (same TMDb API key the harness already uses for `movie/popular`); it adds no new secret, no new infra, and no new config value, and runs outside the API runtime path.

## Project Structure

### Documentation (this feature)

```text
specs/013-chat-pipeline/
├── prompt.md         # Owner prompt (source of truth)
├── spec.md           # Approved spec (Codex approved round 5)
├── plan.md           # This file
├── research.md       # Phase 0 output
├── data-model.md     # Phase 1 output (internal pipeline records + eval records touched)
├── quickstart.md     # Phase 1 output (byte-identical error-contract test plan)
├── contracts/        # Phase 1 output (the three endpoint contracts)
│   ├── chat-start.md
│   ├── chat-ask.md
│   └── chat-ask-v2.md
├── checklists/
│   └── requirements.md
└── tasks.md          # Phase 2 output (NOT created here)
```

### Source Code (repository root)

The refactor touches these paths and no others:

```text
MovieTracker.Api/
├── Chat/
│   ├── ChatTurnEndpoints.cs          # EDIT: HTTP-only (routes, validation, spans, pipeline call, result mapping)
│   ├── ChatStartSystemPrompt.cs      # UNCHANGED
│   ├── IChatPipeline.cs              # NEW (interface)
│   ├── ChatPipeline.cs               # NEW (orchestrator)
│   ├── ChatContextReducer.cs         # NEW (reducer + tool-call summaries moved from ChatTurnEndpoints)
│   ├── MovieHydrator.cs              # NEW (ToClientMessageAsync, ProcessMovieAsync, SafeProcessMovieAsync, LoadHydrationCache)
│   └── ChatPipelineRegistration.cs   # NEW (AddChatPipeline extension)
├── Telemetry/
│   └── UsageLogger.cs                # NEW (LogUsage as an instance method; DI-registered)
└── Program.cs                        # EDIT: one AddChatPipeline() call

MovieTracker.Evals/
├── Cases/
│   ├── GeneratedCaseBuilder.cs       # EDIT: BuildRatingAsync uses rating titles
│   ├── RunTitleSelector.cs           # EDIT (threading decided in tasks)
│   ├── RunTitle.cs                   # UNCHANGED (the same record is reused for rating titles)
│   └── (others)                      # UNCHANGED
└── Tmdb/
    └── TmdbClient.cs                 # EDIT: + GetTopRatedAsync (parallel to GetPopularAsync)

CLAUDE.md                             # EDIT: one line under Project Structure Chat/

specs/013-chat-pipeline/              # Plan artifacts
```

**Structure Decision**: single-project API with a sibling evals harness. The refactor stays entirely inside `MovieTracker.Api/Chat/` (new + edits), one `Program.cs` line, one `Telemetry/` file, and the eval fix inside `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/`. The one-line `CLAUDE.md` edit is scoped to the `Chat/` bullet.

## File Split Proposal (owner-approved)

**Owner approved on 2026-11-24.** Implementation uses this split.

| New / Edited file | Contents moved from `ChatTurnEndpoints.cs` | Rationale |
|---|---|---|
| `MovieTracker.Api/Chat/IChatPipeline.cs` | — | Single interface the endpoints call. Keeps the HTTP boundary one-way: endpoints never reach past `IChatPipeline`. |
| `MovieTracker.Api/Chat/ChatPipeline.cs` | `AskAsync` body + `AskV2Async` body deduplicated into shared turn code; `SaveSessionAsync`; `RunTurnAsync`; `SafeGenerateFunnyFactAsync`. `MaxModelTurns`, `HydratedMovieLimit` land here as `private const int`. `StructuredOutputJsonOptions` and `MovieListResponseFormat` land here as **readonly instance fields** initialized in the constructor (not `private static readonly`) — playbook NET-8 forbids private statics on non-static classes for new/changed code; the current private statics in `ChatTurnEndpoints.cs` are grandfathered on `main` but this slice is moving them, so the move converts them to instance fields. Schema and inference options are preserved byte-for-byte (`RequireAllProperties = true`, `DisallowAdditionalProperties = true`). | Orchestrator. One place to add agents later (Option B). |
| `MovieTracker.Api/Chat/ChatContextReducer.cs` | `ContextReducer`, `SummariseToolCalls`, `SummariseArguments`, `SummariseResult`, `NameOf` — all become **instance members** of `ChatContextReducer` (playbook NET-8 forbids private statics on non-static classes for new/changed code; the current private static is grandfathered on `main` but this slice is changing that file). The `IChatReducer` instance is held on a readonly instance field initialized in the constructor. The three summariser methods take their `CompactionMessageGroup` / `IDictionary<string, object?>` / `string` inputs and are called through an instance. Constants `CollapsedToolResultChars`, `CollapsedArgumentChars`, `CollapsedResultNames` land here as `private const int` (compile-time constants, not runtime statics; NET-8 targets `private static` runtime members). | Reducer + tool-call summary is cohesive and has zero coupling to the rest of the pipeline; isolates the `#pragma warning disable MAAI001` scope. |
| `MovieTracker.Api/Chat/MovieHydrator.cs` | `ToClientMessageAsync`, `ProcessMovieAsync`, `SafeProcessMovieAsync`, `LoadHydrationCache`. | Hydration is called twice (once per endpoint), with different inputs (full history vs single new turn). One class owns it; the pipeline calls it from both branches. |
| `MovieTracker.Api/Chat/ChatPipelineRegistration.cs` | — | One `AddChatPipeline` extension method. Registers `IChatPipeline`, `ChatContextReducer`, `MovieHydrator`, and (if moved) `UsageLogging`. |
| `MovieTracker.Api/Telemetry/UsageLogger.cs` | `LogUsage`. Becomes an instance method on `UsageLogger`, which takes `ILogger<UsageLogger>` and `ModelPricing?` through DI (playbook NET-36 — no static method with injected dependencies as parameters). | Lives next to the existing `Telemetry` static class (cost/token helpers) rather than in `Chat/`. Registered in `ChatPipelineRegistration`. |
| `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` | Keeps `MapChatEndpoints` + three endpoint lambdas. Each lambda: start span, read + validate the request, call `IChatPipeline`, map outcome to HTTP result. | HTTP-only. |

Constant migration summary:

| Constant | Current value | New home |
|---|---|---|
| `MaxModelTurns` | 12 | `ChatPipeline` |
| `CollapsedToolResultChars` | 160 | `ChatContextReducer` |
| `CollapsedArgumentChars` | 60 | `ChatContextReducer` |
| `CollapsedResultNames` | 6 | `ChatContextReducer` |
| `HydratedMovieLimit` | 150 | `ChatPipeline` |
| `StructuredOutputJsonOptions` | — | `ChatPipeline` |
| `MovieListResponseFormat` | — | `ChatPipeline` |

Owner approval recorded (2026-11-24).

## IChatPipeline Observable Behavior

The exact method signatures and outcome types are a `/speckit-tasks` and `/speckit-implement` decision. The plan fixes only the observable behavior the interface must guarantee:

- **One entry point per endpoint action.** Each of the three chat endpoints calls `IChatPipeline` and nothing else beyond request validation, span start, and HTTP result mapping. Session start, session load, turn body, hydration, and save are all behind that interface.
- **One shared code path for the turn body.** Both ask endpoints route through the same turn body code (session load, context reducer, `agent.RunAsync`, telemetry, usage logging, funny fact parallel start + await + assign, session save). Only the hydration branch differs.
- **Hydration is owned by the pipeline, branched per endpoint.** `/ask` → hydrate the full chat history. `/ask/v2` → hydrate only the newly produced assistant message. Each branch preserves `main`'s per-endpoint cache writes and failure points exactly.
- **Chat-not-found is a distinct result, not an exception.** The pipeline catches `CosmosException { StatusCode: HttpStatusCode.NotFound }` internally when loading the session and surfaces a result shape that the endpoint maps to `404 {"error":"Chat not found"}`. Any other exception bubbles out and the endpoint's existing outer try/catch returns `503 {"error":"Chat request failed."}` with the same log line as today.
- **Session start remains a separate entry.** `/api/chat/start` has its own pipeline entry point that constructs the initial `ChatHistory` with `ChatStartSystemPrompt.Text` and returns the new session id. Failure here yields `503 {"error":"Session creation failed."}` with log `Chat-Start session creation failed.`.

Whether the pipeline exposes one shared ask method (with a parameter/mode) or two public methods that delegate to a shared private helper is left to `/speckit-tasks` / `/speckit-implement`, provided the constraints above are met. Whichever shape is chosen, the two endpoints MUST reach the same shared turn body code.

Shared internal path inside `ChatPipeline`:

```text
AskAsync(chatId, input)                AskV2Async(chatId, input)
         │                                    │
         └──────► LoadOrNotFound(chatId) ◄────┘
                           │
                           ▼
                 RunTurnAsync(session, input)
                 (one body: funny-fact parallel start, reducer, agent.RunAsync,
                  telemetry, usage logging, funny-fact await + assign, returns
                  the new assistant AIChatMessage?)
                           │
            ┌──────────────┴──────────────┐
            ▼                             ▼
   HydrateHistoryAsync(session)    HydrateNewTurnAsync(newAssistant)
            │                             │
            └──────────────┬──────────────┘
                           ▼
                 SaveSessionAsync(session, cache)
```

The hydration branch differs; everything above and below is shared. This preserves `main`'s per-endpoint cache contents and failure points exactly: `/ask` can fail hydrating any history message (as today); `/ask/v2` only hydrates the new turn (as today).

Endpoint handler shape (exact wiring left to `/speckit-tasks` / `/speckit-implement`):

1. `using TelemetrySpan activity = tracer.StartActiveSpan("<one of the three span names>");`
2. For `/ask` and `/ask/v2`: validate content type; deserialize `Ask`; if any of `!HasJsonContentType()`, thrown `JsonException`, or `ask is null`, return `400 {"error":"Invalid request, missing ask object"}`.
3. Call the pipeline method (one call).
4. Map the outcome to the matching `Results.*` — `Ok` → `200` with the matching DTO (`ChatMessageResponse` or `ChatTurnResponse`); `NotFound` → `404 {"error":"Chat not found"}`.
5. Outer `try/catch` and 503 responses — each endpoint uses its exact `main` log line:
   - `/api/chat/start`: `logger.LogError(ex, "Chat-Start session creation failed.");` → `503 {"error":"Session creation failed."}`.
   - `/api/chat/{chatId}/ask`: `logger.LogError(ex, "Chat-Ask failed for session {ChatId}", chatId);` → `503 {"error":"Chat request failed."}`.
   - `/api/chat/{chatId}/ask/v2`: `logger.LogError(ex, "Chat-Ask-V2 failed for session {ChatId}", chatId);` → `503 {"error":"Chat request failed."}`.

## DI Lifetimes (FR-010)

Verified against `MovieTracker.Api/Program.cs`, `ChatSessions/ChatSessionRegistration.cs`, `Tools/MovieToolsRegistration.cs`, `Agents/MovieAgent.cs`, `Telemetry/TelemetryRegistration.cs`.

| Dependency | Lifetime on `main` | Source |
|---|---|---|
| `CosmosClient` | Singleton | `ChatSessionRegistration` |
| `ChatSessionRepository` | Singleton | `ChatSessionRegistration` |
| `TMDbClient` | Singleton | `MovieToolsRegistration` |
| `IDistributedCache` | Singleton | `Program.cs` → `AddDistributedMemoryCache` |
| `IConfiguration` | Singleton (framework) | ASP.NET host |
| `ILogger<T>` | Singleton (framework) | ASP.NET host |
| `Tracer` | Singleton | `TelemetryRegistration` |
| `ModelPricing` (optional) | Singleton | `TelemetryRegistration` |
| `ChatPlanner` | Scoped | `MovieToolsRegistration` |
| `IChatClient` | Scoped | `MovieAgent` |
| `AIAgent` | Scoped | `MovieAgent` |

**Logger type (LOG-1 deviation)**: every logger injection in this slice uses `Microsoft.Extensions.Logging.ILogger<T>`, matching the existing repo pattern (`ChatSessionRepository`, `MovieAgent`, every registration here uses `ILogger<T>` on `main`). Playbook LOG-1 prefers Serilog `ILogger`; introducing Serilog is an infrastructure change (new package, `logsettings.json`, `Program.cs` config) that is explicitly out of scope for this slice (owner prompt: "Do not add anything not listed in Scope"), and the spec requires byte-identical log messages which the current `ILogger<T>` + `LogInformation`/`LogWarning`/`LogError` call sites produce. Repo-specific guidance takes precedence over the playbook per `C:\projects\CLAUDE.md`. Net effect: this slice matches the project's existing logger pattern and defers the Serilog migration to a later slice.

Chosen lifetimes for new registrations:

| New registration | Lifetime | Rationale |
|---|---|---|
| `IChatPipeline` (`ChatPipeline`) | Scoped | Depends on scoped `AIAgent` and scoped `ChatPlanner`. A singleton would capture those and leak their per-request graph; scoped matches their lifetime. |
| `MovieHydrator` | Scoped | Depends on `TMDbClient` (singleton), `IDistributedCache` (singleton), and takes `ILogger` — safe as singleton on paper, but per-request `ILogger<T>` captures scope context cleanly and matches `IChatPipeline`'s lifetime; scoped keeps the graph consistent and avoids accidental singleton-of-scoped capture if a later slice adds a scoped dependency. |
| `ChatContextReducer` | Singleton | Stateless; only configures a `PipelineCompactionStrategy` and formats strings. Reused across requests; no scoped capture. Exposes a method that returns the configured `IChatReducer`. |
| `UsageLogger` | Singleton | Instance method per playbook NET-36 (no static with injected services). Dependencies: `ILogger<UsageLogger>` plus `ModelPricing?` resolved optionally via factory — `services.AddSingleton(sp => new UsageLogger(sp.GetRequiredService<ILogger<UsageLogger>>(), sp.GetService<ModelPricing>()))` — because `ModelPricing` is only registered when configured (`TelemetryRegistration.cs:18-25`). `GetService<T>()` returns `null` when `ModelPricing` is absent; the constructor accepts `ModelPricing?` and skips cost recording in that case (same as `main`). |

`ChatPipelineRegistration.AddChatPipeline` registers `IChatPipeline` and `MovieHydrator` as `Scoped`, and `ChatContextReducer` and `UsageLogger` as `Singleton`. Called from `Program.cs` immediately after `AddChatSessions`.

## Rating SKIPPED root cause (FR-013)

### The three `NoTruth` branches in `BuildRatingAsync`

`MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs:210-232` has the rating case builder. Three branches return `NoTruth`:

- **Branch A — missing `ImdbId`**: `GeneratedCaseBuilder.cs:219` `if (string.IsNullOrWhiteSpace(details.ImdbId) || year is null)` — first operand. `details.ImdbId` comes from `TmdbClient.GetMovieDetailsAsync` (`TmdbClient.cs:68-77`), which reads `external_ids.imdb_id` and normalizes whitespace to null.
- **Branch B — missing release year**: `GeneratedCaseBuilder.cs:219` — second operand. `year` comes from `ReleaseYearExtractor.Extract(details.ReleaseDate)`.
- **Branch C — empty rating string from OMDb**: `GeneratedCaseBuilder.cs:225-228`. `OmdbClient.GetImdbRatingAsync` (`OmdbClient.cs:27-79`) returns `null` when OMDb returns an `imdbRating` that is absent, not a JSON string, empty/whitespace, or the literal `"N/A"` (case-insensitive).

### Which branch triggered on Nov 18

**Evidence available**:

- `MovieTracker.Evals/results/last-run.json` (Nov 18 run, `runAt = 2026-11-18T23:31:04`) records both rating cases with `skipped: true`, `reason: "no truth value"`. The result file does **not** retain which branch fired, the rating-case titles used, or what OMDb returned — so the **historical branch and OMDb response cannot be confirmed from available evidence**.
- `identifyByPlot-2` from the same run records the slot-2 TMDb id as `1507696` ("Apocalypse Z: New World"), consistent with TMDb `movie/popular?page=1` being dominated by very recent releases.
- Code inspection: `TmdbClient.GetMovieDetailsAsync` appends `external_ids` and normalizes `imdb_id`. TMDb typically assigns an `imdb_id` on the record, and `release_date` is normally populated. The common path on popular page 1 is that `details.ImdbId` and `year` both exist and OMDb is queried.
- `OmdbClient.cs:71-75` treats `"N/A"` and empty rating strings as "no rating" — a design choice that translates OMDb's "we have no rating for this new release" into a `NoTruth` case upstream.

**Most likely branch (hypothesis)**: **Branch C** — `details.ImdbId` and `year` both exist, OMDb is called, and OMDb returns `imdbRating = "N/A"` (or empty), which `OmdbClient.GetImdbRatingAsync` collapses to `null`. This is a **hypothesis** grounded in code inspection plus the observed shape of popular page 1, not a confirmed historical finding; the Nov 18 OMDb return is not recoverable.

**Evidence used to narrow the cause**: code inspection of the three `NoTruth` branches, cross-referenced with the `OmdbClient` normalization rules and the observed character of TMDb popular page 1 (as seen in the preserved `identifyByPlot-2` title). A repro against today's `movie/popular?page=1` would show the same mechanism pattern but cannot confirm what happened on Nov 18 specifically.

### Proposed fix (owner approval required)

**Recommended**: switch the rating cases' title source to TMDb `movie/top_rated?page=1`, selected with the same rule `Cases/RunTitleSelector.SelectAsync` uses today for popular titles — pick 2 distinct indices at random over the returned page, then use their titles in slot order.

**Why this fix matches the evidence**: `movie/top_rated` is TMDb's long-standing ranking of highly-rated titles, dominated by canonical releases (The Godfather, Shawshank, etc.) that OMDb reliably carries an IMDb rating for. Switching the source eliminates Branch C on the common path. Branches A and B remain possible edge cases and keep their existing `NoTruth` behavior (preserved by FR-014).

**Alternatives considered**:

- **Filter popular page 1 to titles with IMDb ratings**: requires a second OMDb pass per title, duplicates the selection logic, and can empty-out the candidate list. Rejected.
- **Pre-check `details.ImdbId` + `year` before picking**: does not address Branch C, which is the common cause.
- **Hard-code two canonical rating titles**: removes randomness. Rejected — random selection is a stated rule of `RunTitleSelector`.

**Scope of change under the recommended fix**:

- `MovieTracker.Evals/Tmdb/TmdbClient.cs`: add `GetTopRatedAsync()` parallel to `GetPopularAsync()`, same `TryParseSummary` path, URL `movie/top_rated?api_key=...&page=1`.
- `MovieTracker.Evals/Cases/RunTitleSelector.cs`: either extend with a `SelectRatingTitlesAsync()` method that calls `GetTopRatedAsync()` and reuses the pick-two-distinct-indices body, or extract that body into a private helper. `/speckit-tasks` decides.
- `MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs`: the `RatingCheck.CheckName` branch of `BuildAsync` uses the rating title for its slot instead of the popular `selected` run title. `/speckit-tasks` decides the exact threading, staying entirely inside `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/`.

**What stays the same**: `Checks/RatingCheck`, `Checks/RatingExpected`, the rating question template in `eval-movie-templates.json`, every other `BuildXxxAsync` method, the harness output shape, the per-check order, the popular-title preamble, and every file outside `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/`.

**Owner approved on 2026-11-24**: rating cases take their titles from TMDb `movie/top_rated?page=1`, selected by the same rule `Cases/RunTitleSelector` uses today for popular titles. Implementation follows this fix.

## Scope Guard

The plan expects changes only in:

- `MovieTracker.Api/Chat/` (new: `IChatPipeline.cs`, `ChatPipeline.cs`, `ChatContextReducer.cs`, `MovieHydrator.cs`, `ChatPipelineRegistration.cs`; edit: `ChatTurnEndpoints.cs`)
- `MovieTracker.Api/Program.cs` (one `AddChatPipeline()` call)
- `MovieTracker.Api/Telemetry/UsageLogger.cs` (new, instance class for `LogUsage`)
- `MovieTracker.Evals/Cases/` (edit: `GeneratedCaseBuilder.cs`, `RunTitleSelector.cs`)
- `MovieTracker.Evals/Tmdb/` (edit: `TmdbClient.cs`)
- `CLAUDE.md` (one line under Project Structure, `Chat/` bullet)
- `specs/013-chat-pipeline/` (plan artifacts)

Explicitly unchanged in this slice:

- `.gitignore`
- `infrastructure/` and any Bicep template
- Every spec in `specs/001-*` through `specs/012-*`
- `MovieTracker.Api/Models/`, `MovieTracker.Api/ChatSessions/`, `MovieTracker.Api/Agents/`, `MovieTracker.Api/Tools/`, `MovieTracker.Api/Azure/`, `MovieTracker.Api/HealthEndpoints.cs`
- `MovieTracker.Api/Telemetry/Telemetry.cs`, `MovieTracker.Api/Telemetry/TelemetryRegistration.cs`, `MovieTracker.Api/Telemetry/InstrumentedAIFunction.cs`, `MovieTracker.Api/Telemetry/ModelPricing.cs` (unless the owner approves folding `UsageLogger` into one of these instead of adding `UsageLogger.cs`)
- `MovieTracker.Evals/Checks/`, `MovieTracker.Evals/Agent/`, `MovieTracker.Evals/Reporting/`, `MovieTracker.Evals/Program.cs`, `MovieTracker.Evals/EvalRunner.cs`, `MovieTracker.Evals/HarnessHost.cs`, `MovieTracker.Evals/ConfigChecker.cs`, `MovieTracker.Evals/ProjectRoot.cs`
- `MovieTracker.Evals/eval-movie-templates.json`, `MovieTracker.Evals/eval-movie-cases.json`
- `MovieTracker.Evals/results/` — unchanged by this slice. The eval harness overwrites `results/last-run.json` on every run; after the FR-018 verification run, the quickstart restores the file from `main`'s baseline so the working tree matches the owner prompt's allowed-changed-paths list. The slice's eval verification does not leave `last-run.json` modified.
- `movie-tracker-api.slnx`, `global.json`, `.editorconfig`
- Dockerfile, `.github/workflows/`

No commit, no push, no PR from the automation. The owner commits and ships.

## Phase 0 Output: Research

See [research.md](./research.md). Resolves the two owner-approvable items (file split, rating fix) and documents the shape decisions behind `IChatPipeline`.

## Phase 1 Output: Design Artifacts

- [data-model.md](./data-model.md) — internal pipeline outcome records and the eval records touched by the rating fix.
- [contracts/chat-start.md](./contracts/chat-start.md) — `GET /api/chat/start` contract.
- [contracts/chat-ask.md](./contracts/chat-ask.md) — `POST /api/chat/{chatId}/ask` contract.
- [contracts/chat-ask-v2.md](./contracts/chat-ask-v2.md) — `POST /api/chat/{chatId}/ask/v2` contract.
- [quickstart.md](./quickstart.md) — byte-identical error-contract verification on `013-chat-pipeline`, using the contract files + `ChatTurnEndpoints.cs` as the non-mutating baseline evidence (no cross-branch git operations, no infrastructure changes).

## Complexity Tracking

No constitution violations. No deviations to justify.
