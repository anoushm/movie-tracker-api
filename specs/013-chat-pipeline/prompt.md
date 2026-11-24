# 013 Chat Pipeline: Spec Kit Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api`. Start on `main` (with `012-eval-cleanup` merged); step 0 creates the branch `013-chat-pipeline`.
Goal: move the answer-building logic out of `Chat/ChatTurnEndpoints.cs` into one `ChatPipeline` class, so the endpoints only handle HTTP. This is the groundwork for Option B (resolver, main agent, critic), which comes in a later spec.
No behavior change: same URLs, same request and response shapes, same status codes, same answers.

Why: today `ChatTurnEndpoints` mixes HTTP handling with the agent call, the JSON response format, the context reducer, the funny fact, movie hydration, usage logging and session save. `AskAsync` and `AskV2Async` also duplicate most of that code. Option B needs one place to add agents.

---

## Rules

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly. Use the Spec Kit flow (specify, plan, tasks, implement).
- Git only for step 0 (sync `main`, create the branch), run from Windows. No commit, no push, no PR. The owner commits.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces that match the folder.
- Do not add anything not listed in Scope. Name any extra idea in one line and ask.
- Leave specs `001` to `012` unchanged.

## Scope

### 1. `IChatPipeline` and `ChatPipeline` (in `MovieTracker.Api/Chat/`)
- `IChatPipeline` is the only thing the endpoints call for chat work.
- `ChatPipeline` owns everything that builds an answer, moved from `ChatTurnEndpoints` with no logic change:
  - starting a session with `ChatStartSystemPrompt`
  - loading the session (chat not found stays a distinct outcome so the endpoint can return 404)
  - the turn: `MovieListResponseFormat`, the context reducer, `agent.RunAsync`, tool-call and context telemetry, usage logging and cost
  - the funny fact (same parallel start and same failure handling)
  - movie hydration (`ToClientMessageAsync`, `ProcessMovieAsync`, the session and distributed caches)
  - session save with the `HydratedMovieLimit` trim
- One shared code path for both ask endpoints. The `/ask` result (full history) and the `/ask/v2` result (new turn only) are built from that same path.
- Keep each file small and single-purpose. Propose the split to the owner before implementing, for example: `ChatPipeline` (orchestration), `ChatContextReducer` (reducer and tool-call summaries), `MovieHydrator` (assistant message to `MovieViewModel`), and usage logging next to the existing telemetry code.
- Constants keep their current values (`MaxModelTurns`, `CollapsedToolResultChars`, `CollapsedArgumentChars`, `CollapsedResultNames`, `HydratedMovieLimit`).

### 2. Endpoints become HTTP-only
- `ChatTurnEndpoints` keeps `MapChatEndpoints`, the three routes and their names (`ChatStart`, `ChatAsk`, `ChatAskV2`).
- Each endpoint: start its span (same span names: `movie-tracker-api.chat-start`, `movie-tracker-api.chat-ask`, `movie-tracker-api.chat-ask-v2`), read and validate the request, call `IChatPipeline`, map the outcome to the same HTTP result as today.
- Error responses stay byte-for-byte the same: the 400 `Invalid request, missing ask object` body, the 404 `Chat not found` body, the 503 bodies, and the same log messages.

### 3. Registration
- Register `IChatPipeline` (and any split-out classes) in one `Chat/ChatPipelineRegistration.cs` with an `Add...` extension method called from `Program.cs`.
- Pick lifetimes that match the dependencies (`ChatSessionRepository`, `AIAgent`, `ChatPlanner`, `IDistributedCache`, `TMDbClient`, `ModelPricing`, `IConfiguration`, loggers). Explain the choice in the plan.

### 4. Eval rating cases always skipped (`MovieTracker.Evals` only)
- In the Nov 18 baseline both `rating` cases were `SKIPPED` (no truth value), so the rating check measures nothing.
- First confirm the cause in `Cases/GeneratedCaseBuilder.BuildRatingAsync` and `Tmdb/OmdbClient`. The likely cause: TMDb popular page 1 is mostly new releases, and OMDb returns `imdbRating` = `N/A` for them.
- Then propose a fix to the owner before implementing. Recommended: rating cases take their titles from TMDb `movie/top_rated` page 1 (picked the same way as run titles), while every other check keeps the popular run titles.
- Do not change the rating check logic or its question template.

### 5. Docs
- Update the `Chat/` line in the `CLAUDE.md` project structure. Nothing else in `CLAUDE.md` changes.

## Out of scope
- Any agent, prompt, tool, model or response-shape change. No resolver, no critic, no Copilot SDK (that is the Option B spec).
- New endpoints, new config values, feature switches.
- Merging `/ask` and `/ask/v2` into one route (both stay).
- Changes to other eval checks, templates or reporting.

---

## Steps

0. Branch: on `main`, pull, confirm `012-eval-cleanup` is merged and there are no tracked changes (the untracked `specs/013-chat-pipeline/` folder is expected and comes along), then create and switch to `013-chat-pipeline`. If `012` is not merged, `main` has tracked changes, or the branch already exists, stop and report.
1. Spec Kit: run `/speckit-specify`, `/speckit-plan`, `/speckit-tasks`, then `/speckit-implement`. Before implement, get the owner's OK on the file split (item 1) and the rating fix (item 4).
2. After each stage, run a Codex review with `codex exec`, stdin closed (append `< /dev/null`).
   Guardrails: stay focused on items 1 to 5, cut inflated or overkill work, and keep it simple without losing any listed change.
   Fix the findings and review again. Repeat until Codex approves, up to 5 rounds per stage. If round 5 is not approved, stop and report the open findings.
3. Build: `dotnet build movie-tracker-api.slnx`. There must be no new warnings compared with `main`.
4. Run the eval: `dotnet run --project MovieTracker.Evals` (needs `az login`).
   - Rating cases must now be graded (not `SKIPPED`).
   - No check may get worse because of the refactor. Titles are random and the model is not deterministic, so if a check drops, rerun once; if it still drops, find the cause in the diff before reporting.
5. Report to the owner: the file split, the console table, the totals line, the per-check comparison, Codex rounds per stage, and anything left open.

## Done when
- `dotnet build movie-tracker-api.slnx` succeeds with no new warnings.
- `ChatTurnEndpoints` contains only route mapping, request validation, spans and HTTP result mapping. No agent call, reducer, response format, funny fact, hydration or save logic is left in it.
- `/api/chat/start`, `/api/chat/{chatId}/ask` and `/api/chat/{chatId}/ask/v2` return the same shapes and status codes as on `main`.
- `/ask` and `/ask/v2` share one code path in `ChatPipeline`.
- The eval run grades both rating cases and no check is worse than the Nov 18 baseline for a reason in the code.
- Only `MovieTracker.Api/Chat/`, `MovieTracker.Api/Program.cs`, any moved usage-logging file under `MovieTracker.Api/Telemetry/`, the rating fix in `MovieTracker.Evals/`, `CLAUDE.md` (one line) and `specs/013-chat-pipeline/` change.
