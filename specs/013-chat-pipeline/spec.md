# Feature Specification: Chat Pipeline

**Feature Branch**: `013-chat-pipeline`

**Created**: 2026-11-24

**Status**: Draft

**Input**: Owner prompt at `specs/013-chat-pipeline/prompt.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One code path builds both ask responses (Priority: P1)

A maintainer opens `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` and sees only route mapping, request validation, span starts, and the mapping of a pipeline outcome back to an HTTP result. The answer-building work — starting a session with `ChatStartSystemPrompt`, loading the session, running the turn (`MovieListResponseFormat`, context reducer, `agent.RunAsync`, tool-call and context telemetry, usage logging and cost), the funny fact, movie hydration, and the session save with `HydratedMovieLimit` trim — lives in a new `IChatPipeline` / `ChatPipeline` pair under `MovieTracker.Api/Chat/`. Both endpoints use the same shared code path for the turn body (session load, context reducer, `agent.RunAsync`, telemetry, funny fact, save). The pipeline owns hydration too, but hydrates only the payload the calling endpoint needs — full chat history for `/ask`, the new assistant turn for `/ask/v2` — preserving each endpoint's existing side effects (cache writes, failure points) exactly as on `main`. Each endpoint handler reads the pipeline outcome and maps it to its HTTP response; no hydration, no assistant-JSON parsing, no fallback-message logic runs in the handler.

**Why this priority**: this is the whole point of the slice. Option B (resolver, main agent, critic) in a later spec needs one place to add agents, and today `AskAsync` and `AskV2Async` duplicate the full turn body.

**Independent Test**: on an `az login` session, run `dotnet run --project MovieTracker.Api` and call `GET /api/chat/start`, then `POST /api/chat/{chatId}/ask` and `POST /api/chat/{chatId}/ask/v2` with a valid `Ask` body. Both endpoints return the same status codes and response shapes as `main` for valid requests; the only chat-answer logic in `ChatTurnEndpoints.cs` is the three endpoint lambdas (routes, spans, request validation, outcome-to-HTTP mapping), and both lambdas delegate their turn work to one `IChatPipeline` method.

**Acceptance Scenarios**:

1. **Given** a client posts a valid `Ask` to `/api/chat/{chatId}/ask`, **When** the endpoint runs, **Then** the response is `200 OK` with a `ChatMessageResponse { FunnyFact, Messages }` whose messages match the full hydrated history that `main` returns for the same input, the hydrated history is produced by the pipeline (not the endpoint), and the handler's only work is span start, request validation, pipeline call, and DTO assembly from pipeline fields.
2. **Given** a client posts a valid `Ask` to `/api/chat/{chatId}/ask/v2`, **When** the endpoint runs, **Then** the response is `200 OK` with a `ChatTurnResponse { FunnyFact, Turn }` whose turn matches the newly hydrated assistant turn that `main` returns for the same input, the hydrated turn (including the `No movies were found` fallback) is produced by the pipeline (not the endpoint), only the new turn is hydrated (not the full history, matching `main`'s cache writes and failure points), and the handler's only work is span start, request validation, pipeline call, and DTO assembly from pipeline fields.
3. **Given** `ChatTurnEndpoints.cs` is inspected after the slice, **When** it is read, **Then** it contains no agent call, no context reducer, no response-format definition, no funny-fact invocation, no movie hydration, no assistant-JSON parsing, and no session save; those live in `ChatPipeline` and its collaborators.
4. **Given** both ask endpoints exist, **When** the pipeline is called, **Then** both `/ask` and `/ask/v2` reach the same shared code path for the turn body (session load, context reducer, `agent.RunAsync`, telemetry, funny fact, save), and only the hydration branch differs: `/ask` hydrates the full chat history and `/ask/v2` hydrates the new assistant turn only — matching `main`'s per-endpoint side effects (cache writes, failure points).
5. **Given** the turn constants are inspected, **When** the pipeline runs a turn, **Then** `MaxModelTurns`, `CollapsedToolResultChars`, `CollapsedArgumentChars`, `CollapsedResultNames`, and `HydratedMovieLimit` keep their current values.

---

### User Story 2 - Endpoint error responses byte-for-byte identical (Priority: P1)

A client that already works against `main` keeps working. The three routes keep their names and paths (`ChatStart` -> `GET /api/chat/start`, `ChatAsk` -> `POST /api/chat/{chatId}/ask`, `ChatAskV2` -> `POST /api/chat/{chatId}/ask/v2`). The spans keep their names (`movie-tracker-api.chat-start`, `movie-tracker-api.chat-ask`, `movie-tracker-api.chat-ask-v2`). Every error body and log message stays identical to `main`: the 400 body `{"error":"Invalid request, missing ask object"}` for missing/invalid JSON, the 404 body `{"error":"Chat not found"}` when the Cosmos read returns 404, and the 503 bodies `{"error":"Session creation failed."}` and `{"error":"Chat request failed."}` for start and ask failures respectively. The log messages (`Chat-Start session creation failed.`, `Chat-Ask failed for session {ChatId}`, `Chat-Ask-V2 failed for session {ChatId}`) are preserved verbatim.

**Why this priority**: the slice is a refactor, not a behavior change. Any drift in error contract breaks existing clients and the eval harness.

**Independent Test**: on an `az login` session, run `dotnet run --project MovieTracker.Api`. (a) `POST /api/chat/{chatId}/ask` with no body returns `400` and body `{"error":"Invalid request, missing ask object"}`. (b) `POST /api/chat/{unknown}/ask` with a valid body returns `404` and body `{"error":"Chat not found"}`. (c) Repeat for `/ask/v2`. All status codes and response bodies match `main` exactly.

**Acceptance Scenarios**:

1. **Given** a `POST /api/chat/{chatId}/ask` or `/ask/v2` with no JSON content type or an unreadable body, **When** the endpoint runs, **Then** the response is `400 BadRequest` with body `{"error":"Invalid request, missing ask object"}`.
2. **Given** a `POST /api/chat/{chatId}/ask` or `/ask/v2` whose `chatId` does not exist, **When** the Cosmos read throws `CosmosException` with status `NotFound`, **Then** the response is `404 NotFound` with body `{"error":"Chat not found"}`.
3. **Given** a `GET /api/chat/start` whose repository call throws, **When** the endpoint handles the failure, **Then** the response is `503` with body `{"error":"Session creation failed."}` and the log message `Chat-Start session creation failed.` is emitted at `Error` level.
4. **Given** a `POST /api/chat/{chatId}/ask` whose pipeline call throws, **When** the endpoint handles the failure, **Then** the response is `503` with body `{"error":"Chat request failed."}` and the log message `Chat-Ask failed for session {ChatId}` is emitted at `Error` level.
5. **Given** a `POST /api/chat/{chatId}/ask/v2` whose pipeline call throws, **When** the endpoint handles the failure, **Then** the response is `503` with body `{"error":"Chat request failed."}` and the log message `Chat-Ask-V2 failed for session {ChatId}` is emitted at `Error` level.
6. **Given** the three routes are mapped, **When** the OpenAPI routes list is inspected, **Then** the route names are `ChatStart`, `ChatAsk`, `ChatAskV2` and the paths are `GET /api/chat/start`, `POST /api/chat/{chatId}/ask`, `POST /api/chat/{chatId}/ask/v2`.
7. **Given** the three endpoints run, **When** OpenTelemetry spans are observed, **Then** the span names are `movie-tracker-api.chat-start`, `movie-tracker-api.chat-ask`, `movie-tracker-api.chat-ask-v2`.

---

### User Story 3 - Single `ChatPipelineRegistration` wires the pipeline into DI (Priority: P2)

A reader of `Program.cs` sees one `builder.Services.AddChatPipeline(...)` (or equivalent `Add...` extension method) that registers `IChatPipeline` and any split-out collaborators, alongside the existing `AddChatSession(...)`, `AddMovieTools(...)`, `AddTelemetry(...)`. The extension lives in a new `MovieTracker.Api/Chat/ChatPipelineRegistration.cs`. The lifetime chosen for `IChatPipeline` matches the lifetimes of its dependencies (`ChatSessionRepository`, `AIAgent`, `ChatPlanner`, `IDistributedCache`, `TMDbClient`, `ModelPricing`, `IConfiguration`, and the loggers already in use) and is documented in `plan.md`.

**Why this priority**: the pipeline is useless without DI, but the DI choice is mechanical once the pipeline exists; it rides alongside User Stories 1-2.

**Independent Test**: inspect `Program.cs` and `Chat/ChatPipelineRegistration.cs`. `Program.cs` calls the new `Add...` method and does not construct the pipeline or its collaborators by hand. The DI container resolves `IChatPipeline` at startup without additional manual wiring, and `dotnet build movie-tracker-api.slnx` succeeds.

**Acceptance Scenarios**:

1. **Given** the slice lands, **When** `Program.cs` is read, **Then** it calls a single `Add...` extension method defined in `Chat/ChatPipelineRegistration.cs` that registers `IChatPipeline` and any split-out collaborators (such as `ChatContextReducer` or `MovieHydrator` if the split is used).
2. **Given** `ChatPipelineRegistration.cs` is read, **When** its method is inspected, **Then** the service lifetime for `IChatPipeline` and each split-out collaborator is explicit and documented in `plan.md` with a one-line rationale per registration (matching the lifetime of its dependencies).
3. **Given** the application starts, **When** the host resolves the three chat endpoints, **Then** each endpoint receives `IChatPipeline` through DI without any manual `new ChatPipeline(...)` elsewhere in the project.
4. **Given** the new registration file is read, **When** its contents are inspected, **Then** no other registration extension method (`AddChatSession`, `AddMovieTools`, `AddTelemetry`) is modified beyond what the new `AddChatPipeline` extension needs to coexist.

---

### User Story 4 - Eval rating cases are graded, not skipped (Priority: P1)

A maintainer runs `dotnet run --project MovieTracker.Evals`. In the console table, both rating cases (`rating-1` and `rating-2`) show `PASS` or `FAIL` — never `SKIP` on account of a missing truth value from OMDb in the common case. The investigation and fix happen in two steps:

1. Investigate the SKIPPED cause by reading `Cases/GeneratedCaseBuilder.BuildRatingAsync` and `Tmdb/OmdbClient` on `main`, identifying which `NoTruth` branch (missing `ImdbId`, missing release year, or empty rating string from OMDb) most likely triggered on the Nov 18 run. If OMDb was called and the return value is recoverable, record it; otherwise record that the Nov 18 OMDb return is not recoverable or that the historical branch cannot be confirmed from available evidence, and state what evidence (code inspection, repro on today's data) was used to narrow the cause. Record this under a "Rating SKIPPED root cause" heading in `plan.md` before any fix is proposed.
2. Then propose an evidence-informed fix to the owner and apply only the owner-approved fix. The recommended fix (pending owner approval): rating cases take their titles from TMDb `movie/top_rated` page 1, picked by the same selection rule `Cases/RunTitleSelector` uses today for popular titles; every other check (listIncludes, director, actorFilmography, identifyByPlot, genreDecade, trailer, followUpDirector, funnyFactNotEmpty) continues to use the popular run titles. The rating check's grading logic and its question template are unchanged regardless of which fix the owner approves.

**Why this priority**: in the Nov 18 baseline both rating cases came back `SKIPPED` with reason `"no truth value"`. The saved result does not retain which `NoTruth` branch fired or what OMDb returned, so the historical cause is not recoverable from the baseline; the hypothesis is Branch C (OMDb returning `"N/A"` for recent popular-page-1 titles) based on code inspection of `BuildRatingAsync` + `OmdbClient` and the observed character of popular page 1. Either way the rating check measures nothing in the common case today, so fixing the data source is prerequisite to interpreting the rating signal at all, in this slice or the next.

**Independent Test**: on an `az login` session with a working OMDb key, run `dotnet run --project MovieTracker.Evals`. The table shows `rating-1` and `rating-2` rows with `PASS` or `FAIL` in the `result` column (not `SKIP` in the common case). The console preamble still prints one line of the form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` for the run titles used by every other check. If the owner approved the recommended fix, the rating cases' question text (via the shared template) uses the top-rated titles for their two slots; if the owner approved a different fix, the rating cases' question text matches the owner-approved proposal.

