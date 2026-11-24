---
description: "Task list for 013-chat-pipeline"
---

# Tasks: Chat Pipeline

**Input**: Design documents from `specs/013-chat-pipeline/` — `prompt.md` (owner), `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/*.md`, `quickstart.md`.

**Prerequisites**: plan.md (required) and spec.md (required for user stories) are both present and approved; the owner has approved both gate items (file split and the rating-fix title source).

**Tests**: no new test projects or test tasks — the slice is validated through `dotnet build movie-tracker-api.slnx` and the `MovieTracker.Evals` harness, per `plan.md` §"Technical Context" and `quickstart.md`.

**Organization**: Tasks are grouped by user story. A compact mapping to the owner prompt's A-F phase labels is included under each phase heading for traceability.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story label for Phase 3+ tasks; omitted for Setup / Foundational / Polish

## Path Conventions

- All paths are repository-relative to `C:\projects\dev\movie-tracker-api`.
- `MovieTracker.Api/` = ASP.NET Core API project.
- `MovieTracker.Evals/` = console eval harness project.

---

## Phase 1: Setup

**Purpose**: project initialization — the refactor needs none. Step 0 of `prompt.md` already created the branch and verified the working tree, so there is no new setup task to run from automation (`prompt.md` §Rules: "Git only for step 0").

- [X] T001 No-op setup. Step 0 of `prompt.md` already synced `main`, created the branch `013-chat-pipeline`, and left the working tree clean except for `specs/013-chat-pipeline/` and the pre-existing untracked files. No automation command runs here.

---

## Phase 2: Foundational (Blocking Prerequisites — Owner prompt Phase A + Phase B)

**Purpose**: create the new pipeline files and wire DI. Blocks every user story because the endpoints can only be cut over once the pipeline resolves at startup.

**⚠️ CRITICAL**: no user-story work (Phase 3+) can begin until Phase 2 completes.

Scaffolding (T002-T005 create brand-new files; they are [P]-able among themselves). T006 depends on the new files being referenceable. T007 wires everything into DI. T008 edits Program.cs.

