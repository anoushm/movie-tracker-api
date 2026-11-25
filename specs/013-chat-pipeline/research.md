# Phase 0 Research: Chat Pipeline

**Branch**: `013-chat-pipeline` | **Date**: 2026-11-24

Resolves the two owner-approvable items in `spec.md` (file split, rating fix) and documents the shape decisions behind `IChatPipeline`.

## Decision 1 — File split for the pipeline

**Decision**: Split the current `ChatTurnEndpoints.cs` into five new files under `MovieTracker.Api/Chat/` plus one new file under `MovieTracker.Api/Telemetry/`:

- `Chat/IChatPipeline.cs` — interface
- `Chat/ChatPipeline.cs` — orchestrator
- `Chat/ChatContextReducer.cs` — reducer + tool-call summaries
- `Chat/MovieHydrator.cs` — assistant JSON → `MovieViewModel`
- `Chat/ChatPipelineRegistration.cs` — DI extension
- `Telemetry/UsageLogger.cs` — `UsageLogger` instance class carrying `LogUsage` as an instance method (DI-registered; playbook NET-36 bars a static method that takes injected services per call)

**Rationale**:

- The reducer code (today's `ContextReducer` + `SummariseToolCalls` + `SummariseArguments` + `SummariseResult` + `NameOf`, plus the `#pragma warning disable MAAI001`) has zero coupling to the hydration code or the agent. Isolating it keeps the `MAAI001` suppression scoped and makes it easy to review by itself. All five become instance members of `ChatContextReducer` per playbook NET-8 (private statics in non-static classes are prohibited for new/changed code).
- Hydration (`ToClientMessageAsync` + `ProcessMovieAsync` + `SafeProcessMovieAsync` + `LoadHydrationCache`) is used by both endpoint paths — the `/ask` path hydrates the full history once per request, the `/ask/v2` path hydrates the single new assistant message once per request — with the same cache semantics. One class owns it.
- The orchestrator (`ChatPipeline`) wires session load, the funny fact (parallel start + failure handling), the reducer call, `agent.RunAsync`, telemetry, the hydration branch, and the session save. That is the "one place to add agents" future slice.
- `UsageLogger` sits next to `Telemetry.cs` so the token / cost / Activity-tag code lives in one folder. The owner prompt explicitly names `MovieTracker.Api/Telemetry/` as the right home. `UsageLogger` is an instance class because playbook NET-36 bars a static method that takes injected services (logger, `ModelPricing`) as parameters per call.
- One file per type matches the project's code-style rule.

**Alternatives considered**:

- **Keep everything in `ChatPipeline.cs`**: moves all the logic but keeps a 500-line file. Rejected — the reducer pragma and the hydration cache code each deserve a separate file.
- **Put reducer + hydration as nested private classes**: hides them from reuse but balloons `ChatPipeline.cs` and makes DI verbose. Rejected.
- **Put `UsageLogging` inside `ChatPipeline.cs`**: the owner prompt specifically names `MovieTracker.Api/Telemetry/` as the home for usage logging. Rejected.

**Owner decision**: approved on 2026-11-24.

## Decision 2 — `IChatPipeline` boundary constraints

**Decision**: The pipeline interface guarantees a set of observable constraints; exact method count, method names, and outcome type hierarchies are left to `/speckit-tasks` / `/speckit-implement`.

Observable constraints the interface must guarantee:

- Endpoints call into the interface and nothing else; session start, session load, turn body, hydration, and save are all behind it.
- Chat-not-found is a distinct non-exceptional outcome the endpoint can switch on to map to `404 {"error":"Chat not found"}`.
- Any other failure bubbles out of the interface and the endpoint's existing outer `try/catch` maps to `503` with the same log line as today.
- The two ask paths share the same turn body code path inside the pipeline; only the hydration branch differs (full history for `/ask`, new assistant message only for `/ask/v2`).

**Rationale**: fixing signatures here blocks `/speckit-tasks` from picking the cleanest C# shape (one method with an enum/discriminator, two methods with a shared private helper, or a sealed base-record outcome). The observable mapping to HTTP is what matters for byte-identical behavior; the method surface is an implementation choice.

**Alternatives considered (not binding on tasks)**:

- **Single shared ask method with a mode parameter**: one public entry; the pipeline switches on mode for hydration. Trivial HTTP mapping on the endpoint side.
- **Two public ask methods delegating to one private helper**: two entries; the shared helper owns the turn body; outcome types can be distinct. Also trivial HTTP mapping.
- **Throwing `ChatNotFoundException`**: rejected — the endpoint would have to catch it on the hot path, and the refactor is cleaning up that pattern.

## Decision 3 — Pipeline hydrates per endpoint (not one combined payload)

**Decision**: The pipeline owns hydration, but hydrates only what the calling endpoint needs — full history for `/ask`, new turn only for `/ask/v2`. The outcome types reflect that difference.

**Rationale**:

- On `main`, `/ask/v2` hydrates exactly one assistant message. If the pipeline hydrated the full history for both endpoints, `/ask/v2` would get extra TMDb calls, extra distributed-cache writes, extra potential failure points, and a changed cache footprint — a behavior change.
- The shared code path (session load, funny fact, reducer, `agent.RunAsync`, telemetry, usage logging, session save) is where the duplication was. That stays shared. Hydration is the one place that branches.
- The eval harness and any client sampling the responses would see different cache-hit patterns if hydration were merged. The spec requires "same answers".

**Alternatives considered**:

- **Hydrate everything once, let endpoint pick**: changes `/ask/v2`'s side effects. Rejected (Codex round 2 finding 3).
- **Lazily hydrate**: the pipeline would need to return a hydrator closure the endpoint runs. Adds a layer for no gain. Rejected.

## Decision 4 — DI lifetimes

**Decision**: `IChatPipeline` and `MovieHydrator` scoped; `ChatContextReducer` and `UsageLogger` singleton.

**Rationale**: see the "DI Lifetimes" section in `plan.md`. In short: `IChatPipeline` captures scoped `AIAgent` and scoped `ChatPlanner`, so it must be scoped or transient — scoped matches the per-request graph. `MovieHydrator` is scoped for graph consistency (its deps are all singleton today, but keeping it scoped avoids the next slice silently introducing a scoped dep and creating a singleton-of-scoped capture). `ChatContextReducer` and `UsageLogger` are stateless (or hold only singleton-injected dependencies) and reused across requests.

**Alternatives considered**:

- **Everything scoped**: `ChatContextReducer` and `UsageLogger` are reused on every turn; singleton saves allocation. Rejected for both.
- **Everything singleton**: would capture scoped `AIAgent` and `ChatPlanner`. Rejected (classic DI pitfall).
- **`UsageLogger` as a static class with dependencies passed as parameters**: rejected — violates playbook NET-36.

## Decision 5 — Rating SKIPPED fix source

**Decision**: Rating case titles come from TMDb `movie/top_rated?page=1`, selected by the same rule `RunTitleSelector` uses today for popular titles.

**Rationale**: see the "Rating SKIPPED root cause" section in `plan.md`. The Nov 18 historical cause is **not recoverable** from the saved results — only the reason string `"no truth value"` was persisted, not which branch triggered or what OMDb returned. Code inspection of the three `NoTruth` branches plus the observed shape of TMDb popular page 1 (new releases with a known OMDb pattern of returning `"N/A"` for very new films) makes Branch C (empty OMDb rating string) the **most likely hypothesis**, not a confirmed cause. `top_rated` is TMDb's long-standing ranking, dominated by canonical titles that OMDb typically carries IMDb ratings for; switching the source addresses the Branch C hypothesis on the common path. Branches A (missing `ImdbId`) and B (missing release year) remain possible edge cases and keep their existing `NoTruth` behavior via FR-014.

**Alternatives considered**:

- **Filter popular page 1 to rated titles**: adds a second OMDb pass, duplicates selection logic, can empty candidate list. Rejected.
- **Pre-check `details.ImdbId` + `year` only**: doesn't address the Branch C hypothesis, which is the most likely cause. Rejected.
- **Hard-code two canonical rating titles**: removes randomness (a stated rule of `RunTitleSelector`). Rejected.

**Owner decision**: approved on 2026-11-24.

## Decision 6 — Which component picks rating titles

**Decision**: The plan does NOT fix which component performs the selection. Implementation stays inside the approved eval scope (`MovieTracker.Evals/Cases/` and `MovieTracker.Evals/Tmdb/`); no new abstraction, no file added outside that scope.

**Rationale**: FR-013 explicitly leaves implementation location to tasks. Codex round 1 finding 3 called out that prescribing `BuildRatingAsync` to do its own selection over-constrains the spec. The plan preserves that flexibility. The Scope Guard section of `plan.md` fixes the on-disk scope; everything else is a task-level decision.

## Non-decisions (noted, left for tasks)

- Whether the `Ask` record (today in `Models/`) is renamed or kept. **Keep** per FR-003 (routes and DTOs unchanged).
- Whether the ask paths share a private helper or use a different internal factoring. Implementation detail for `/speckit-implement`.
- Whether `ChatContextReducer` exposes the `IChatReducer` instance or an `AsChatReducer()` method. Implementation detail.

## Evidence base

- `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` (current `main`/branch-base state).
- `MovieTracker.Api/Program.cs`, `MovieTracker.Api/ChatSessions/ChatSessionRegistration.cs`, `MovieTracker.Api/Tools/MovieToolsRegistration.cs`, `MovieTracker.Api/Agents/MovieAgent.cs`, `MovieTracker.Api/Telemetry/TelemetryRegistration.cs` for DI lifetimes.
- `MovieTracker.Evals/Cases/GeneratedCaseBuilder.cs:210-232`, `MovieTracker.Evals/Tmdb/OmdbClient.cs:27-79`, `MovieTracker.Evals/Cases/RunTitleSelector.cs`, `MovieTracker.Evals/Tmdb/TmdbClient.cs` for the rating fix.
- `MovieTracker.Evals/results/last-run.json` for Nov 18 baseline.