**Acceptance Scenarios**:

1. **Given** the owner-approved fix is in place and the two titles it selects each carry a TMDb `ImdbId`, a parseable release year, and an IMDb rating in OMDb, **When** the harness runs, **Then** `rating-1` and `rating-2` each resolve to a numeric expected rating and are graded `PASS` or `FAIL` (never `SKIP`).
2. **Given** the owner has approved the recommended fix (rating titles from TMDb `movie/top_rated?page=1`), **When** a rating case picks its title, **Then** the title comes from that source selected by the same rule the popular-title selector uses today, and every other check keeps using the popular run titles. (If the owner approves a different fix instead, the implemented fix matches the owner-approved proposal as recorded in `plan.md`.)
3. **Given** the rating check is run, **When** the grading logic is inspected, **Then** `RatingCheck`, `RatingExpected`, and the rating question template are byte-for-byte the same as on `main` (no change to the question template, the parse logic, or the comparison logic).
4. **Given** a selected rating-case title is missing its TMDb `ImdbId`, is missing a parseable release year, or has no IMDb rating in OMDb (any of the three `NoTruth` branches in `BuildRatingAsync` today), **When** the rating case is graded, **Then** that case is `SKIP` just like other no-truth-value cases today; FR-014 keeps that existing no-truth behavior for all three missing-data conditions.
5. **Given** the slice is complete, **When** files outside `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/` are inspected, **Then** no other eval check's title source has been changed (every non-rating check still reads popular run titles).
6. **Given** `/speckit-plan` is produced, **When** `plan.md` is read, **Then** it contains a "Rating SKIPPED root cause" section that either names the branch in `BuildRatingAsync` that returned `NoTruth` on Nov 18 (and the OMDb return if OMDb was called and the return is recoverable) or records that the historical cause is not recoverable from available evidence, and states what evidence (code inspection, repro on today's data) was used to narrow the cause; only then does the plan propose an evidence-informed fix and seek owner approval.

---

### User Story 5 - Docs reflect the new `Chat/` layout (Priority: P3)

A reader of `CLAUDE.md` sees the `Chat/` folder line updated to name the new pipeline file(s) alongside the existing `ChatTurnEndpoints`. Only that one line changes; no other section of `CLAUDE.md` is edited in this slice.

**Why this priority**: trailing cleanup; not user-visible, no runtime effect. Parked at P3 so it does not compete with the refactor itself.

**Independent Test**: diff `CLAUDE.md` against `main`. The only change is the `Chat/` bullet under `Project Structure`.

**Acceptance Scenarios**:

1. **Given** the slice lands, **When** `CLAUDE.md` is diffed against `main`, **Then** the only changed line is the `Chat/` entry under `Project Structure`, and it names `ChatPipeline` (and any split-out collaborators actually introduced) alongside the existing `ChatTurnEndpoints` entry.
2. **Given** `CLAUDE.md` is inspected, **When** the Project Structure bullets are read, **Then** all other folder lines (`Agents/`, `Tools/`, `Models/`, `ChatSessions/`, `Telemetry/`, `Dockerfile`, `infrastructure/`) are unchanged from `main`.

---

### Edge Cases

- `/ask` or `/ask/v2` request has no JSON content type or an unreadable body: the endpoint returns `400` with body `{"error":"Invalid request, missing ask object"}` without entering the pipeline.
- The pipeline is called with a `chatId` that does not exist in Cosmos: the pipeline surfaces the not-found outcome as a distinct result, and the endpoint returns `404` with body `{"error":"Chat not found"}`.
- The agent call throws or the hydration throws partway through: the endpoint returns `503` with body `{"error":"Chat request failed."}` and the matching `Chat-Ask failed...` or `Chat-Ask-V2 failed...` log line is emitted. The response contract matches `main`.
- The funny fact call throws: the turn succeeds, the chat session's `FunnyFact` is left at its previous value, and the warning `Funny fact generation failed; answering without one` is logged (same as today).
- The hydration cache grows past `HydratedMovieLimit`: the session save trims to `HydratedMovieLimit` and emits the same info log line as today.
- The pipeline is called under `/ask/v2` and the turn produces no assistant text: the endpoint returns a `ChatTurnResponse` whose `Turn` is `new AssistantChatMessage("No movies were found", new List<MovieViewModel>())` — unchanged from `main`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new interface `IChatPipeline` and a class `ChatPipeline` MUST be added under `MovieTracker.Api/Chat/`. `ChatPipeline` MUST own session start (using `ChatStartSystemPrompt`), session load (with a distinct chat-not-found outcome so the endpoint can return 404), turn execution (`MovieListResponseFormat`, the context reducer, `agent.RunAsync`, tool-call and context telemetry, usage logging and cost), the funny fact (same parallel start and same failure handling as today), movie hydration (`ToClientMessageAsync` and `ProcessMovieAsync` paths, session + distributed caches), and session save with the `HydratedMovieLimit` trim.
- **FR-002**: `ChatTurnEndpoints.cs` MUST contain only `MapChatEndpoints`, the three endpoint lambdas, request validation, span starts, pipeline calls, and HTTP result mapping. No agent call, no response-format definition, no context reducer or tool-call summary code, no funny-fact invocation, no movie hydration, no session save MUST remain in this file.
- **FR-003**: The three route names MUST stay `ChatStart`, `ChatAsk`, `ChatAskV2` and the paths MUST stay `GET /api/chat/start`, `POST /api/chat/{chatId}/ask`, `POST /api/chat/{chatId}/ask/v2`.
- **FR-004**: The three span names MUST stay `movie-tracker-api.chat-start`, `movie-tracker-api.chat-ask`, `movie-tracker-api.chat-ask-v2`.
- **FR-005**: Both `/ask` and `/ask/v2` MUST drive one shared code path in `ChatPipeline` for the turn body: session load, context reducer, `agent.RunAsync`, tool-call and context telemetry, usage logging and cost, funny fact (same parallel start and failure handling as today), session save with `HydratedMovieLimit` trim. Hydration is owned by the pipeline but hydrates only the payload the calling endpoint needs — full chat history for `/ask`, the new assistant turn for `/ask/v2` — preserving each endpoint's existing cache writes and failure points exactly as on `main`. The endpoint handlers MUST NOT hydrate, parse assistant JSON, or decide the fallback assistant message. Each handler picks the hydrated field from the pipeline outcome and builds its DTO: `/ask` returns `ChatMessageResponse { FunnyFact, Messages }`; `/ask/v2` returns `ChatTurnResponse { FunnyFact, Turn }`.
- **FR-006**: Error bodies MUST be byte-for-byte identical to `main`: 400 `{"error":"Invalid request, missing ask object"}`, 404 `{"error":"Chat not found"}`, 503 for start `{"error":"Session creation failed."}`, 503 for ask and ask/v2 `{"error":"Chat request failed."}`.
- **FR-007**: Error log messages MUST be byte-for-byte identical to `main`: `Chat-Start session creation failed.`, `Chat-Ask failed for session {ChatId}`, `Chat-Ask-V2 failed for session {ChatId}`, `Funny fact generation failed; answering without one`, and the hydration cache trim info message. Log levels MUST be unchanged from `main`.
- **FR-008**: The constants `MaxModelTurns = 12`, `CollapsedToolResultChars = 160`, `CollapsedArgumentChars = 60`, `CollapsedResultNames = 6`, and `HydratedMovieLimit = 150` MUST keep their current values. They MAY move to `ChatPipeline` or to its split-out collaborators but their numeric values and effective behavior MUST NOT change.
- **FR-009**: A new `MovieTracker.Api/Chat/ChatPipelineRegistration.cs` MUST expose exactly one `Add...` extension method on `IServiceCollection` (or `WebApplicationBuilder.Services`) that registers `IChatPipeline` and any split-out collaborators. `Program.cs` MUST call this method and MUST NOT otherwise reach into pipeline internals.
- **FR-010**: Service lifetimes chosen in `ChatPipelineRegistration.cs` MUST match the lifetimes of each registration's dependencies (`ChatSessionRepository`, `AIAgent`, `ChatPlanner`, `IDistributedCache`, `TMDbClient`, `ModelPricing`, `IConfiguration`, and loggers). Each registration's chosen lifetime MUST be documented in `plan.md` with a one-line rationale.
- **FR-011**: The owner-approved file split MUST be settled before implementation and MUST land as named. The proposed split (one `ChatPipeline` orchestration class, one `ChatContextReducer` for the reducer and tool-call summaries, one `MovieHydrator` for assistant-message-to-`MovieViewModel`, with usage logging placed next to the existing telemetry code under `MovieTracker.Api/Telemetry/`) is the recommended choice; a different split is allowed only if the owner approves it in writing before `/speckit-implement`.
- **FR-012**: The behavior of `ChatSessionRepository`, `AIAgent`, `ChatPlanner`, `IDistributedCache`, `TMDbClient`, `ModelPricing`, and the telemetry `Telemetry` static helpers MUST NOT change in this slice. Their public surface MAY be referenced by new pipeline code but MUST NOT be edited.
- **FR-013**: Before any fix is proposed, `plan.md` MUST record under a "Rating SKIPPED root cause" heading (a) the evidenced `NoTruth` branch in `Cases/GeneratedCaseBuilder.BuildRatingAsync` (missing `ImdbId`, missing release year, or empty rating string from `OmdbClient.GetImdbRatingAsync`) that triggered on the Nov 18 run and (b) what `Tmdb/OmdbClient.GetImdbRatingAsync` returned for those titles if it was called; the plan MAY instead record that the Nov 18 OMDb return is not recoverable, or that the historical branch cannot be confirmed from available evidence, and MUST then state what evidence was used (code inspection, repro run on today's data) to narrow the cause. The plan MUST then propose a fix (informed by the evidence it has) and obtain owner approval before implementation. The recommended fix is that the rating cases' title source for both slots is TMDb `movie/top_rated?page=1`, with selection using the same rule applied today for popular run titles (pick 2 distinct indices at random over the returned page, then use their titles in slot order). The spec does not prescribe which component does the selection. Whatever fix the owner approves, every other check MUST continue to use the popular run titles.
- **FR-014**: The rating check's grading logic (`Checks/RatingCheck`, `Checks/RatingExpected`) and the rating question template MUST NOT change.
- **FR-015**: `CLAUDE.md`'s `Chat/` line under `Project Structure` MUST be updated to name the new file(s) alongside `ChatTurnEndpoints`. No other `CLAUDE.md` content MUST be changed in this slice.
- **FR-016**: No code comments MUST be added. Changed code MUST follow the project style: explicit types (never `var`), braces on every `if`, private fields plain camelCase (no leading underscore), one type per file, file-scoped namespaces matching the folder.
- **FR-017**: `dotnet build movie-tracker-api.slnx` MUST succeed with no new warnings compared with `main`.
- **FR-018**: Running `dotnet run --project MovieTracker.Evals` after the slice MUST grade both rating cases (both rows `PASS` or `FAIL`, never `SKIP` on account of a missing OMDb rating in the common case). No other check MUST get worse because of this refactor. Titles are random and the model is not deterministic, so if a check drops the run MUST be repeated once; if it still drops on the rerun, the cause MUST be found in the diff before the drop is reported.
- **FR-019**: The only paths changed by this slice MUST be inside `MovieTracker.Api/Chat/`, `MovieTracker.Api/Program.cs`, any moved usage-logging file under `MovieTracker.Api/Telemetry/`, files under `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/` touched by FR-013 (and only those), the single `Chat/` line in `CLAUDE.md`, and files under `specs/013-chat-pipeline/`.

### Out of Scope (binding)

- No agent, prompt, tool, model, or response-shape change. No resolver, no critic, no Copilot SDK (that is the Option B spec).
- No new endpoints, no new config values, no feature switches.
- No merging of `/ask` and `/ask/v2` into one route; both stay.
- No change to other eval checks' logic, their question templates, or the reporting layer.
- No changes to CI, GitHub Actions, or deployment infrastructure.
- No edits to `.gitignore`, to specs `001` through `012`, or to docs outside the one-line `CLAUDE.md` edit in FR-015.
- No commit, push, or PR from the automation. The owner commits and ships.

### Key Entities

- **IChatPipeline**: the one interface the chat endpoints call for chat work. Exposes session start, session load with a distinct not-found outcome, and a single turn-run method whose outcome is shaped by both ask endpoints into their own response.
- **ChatPipeline**: the concrete orchestrator. Owns the turn body that was duplicated across `AskAsync` and `AskV2Async` and surfaces the outcome in a form that supports both response shapes.
- **ChatContextReducer** *(proposed split)*: the `PipelineCompactionStrategy` reducer and tool-call summary code (`SummariseToolCalls`, `SummariseArguments`, `SummariseResult`, `NameOf`). Lives next to `ChatPipeline`.
- **MovieHydrator** *(proposed split)*: `ToClientMessageAsync`, `ProcessMovieAsync`, and the session + distributed cache paths that turn an assistant JSON message into a `List<MovieViewModel>`.
- **ChatPipelineRegistration**: one DI extension method under `Chat/` that registers `IChatPipeline` and any split-out collaborators with explicit lifetimes.
- **Usage-logging helper** *(proposed location)*: the `LogUsage` body today in `ChatTurnEndpoints`, moved next to the existing `Telemetry` static class so it sits with its sibling cost/token helpers.
- **Rating title source (evals)**: the recommended source is TMDb `movie/top_rated?page=1`, picked the same way the popular run titles are picked today. Subject to owner approval of the proposed fix after the root-cause record. Under that fix, the rating cases consume these titles instead of the popular run titles; every other check continues to use the popular run titles. The spec does not prescribe which component performs the selection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the slice, `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` contains no identifier among `MovieListResponseFormat`, `ContextReducer`, `SummariseToolCalls`, `RunTurnAsync`, `ProcessMovieAsync`, `ToClientMessageAsync`, `LoadHydrationCache`, `SaveSessionAsync`, `LogUsage`, `SafeGenerateFunnyFactAsync`, `SafeProcessMovieAsync`, `NameOf`. These live in `ChatPipeline` or its split-out collaborators.
- **SC-002**: `grep -n "agent\\.RunAsync\\|ChatStartSystemPrompt\\|HydratedMovieLimit" MovieTracker.Api/Chat/ChatTurnEndpoints.cs` returns no matches.
- **SC-003**: The two ask endpoints share one code path in `ChatPipeline` for the turn body (session load, reducer, `agent.RunAsync`, telemetry, funny fact, save). The hydration step runs inside the pipeline but hydrates only what each endpoint needs — full history for `/ask`, new turn only for `/ask/v2` — matching `main`'s per-endpoint cache writes and failure points. The per-endpoint handlers only pick the hydrated field from the pipeline outcome and assemble their DTO.
- **SC-004**: For valid requests, `POST /api/chat/{chatId}/ask` and `POST /api/chat/{chatId}/ask/v2` return the same status codes and response shapes as `main` (`ChatMessageResponse` and `ChatTurnResponse` respectively, each with `FunnyFact` and the hydrated turn/messages).
- **SC-005**: Error responses are byte-identical to `main`: the three 400/404/503 bodies named in FR-006 and the span names in FR-004.
- **SC-006**: `Program.cs` calls exactly one new `Add...` extension method defined in `Chat/ChatPipelineRegistration.cs` and does not construct `ChatPipeline` or any new collaborator by hand.
- **SC-007**: An eval run started on an `az login` session completes end to end. In the console table, `rating-1` and `rating-2` show `PASS` or `FAIL` (not `SKIP` from a missing OMDb rating in the common case), and no non-rating check gets worse because of the refactor; if a check drops, one rerun is attempted, and if the drop persists the cause is found in the diff before the drop is reported.
- **SC-008**: `dotnet build movie-tracker-api.slnx` succeeds with zero new warnings compared with `main`.
- **SC-009**: The files changed by this slice live only inside `MovieTracker.Api/Chat/`, `MovieTracker.Api/Program.cs`, any one moved file under `MovieTracker.Api/Telemetry/` for usage logging, files under `MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/` touched by FR-013, the single `Chat/` line in `CLAUDE.md`, and files under `specs/013-chat-pipeline/`. `.gitignore`, `infrastructure/`, specs `001` through `012`, and every other project file are unchanged.

## Assumptions

- The agent, prompts, tool set, OpenAI deployment, Cosmos schema, and chat session schema are held constant for this slice. The slice is a refactor plus the one eval data-source fix.
- The `ChatMessageResponse` and `ChatTurnResponse` DTOs are unchanged from `main`; the pipeline outcome is shaped internally so each endpoint composes the right DTO without altering the DTO definitions.
- `MovieTracker.Api/ChatSessions/ChatSessionRepository` and `MovieTracker.Api/ChatSessions/MovieTrackerChatSession` keep their public surface; the pipeline consumes them as-is.
- The TMDb client already registered in DI can expose `movie/top_rated` through the same mechanism it uses for `movie/popular`. If the current `TmdbClient` has no `GetTopRatedAsync` method, adding exactly that one method (parallel to the existing `GetPopularAsync`) is in scope for FR-013 and no other `TmdbClient` surface changes are made. The spec does not fix which component selects the rating titles.
- The owner reviews and approves the proposed file split (recommended: `ChatPipeline`, `ChatContextReducer`, `MovieHydrator`, usage logging under `Telemetry/`) before `/speckit-implement` runs.
- The owner reviews the rating SKIPPED root-cause finding in `plan.md` and approves a fix (recommended: TMDb `movie/top_rated?page=1`, selected by the same rule as popular run titles) before `/speckit-implement` runs. The implementation matches the owner-approved fix, not necessarily the recommended one.
- Codex cross-review runs after each Spec Kit stage (`specify`, `plan`, `tasks`, `implement`) per the constitution's Development Workflow, up to 5 rounds per stage per the owner prompt (the project constitution allows 6; the owner prompt tightens that to 5 for this slice).
- The owner commits and ships. The automation does not commit, push, or open a PR in this slice.