- [X] T002 [P] Create `MovieTracker.Api/Chat/IChatPipeline.cs` with the pipeline interface. The interface MUST expose exactly one entry per endpoint action (chat start, `/ask`, `/ask/v2`); session loading is internal to `ChatPipeline` and not part of the public surface. Each ask entry MUST surface a distinct non-exceptional chat-not-found outcome the endpoint can switch on without catching `CosmosException`. The concrete method count and outcome type hierarchy are task-level choices respecting `plan.md` §"IChatPipeline Observable Behavior" (`/ask` returns the full hydrated history outcome, `/ask/v2` returns the hydrated-new-turn outcome). File-scoped namespace `MovieTracker.Api.Chat`. No `var`, braces on every `if`, no code comments.
- [X] T003 [P] Create `MovieTracker.Api/Chat/ChatContextReducer.cs`. Move the `PipelineCompactionStrategy` reducer and the four summariser methods (`SummariseToolCalls`, `SummariseArguments`, `SummariseResult`, `NameOf`) from the current `MovieTracker.Api/Chat/ChatTurnEndpoints.cs:59-169` into the new class so that their **behavior and output are identical** to `main` for the same inputs. The mechanical text changes required by the move are allowed: class-wrapping into `ChatContextReducer`, file header + file-scoped namespace, converting the private-static reducer field and the four private-static helper methods to instance members (NET-8), `this.` prefix on instance member access (NET-7). No logic change. The `IChatReducer` is a readonly instance field initialized in the constructor. Expose it via a public `Reducer` property or an `AsChatReducer()` method (your choice; stay minimal). Keep the `#pragma warning disable MAAI001` / `#pragma warning restore MAAI001` pair scoped to this file. Constants `CollapsedToolResultChars = 160`, `CollapsedArgumentChars = 60`, `CollapsedResultNames = 6` land here as `private const int`. Acceptance: the four summariser methods and the reducer produce the same strings for the same inputs as `main`.
- [X] T004 [P] Create `MovieTracker.Api/Chat/MovieHydrator.cs`. Move `ToClientMessageAsync`, `ProcessMovieAsync`, `SafeProcessMovieAsync`, and `LoadHydrationCache` from `MovieTracker.Api/Chat/ChatTurnEndpoints.cs:344-646` into the new class so that their **behavior and output are identical** to `main` for the same inputs. The mechanical text changes required by the move are allowed: class-wrapping, file header + file-scoped namespace, converting private-static helpers to instance methods (NET-8), `this.` prefix on instance member access (NET-7), replacing the current `ILogger` method parameter with `this.logger` injected through the constructor (same log templates, same log levels — byte-identical messages). No logic change. Constructor injects `TMDbClient`, `IDistributedCache`, and `ILogger<MovieHydrator>`. File-scoped namespace `MovieTracker.Api.Chat`.
- [X] T005 [P] Create `MovieTracker.Api/Telemetry/UsageLogger.cs`. Move `LogUsage` from `MovieTracker.Api/Chat/ChatTurnEndpoints.cs:429-469` as an instance method on a new `UsageLogger` class (NET-36 — no static with injected deps). Constructor: `public UsageLogger(ILogger<UsageLogger> logger, ModelPricing? modelPricing)` — `ModelPricing?` is the optional-service pattern (resolved via factory in T007). The method skips the cost-recording block when `this.modelPricing` is null, matching `main`'s `if (modelPricing is { } pricing)` guard. `this.` prefix on all instance members. File-scoped namespace `MovieTracker.Api.Telemetry`.
- [X] T006 Create `MovieTracker.Api/Chat/ChatPipeline.cs` implementing `IChatPipeline`. Depends on T002, T003, T004, T005. Orchestrator: owns session start (`ChatSessionRepository.NewChatSessionAsync` with the system-prompt history from `ChatStartSystemPrompt`), session load with try/catch around `CosmosException { StatusCode: HttpStatusCode.NotFound }` returning the distinct not-found outcome, and the shared turn body (reducer via `ChatContextReducer`, `agent.RunAsync`, telemetry recording `TelemetryHelpers.ContextRetainedRatio` and `TelemetryHelpers.ToolCallsPerTurn`, usage logging via `UsageLogger`, funny-fact parallel start + await + assign via `SafeGenerateFunnyFactAsync` behavior kept identical to `main`:406-414 — assigns only when the task returned non-null, retaining the previous `FunnyFact` on null/throw). The `/ask` entry runs this turn body then hydrates the **full** chat history via `MovieHydrator.ToClientMessageAsync` over `ChatHistory`; the `/ask/v2` entry runs the same turn body then hydrates **only** the new assistant message via `MovieHydrator.ToClientMessageAsync` (falling back to `new AssistantChatMessage("No movies were found", new List<MovieViewModel>())` on null hydration — matching `main`:329-331). Both entries end with session save + `HydratedMovieLimit` trim (identical to `main`:353-371 incl. the "Hydration cache for session {ChatId} holds {Count} movies; trimming to {Limit}" info log). `MaxModelTurns = 12` and `HydratedMovieLimit = 150` as `private const int`. `StructuredOutputJsonOptions` and `MovieListResponseFormat` as **readonly instance fields** initialized in the constructor (not `private static readonly`). Constructor injects `ChatSessionRepository`, `AIAgent`, `ChatPlanner`, `ChatContextReducer`, `MovieHydrator`, `UsageLogger`, `IConfiguration`, `ILogger<ChatPipeline>`. Read `AzureOpenAI:DeploymentName` from `IConfiguration` once in the constructor (same `!` null-forgiving as `main`:214). `this.` prefix on all instance members.
- [X] T007 Create `MovieTracker.Api/Chat/ChatPipelineRegistration.cs` with `public static void AddChatPipeline(this IServiceCollection services)` extension. Depends on T002-T006. Registrations:
  - `services.AddSingleton<ChatContextReducer>();`
  - `services.AddSingleton<UsageLogger>(sp => new UsageLogger(sp.GetRequiredService<ILogger<UsageLogger>>(), sp.GetService<ModelPricing>()));` — factory form so `ModelPricing` optional resolution works (NET-36 instance; `ModelPricing` is only registered when configured per `Telemetry/TelemetryRegistration.cs:18-25`).
  - `services.AddScoped<MovieHydrator>();`
  - `services.AddScoped<IChatPipeline, ChatPipeline>();`
  
  File-scoped namespace `MovieTracker.Api.Chat`.
- [X] T008 Edit `MovieTracker.Api/Program.cs` to add `builder.Services.AddChatPipeline();`. Depends on T007. Place the call alongside the existing `AddChatSessions` / `AddMovieTools` / `AddMovieAgent` / `AddTelemetry` / `AddOpenApi` / `AddDistributedMemoryCache` calls. Add `using MovieTracker.Api.Chat;` if not already present (it already is). No other change to `Program.cs`.

**Checkpoint**: after T008 the application compiles and the DI graph resolves `IChatPipeline` at startup — but the endpoints still use the old inline bodies. User-story work begins now.

---

## Phase 3: User Story 1 — One code path builds both ask responses (P1) 🎯 MVP

**Goal**: cut the three chat endpoints over to `IChatPipeline` so both `/ask` and `/ask/v2` share one code path for the turn body and the pipeline owns all hydration.

**Independent Test**: run `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj` on an `az login` session; `GET /api/chat/start` returns `200 { "chatId": ... }`, `POST /api/chat/{id}/ask` returns `200 ChatMessageResponse` with the full hydrated history, `POST /api/chat/{id}/ask/v2` returns `200 ChatTurnResponse` with the new hydrated turn only. Inspect `ChatTurnEndpoints.cs` — it contains no agent call, no reducer, no response-format definition, no funny-fact invocation, no movie hydration, no assistant-JSON parsing, no session save.

Mapping to owner prompt: **Phase C — Endpoint cutover**.

- [X] T009 [US1] Rewrite `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` to be HTTP-only. Depends on T008. Remove everything that is no longer needed by the handlers: `MovieListResponseFormat`, `StructuredOutputJsonOptions`, `ContextReducer`, `SummariseToolCalls`, `SummariseArguments`, `SummariseResult`, `NameOf`, `RunTurnAsync`, `SafeGenerateFunnyFactAsync`, `LogUsage`, `LoadHydrationCache`, `SaveSessionAsync`, `ToClientMessageAsync`, `ProcessMovieAsync`, `SafeProcessMovieAsync`, the `#pragma warning disable MAAI001`/`restore` pair, the five tuning constants, and any now-unused `using` directives. Keep: `MapChatEndpoints` and the three endpoint lambdas. Each lambda MUST:
  - Start its OpenTelemetry span with the exact name (`movie-tracker-api.chat-start`, `movie-tracker-api.chat-ask`, `movie-tracker-api.chat-ask-v2`).
  - For `/ask` and `/ask/v2`: validate `request.HasJsonContentType()`, deserialize `Models.Ask`, and return `400 BadRequest(new { error = "Invalid request, missing ask object" })` on missing content-type, thrown `JsonException`, or `ask is null` — byte-identical to `main`:218-236 and `main`:289-307.
  - Call `IChatPipeline` once.
  - Map the outcome: success → `Results.Ok(new ChatMessageResponse(...))` for `/ask` and `Results.Ok(new ChatTurnResponse(...))` for `/ask/v2`; chat-not-found outcome → `Results.NotFound(new { error = "Chat not found" })`.
  - Outer `try/catch`: `/start` logs `Chat-Start session creation failed.` and returns `503 { error = "Session creation failed." }`; `/ask` logs `Chat-Ask failed for session {ChatId}` and returns `503 { error = "Chat request failed." }`; `/ask/v2` logs `Chat-Ask-V2 failed for session {ChatId}` and returns `503 { error = "Chat request failed." }` — each log line and body byte-identical to `main`:186-190, 266-269, 338-341.
  - Preserve the three route names via `.WithName("ChatStart")`, `.WithName("ChatAsk")`, `.WithName("ChatAskV2")`.
  
  Chat-not-found MUST be delivered by the pipeline as a distinct outcome case — the handler MUST NOT catch `CosmosException` with a status check. `ILogger<ChatMessageResponse>` and `ILogger<ChatTurnResponse>` stay as the injected logger types (matches `main`'s `logger.LogError` type arguments for log category parity). `this.` prefix is not applicable here (static top-level file). Code style: no `var`, braces on every `if`, no code comments, file-scoped namespace `MovieTracker.Api.Chat`. Acceptance: `grep -n "agent\.RunAsync\|ChatStartSystemPrompt\|HydratedMovieLimit\|MovieListResponseFormat\|ContextReducer\|ProcessMovieAsync\|ToClientMessageAsync\|LogUsage\|SummariseToolCalls\|RunTurnAsync" MovieTracker.Api/Chat/ChatTurnEndpoints.cs` returns zero matches.

**Checkpoint**: at this point US1 is complete. Both ask endpoints share one code path in `ChatPipeline`; `ChatTurnEndpoints.cs` is HTTP-only.

---

## Phase 4: User Story 2 — Endpoint error responses byte-for-byte identical (P1)

**Goal**: verify the cutover kept every 400/404/503 body and log message byte-identical to `main`. This is verification-only; the code change happened in T009.

**Independent Test**: grep `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` for the seven byte-identical strings — each present once (or twice for the shared 400 body) inside its handler lambda.

- [X] T010 [US2] Verify the seven exact strings are present in `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` after T009. Run:
  
  ```powershell
  Select-String -Path MovieTracker.Api\Chat\ChatTurnEndpoints.cs -Pattern 'Invalid request, missing ask object|Chat not found|Session creation failed\.|Chat request failed\.|Chat-Start session creation failed\.|Chat-Ask failed for session|Chat-Ask-V2 failed for session'
  ```
  
  Depends on T009. Each pattern MUST appear verbatim at least once in a handler. If any string drifts, fix the handler. Acceptance: the `Select-String` output lists all seven patterns.

**Checkpoint**: US2 verified. Error contracts preserved.

---

## Phase 5: User Story 3 — Single `ChatPipelineRegistration` wires the pipeline into DI (P2)

**Goal**: verify the DI registration and lifetimes match the plan (`ChatPipelineRegistration.cs` only touches its own file; `Program.cs` adds one `AddChatPipeline()` call). The code change happened in T007 + T008.

**Independent Test**: inspect `Program.cs` and `ChatPipelineRegistration.cs`. The DI container resolves `IChatPipeline` at startup without manual wiring; the four new registrations use the lifetimes specified in `plan.md` §"DI Lifetimes".

- [X] T011 [US3] Verify `MovieTracker.Api/Program.cs` calls `builder.Services.AddChatPipeline();` and does not construct `ChatPipeline`, `MovieHydrator`, `ChatContextReducer`, or `UsageLogger` by hand. Depends on T008.
- [X] T012 [US3] Verify `MovieTracker.Api/Chat/ChatPipelineRegistration.cs` registers (a) `ChatContextReducer` as singleton, (b) `UsageLogger` as singleton via the factory that resolves `ModelPricing` optionally via `sp.GetService<ModelPricing>()`, (c) `MovieHydrator` as scoped, (d) `IChatPipeline`/`ChatPipeline` as scoped — nothing else. Depends on T007.

**Checkpoint**: US3 verified. DI graph matches the plan.

---

## Phase 6: User Story 4 — Eval rating cases are graded, not skipped (P1)

**Goal**: switch rating-case titles to TMDb `movie/top_rated?page=1`, selected the same way `RunTitleSelector` picks popular titles today. Every other check keeps the popular titles. Rating check logic and the rating question template MUST NOT change.

**Independent Test**: `dotnet run --project MovieTracker.Evals` on an `az login` session with a working OMDb key; the console table shows `rating-1` and `rating-2` with `PASS` or `FAIL` (not `SKIP` in the common case). Follow the Copy-Item + SHA-256 baseline / restore flow in `quickstart.md` so `results/last-run.json` stays unchanged.

Mapping to owner prompt: **Phase D — Eval fix**.

Constraint: everything stays inside `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/`. `MovieTracker.Evals/EvalRunner.cs` is OUT OF SCOPE and MUST NOT be edited. Verified against `main`: `EvalRunner.cs:59` constructs `new GeneratedCaseBuilder(this.tmdbClient, this.omdbClient)` and `EvalRunner.cs:63` calls `await caseBuilder.BuildAsync(this.templates, runTitles)` — two args each. Neither the constructor nor `BuildAsync` signature may change. The workable threading mechanism is that `GeneratedCaseBuilder` constructs its own `RunTitleSelector` internally from the `tmdbClient` it already has (holds it as a private readonly field) and awaits `SelectRatingTitlesAsync()` **lazily — only inside the `RatingCheck.CheckName` branch, on first rating case encountered in that `BuildAsync` call**. The result is cached in a method-local `IReadOnlyList<RunTitle>?` for the duration of that call. If `eval-movie-templates.json` has no rating template, `SelectRatingTitlesAsync()` is never called and no top_rated request goes out. The `RatingCheck.CheckName` branch uses `ratingTitles[selected.Slot - 1]`; every other branch keeps using the outer `selected` popular title.

- [X] T013 [US4] [P] Add `public async Task<IReadOnlyList<MovieSummary>> GetTopRatedAsync()` to `MovieTracker.Evals/Tmdb/TmdbClient.cs`, parallel to `GetPopularAsync()` at `TmdbClient.cs:28-52`. URL path: `movie/top_rated?api_key={this.apiKey}&page=1`. Reuse `this.GetJsonOrThrowAsync` and `this.TryParseSummary`. Throw `InvalidOperationException` on `results` missing/non-array with message `"TMDb /movie/top_rated?page=1 response missing results array"` (parallel to the popular message style). No other change to `TmdbClient.cs`. `this.` prefix on all instance members. Acceptance: `grep -n "GetTopRatedAsync" MovieTracker.Evals/Tmdb/TmdbClient.cs` returns a hit; `dotnet build` succeeds.
- [X] T014 [US4] Refactor `MovieTracker.Evals/Cases/RunTitleSelector.cs` (depends on T013): extract the "pick 2 distinct indices at random, slot in order" body from `SelectAsync` (`RunTitleSelector.cs:17-45`) into a private helper, then add a sibling public method `SelectRatingTitlesAsync()` that calls `this.tmdbClient.GetTopRatedAsync()` and runs the same helper. Both methods throw with their own precondition message (`"TMDb /movie/popular?page=1 returned fewer than 2 entries; need at least 2"` for popular; `"TMDb /movie/top_rated?page=1 returned fewer than 2 entries; need at least 2"` for top-rated). Keep `this.` prefix (NET-7); no `var`; braces on every `if`. Acceptance: `SelectAsync` and `SelectRatingTitlesAsync` both exist and share the same selection helper body; no duplicate pick-two-distinct-indices block remains.
- [X] T015 [US4] Thread rating titles into `MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs` without changing its public surface (depends on T014). Keep the constructor signature as `GeneratedCaseBuilder(TmdbClient tmdbClient, OmdbClient omdbClient)` and `BuildAsync(IReadOnlyList<CaseTemplate> templates, IReadOnlyList<RunTitle> runTitles)` so `EvalRunner.cs` compiles untouched. In the constructor, construct a private `RunTitleSelector` from the already-injected `TmdbClient` (e.g. `this.ratingTitleSelector = new RunTitleSelector(tmdbClient);`) and hold it as a private readonly field. Inside `BuildAsync`, declare a method-local `IReadOnlyList<RunTitle>? ratingTitles = null;` **before** the loop; **do not** fetch it eagerly. Inside the per-template / per-run-title loop, change the `case RatingCheck.CheckName:` arm so it:
  1. If `ratingTitles is null`, assigns `ratingTitles = await this.ratingTitleSelector.SelectRatingTitlesAsync();` (so the top_rated request only happens when at least one rating case is actually built).
  2. Passes `ratingTitles[selected.Slot - 1]` as the `RunTitle` argument into `BuildRatingAsync` instead of the outer `selected` popular title.
  
  Every other `case ...CheckName:` branch keeps using the popular `selected` RunTitle, unchanged. Acceptance: (a) `Select-String -Path MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs -Pattern 'SelectRatingTitlesAsync|ratingTitles\['` returns hits; (b) the `new GeneratedCaseBuilder(...)` and `caseBuilder.BuildAsync(...)` call sites in `MovieTracker.Evals/EvalRunner.cs` are unchanged from `main` by inspection.

**Checkpoint**: US4 complete. Rating cases take titles from `movie/top_rated`. Eval harness compiles.

---

## Phase 7: User Story 5 — Docs reflect the new `Chat/` layout (P3)

**Goal**: update the `Chat/` bullet in `CLAUDE.md` to name the new files alongside `ChatTurnEndpoints`. Nothing else in `CLAUDE.md` changes.

**Independent Test**: diff `CLAUDE.md` against `main`; the only changed line is the `Chat/` entry under Project Structure.

Mapping to owner prompt: **Phase E — Docs**.

- [X] T016 [US5] [P] Edit the `Chat/` line in `CLAUDE.md` under `Project Structure` (`CLAUDE.md:40` on current branch) to name `ChatPipeline`, `ChatContextReducer`, `MovieHydrator`, `ChatPipelineRegistration`, and `IChatPipeline` alongside the existing `ChatTurnEndpoints (AskAsync, AskV2Async, MapChatEndpoints)` entry. No other line in `CLAUDE.md` changes. Can run in parallel with any other task.

**Checkpoint**: US5 complete.

---

## Phase 8: Polish (Owner prompt Phase F — Verification)

**Purpose**: build check, local smoke test, eval run with baseline restore, and scope-guard check. These run after T001-T016.

- [X] T017 Run `dotnet build movie-tracker-api.slnx` on `013-chat-pipeline`. Depends on T001-T016. Acceptance: succeeds with zero new warnings compared with `main`. The owner compares the warning count against `main` out-of-band (SC-008). If any new warning appears, fix the cause (not the warning filter) before continuing.
- [X] T018 Local smoke test of the three chat endpoints per `quickstart.md` steps 1-9. Depends on T017. Start `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`; issue `GET /api/chat/start` → 200 with chatId; `POST /api/chat/{chatId}/ask` with a valid Ask body → 200 with `ChatMessageResponse` shape; `POST /api/chat/{chatId}/ask/v2` with a valid Ask body → 200 with `ChatTurnResponse` shape; then exercise the three 400 paths (no body, malformed JSON, non-JSON content type), the two 404 paths (`/ask` and `/ask/v2` on an unknown chatId). The 503 paths are verified by code review (T010 covers the log/body strings). Acceptance: each response matches the body described in the contracts under `specs/013-chat-pipeline/contracts/`.
- [X] T019 Eval run and comparison against the Nov 18 baseline. Depends on T017. The Nov 18 baseline is the committed `MovieTracker.Evals/results/last-run.json` on `main` — the harness's own `CheckComparisonComputer` reads it as the previous run before writing the new one, so the per-check comparison lines ARE the Nov 18 comparison. Run `dotnet run --project MovieTracker.Evals` on an `az login` session with a working OMDb key. Capture from the console:
  - the full table (every case row, all columns),
  - the totals line (`Total: {passed} / {total} ({skipped} skipped)`),
  - the per-check comparison (one line per check, in reporter order — each line is `{check} {prev-passed}/{prev-total} -> {curr-passed}/{curr-total} ({curr-skipped} skipped)` where "prev" is the Nov 18 baseline).
  
  Acceptance:
  - `rating-1` and `rating-2` show `PASS` or `FAIL` in the console (not `SKIP` from a missing OMDb rating in the common case).
  - The rating comparison line shows the Nov 18 `0/0 (2 skipped)` transitioning to a graded `p/2` on this run.
  - No check gets worse because of the refactor — applies to every check including rating. If any check drops, rerun once; if the drop persists on the rerun, trace the cause in the diff before reporting (per `prompt.md` §Steps step 4).
  
  The captured full table, totals line, and per-check comparison feed into the T022 Step-5 owner report. `MovieTracker.Evals/results/last-run.json` will be overwritten by the harness — leave it as-is (owner decides whether to keep the new baseline when committing).
- [X] T020 Scope-guard check (SC-009). Depends on T017, T018, T019. Run `git status --porcelain` and inspect the changed-file list. Every path in the output (excluding paths under `specs/013-chat-pipeline/` which are allowed by definition and the untracked `movie-tracker notes.txt` + `specs/012-eval-cleanup/pr-prompt.md` already-untracked files from step 0) MUST be a subset of:
  - `MovieTracker.Api/Chat/IChatPipeline.cs`
  - `MovieTracker.Api/Chat/ChatPipeline.cs`
  - `MovieTracker.Api/Chat/ChatContextReducer.cs`
  - `MovieTracker.Api/Chat/MovieHydrator.cs`
  - `MovieTracker.Api/Chat/ChatPipelineRegistration.cs`
  - `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`
  - `MovieTracker.Api/Program.cs`
  - `MovieTracker.Api/Telemetry/UsageLogger.cs`
  - `MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs`
  - `MovieTracker.Evals/Cases/RunTitleSelector.cs`
  - `MovieTracker.Evals/Tmdb/TmdbClient.cs`
  - `CLAUDE.md`
  
  Any file outside this set in `git status` output is a scope-guard failure. `MovieTracker.Evals/results/last-run.json` MUST be absent from the output (T019 compares the eval run against the Nov 18 baseline in-place; the harness's write reverts to unchanged status only if no rating-grading behavior actually changed — if it appears as modified in `git status` after T019, the owner reviews whether the new result reflects the intended rating fix and decides whether to keep it). `.gitignore`, `infrastructure/`, every `specs/001-*` through `specs/012-*` directory, and every other project file MUST be absent from the `git status` output.

- [X] T021 Implement-stage Codex review loop per `prompt.md` §Steps step 2. Depends on T016, T017, T018, T019, T020. Each round (N = 1..5):
  1. Write the review prompt to `C:\projects\temp\013-implement-round{N}-prompt.md`. The prompt briefs Codex on the five scope items, the guardrails ("stay focused on items 1 to 5, cut inflated or overkill work, and keep it simple without losing any listed change"), and asks for APPROVED / NOT APPROVED against the code on disk.
  2. Invoke Codex with the file piped to stdin, keeping stdin closed after the file ends. Use the form appropriate for the shell driving the automation:
     - Bash tool (used by this automation): `codex exec --skip-git-repo-check --sandbox read-only - < C:/projects/temp/013-implement-round{N}-prompt.md`.
     - PowerShell equivalents when an owner runs the loop manually (because PowerShell reserves `<`): either `cmd /c "codex exec --skip-git-repo-check --sandbox read-only - < C:\projects\temp\013-implement-round{N}-prompt.md"` or `Get-Content C:\projects\temp\013-implement-round{N}-prompt.md -Raw | codex exec --skip-git-repo-check --sandbox read-only -`.
  3. If APPROVED, record the round number and exit the loop (proceed to T022).
  4. If NOT APPROVED **and N < 5**: fix the findings in the implementation files (staying inside the allowed-paths set from T020). For every file edited during the fix, append a new entry to `C:\projects\temp\013-write-log.txt` (format `T021-round{N} <absolute-path>`). Then re-run T017 (build), T018 (smoke), T019 (eval with baseline restore), and T020 (scope-guard) to refresh their evidence against the current code. Only after those four re-runs are green does round N+1 begin.
  5. If NOT APPROVED **and N = 5**: do NOT apply fixes. Record the open findings as-is, leave the current code on disk, and proceed to T022 (which will report the open findings) and T023 (cleanup). The latest evidence from T017-T020 is whatever was captured before round 5 started — if it has drifted from the current code because of prior-round fixes that were not re-verified, note that in the open findings.
  
  Acceptance: either Codex `APPROVED` with the round number and the latest evidence from T017/T018/T019/T020 reflecting the approved code, OR (after round 5 NOT APPROVED) a captured list of open findings with no further code edits.
- [X] T022 Compile the Step-5 owner report per `prompt.md` §Steps step 5. Depends on T021 (either approved or round-5 stopped). Use the latest evidence captured in T017/T018/T019/T020 (refreshed after the last implement-stage fix). The report MUST include: (a) the file split that landed (enumerate every file created or edited — expected list: six new files `MovieTracker.Api/Chat/IChatPipeline.cs`, `ChatPipeline.cs`, `ChatContextReducer.cs`, `MovieHydrator.cs`, `ChatPipelineRegistration.cs`, `MovieTracker.Api/Telemetry/UsageLogger.cs`; edited `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`, `MovieTracker.Api/Program.cs`, `MovieTracker.Evals/Tmdb/TmdbClient.cs`, `MovieTracker.Evals/Cases/RunTitleSelector.cs`, `MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs`, `CLAUDE.md`; plus files under `specs/013-chat-pipeline/`), (b) the full console table from T019, (c) the totals line from T019, (d) the per-check comparison from T019, (e) Codex rounds per stage (specify, plan, tasks, implement) with the approval round number for each (specify APPROVED round 5; plan APPROVED after a file-based rerun — count the rounds from the latest rerun; tasks APPROVED — count from this stage; implement APPROVED per T021, or "stopped at round 5 with open findings" if T021 exited early), (f) anything left open (unresolved findings, surprises, deviations, including the LOG-1 ILogger<T> deviation deferred to a later slice, the six log-category changes caused by the file split — owner-accepted 2026-11-24, message text unchanged — and any T021 open findings). Deliver the report inline to the owner; do not create a new file.

- [X] T023 Temp-file cleanup per `prompt.md` §Rules ("Temp files only in `C:\projects\temp`. Delete them when done."). Depends on T022. Delete every `C:\projects\temp\013-*-prompt.md` and `C:\projects\temp\013-*-rerun.md` / `-converge.md` / `-final.md` / `013-implement-round*-prompt.md` created during specify / plan / tasks / implement review stages. Acceptance: `C:\projects\temp\` contains no file whose name starts with `013-`. If T021 exited early on round-5 NOT APPROVED, cleanup still runs so the owner gets a clean temp directory before the handoff; the owner is informed in T022's report that the review stopped early.

**Final checkpoint**: all 23 tasks complete. The slice is ready for the owner to review the Step-5 report from T022 and ship.

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 Setup (T001): no dependencies.
- Phase 2 Foundational (T002-T008): depends on T001.
- Phase 3 US1 (T009): depends on T008.
- Phase 4 US2 (T010): depends on T009.
- Phase 5 US3 (T011, T012): depends on T008.
- Phase 6 US4 (T013-T015): depends on T001; independent of Phases 3-5.
- Phase 7 US5 (T016): depends on T001; independent of Phases 3-6.
- Phase 8 Polish (T017-T023): depends on every earlier task.
  - T017 (build) first.
  - T018 (smoke), T019 (eval), T020 (scope guard) in any order after T017.
  - T021 (implement-stage Codex review loop) depends on T016-T020. Each NOT-APPROVED round of T021 re-runs T017, T018, T019, T020 after the fix to refresh evidence before the next review round.
  - T022 (Step-5 report) depends on T021 (approved or round-5 stopped). Reads the latest T017-T020 evidence.
  - T023 (temp-file cleanup) depends on T022.

### Within each phase

- Phase 2 scaffolding T002-T005 run in parallel (four brand-new files, no shared edits). T006 depends on T002+T003+T004+T005. T007 depends on T006. T008 depends on T007.
- Phase 6 US4 tasks run sequentially: T013 → T014 → T015 (each builds on the previous public surface).

### Parallel opportunities

- T002, T003, T004, T005 all [P] within Phase 2 (different new files; no shared edits).
- Phase 6 (US4) and Phase 7 (US5) and Phases 3-5 are independent code trees once Phase 2 is done — all can be done concurrently if desired, but a solo developer typically runs Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7.
- Phase 8 verification tasks T018 and T019 and T020 are independent of each other (different commands, different scopes) — once T017 (build) is green, they can all be done in any order.

---

## Parallel Example: Phase 2 scaffolding

```powershell
# Launch four file-creation tasks together (independent new files, no shared edits):
#   T002 — Create Chat/IChatPipeline.cs
#   T003 — Create Chat/ChatContextReducer.cs
#   T004 — Create Chat/MovieHydrator.cs
#   T005 — Create Telemetry/UsageLogger.cs
# Then sequentially:
#   T006 — Create Chat/ChatPipeline.cs (depends on T002-T005)
#   T007 — Create Chat/ChatPipelineRegistration.cs (depends on T006)
#   T008 — Edit Program.cs (depends on T007)
```

---

## Implementation Strategy

### MVP (US1 only)

1. T001 (branch check).
2. T002-T008 (foundational scaffolding + DI wiring).
3. T009 (endpoint cutover) — US1 complete.
4. **STOP and VALIDATE**: local smoke test the three endpoints manually.

At this point the slice is a working refactor with no behavior change; US2 verifies it rigorously and US3 is bookkeeping.

### Incremental delivery

1. Foundational complete → DI resolves.
2. US1 (T009) → endpoints HTTP-only, two ask paths share one code path, main's behavior preserved.
3. US2 (T010) → verify byte-identical error contracts.
4. US3 (T011-T012) → verify DI matches the plan.
5. US4 (T013-T015) → eval rating fix.
6. US5 (T016) → one-line docs edit.
7. Polish (T017-T020) → build, smoke, eval with restore, scope-guard.

### Parallel team strategy

With two developers:

- Developer A: T001 → T002-T008 → T009 (US1) → T010 (US2) → T011-T012 (US3).
- Developer B (starts after T001): T013-T015 (US4) and T016 (US5).
- Both: T017-T020 (Polish) once both tracks are green.

---

## Notes

- `[P]` tasks touch different files and have no dependencies on incomplete tasks — safe to run concurrently.
- Chat-pipeline cutover (T009) requires scaffolding (T002-T008) to be fully in place; attempting the cutover earlier breaks the endpoints.
- Eval rating fix (T013-T015) is independent of the chat refactor and can run in parallel with it.
- `MovieTracker.Evals/results/last-run.json` is NOT edited by this slice; T019 uses `Copy-Item` + SHA-256 to leave it unchanged (the eval harness overwrites it on each run).
- `EvalRunner.cs` is out of scope — T015 picks a threading mechanism that stays inside `Cases/` + `Tmdb/`.
- The owner commits and ships; no task commits, pushes, or opens a PR.
