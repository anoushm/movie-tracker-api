# Phase 0 Research: Movie Agent Evaluation Harness

**Feature**: 008-eval-harness | **Date**: 2026-11-09 | **Reworked**: 2026-11-10 (rich-eval)

Decisions required to unblock Phase 1 design. Every decision below is pinned to a narrow source (Microsoft Learn, the MAF GitHub source, the API's own code, the public third-party contracts the harness has to call, or the authoritative `rich-eval-prompt.md`). Decisions 2, 4, 7, 9, and 10 are updated from the prior rework to match the rich-eval design.

---

## Decision 1 — Hosting the API without changing the API project

**Decision**: Use `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<global::Program>`. The harness's own `Program` lives in `namespace MovieTracker.Evals;` and does not shadow the global type. Pin the environment to `Development` with `factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"))`. Call `CreateClient()` for every chat turn and read `factory.Services.GetRequiredService<IConfiguration>()` for the key check.

**Rationale**: unchanged from the prior rework. The .NET SDK emits `public partial class Program { }` in the global namespace from top-level statements; `MovieTracker.Api/Program.cs` is a top-level-statements file. Spec FR-018 requires this binding; FR-018 also halts the work if the binding does not compile without a change under `MovieTracker.Api/`.

---

## Decision 2 — Grading engine: grouped `LocalEvaluator` per check kind, read `BooleanMetric` off the result

**Decision**: Define a harness-local interface `IEvalCheck` with `string Name { get; }` and `EvalCheckResult Evaluate(EvalItem item)`. Implement **one `IEvalCheck` per check kind** in its own file (nine files total: `ListIncludesCheck`, `DirectorCheck`, `ActorFilmographyCheck`, `IdentifyByPlotCheck`, `GenreDecadeCheck`, `RatingCheck`, `TrailerCheck`, `FollowUpDirectorCheck`, `FunnyFactNotEmptyCheck`). Group cases by check kind; for each group, construct one `new LocalEvaluator(FunctionEvaluator.Create(name, check.Evaluate))` and call `EvaluateAsync(groupItems, evalName: name)` **once per group**. Read `AgentEvaluationResults.Items[i].Metrics[name]` back as `Microsoft.Extensions.AI.Evaluation.BooleanMetric` and project `boolean.Value` + `boolean.Reason` into the per-case `CaseResult`.

**Rich-eval-prompt rules to satisfy** (§3):
- "One check per file: an `IEvalCheck` with a name and `EvalCheckResult Evaluate(EvalItem item)`."
- "Group the run's items by check. For each check, one `LocalEvaluator` with that check's `FunctionEvaluator`, evaluating all its items in one call."
- "Read pass/fail and reason from the results MAF returns. No captured variables."
- "Carry each case's expected data as typed JSON in `EvalItem.ExpectedOutput`; `funnyFact` stays in `EvalItem.Context`. No `|`-separated strings."
- "Remove `EvalCheckRegistry` and `CheckEvaluator` in their current form."
- "Verify every MAF API used against `Microsoft.Agents.AI` 1.17.0."

**1.17.0 verification (provisional, as documented on Microsoft Learn under `Microsoft.Agents.AI` and in the open-source `microsoft/agent-framework` source; spec FR-020 halts the work if any of this is missing from the installed assembly)**:

- `LocalEvaluator(params EvalCheck[] checks)`; `Task<AgentEvaluationResults> EvaluateAsync(IReadOnlyList<EvalItem> items, string evalName = "Local Eval", CancellationToken ct = default)`.
- `FunctionEvaluator.Create(string name, Func<EvalItem, EvalCheckResult> check)` returning `EvalCheck`.
- `EvalItem(string query, string response)` plus settable `Response` / `Context` / `ExpectedOutput` (all string?).
- `EvalCheckResult(bool Passed, string Reason, string CheckName)`.
- `AgentEvaluationResults.Items` returns `IReadOnlyList<EvaluationResult>` from `Microsoft.Extensions.AI.Evaluation`.
- `EvaluationResult.Metrics[name]` is an `EvaluationMetric`; for `FunctionEvaluator`-produced results it is a `BooleanMetric` carrying both `Value` (nullable bool) and `Reason` (string) — confirmed by `LocalEvaluator`'s source (`new BooleanMetric(EvalCheckResult.CheckName, EvalCheckResult.Passed, reason: EvalCheckResult.Reason)`).

**Mapping the nine check kinds to `EvalItem`**:

| Check kind | `EvalItem.Response` | `EvalItem.Context` JSON | `EvalItem.ExpectedOutput` JSON | Pass predicate |
|---|---|---|---|---|
| `listIncludes` | agent answer text | `{ funnyFact, movieList[] }` | `{ expectedTitles: string[] }` (no TMDb ids — anchors are hand-written) | every `expectedTitles` entry is in the answer per FR-023 rule 1: with no id to try, the check falls straight to the title-fallback path (match against `turn.movieList[*].title` OR `turn.text`). |
| `director` | agent answer text | `{ funnyFact, movieList[] }` | `{ directorName: string }` | answer contains `directorName` (case-insensitive). |
| `actorFilmography` | agent answer text | `{ funnyFact, movieList[] }` | `{ actorName: string, filmographyIds: int[], filmographyTitles: string[] }` | `movieList` non-empty AND every entry matches per FR-023 rule 2 (item-in-filmography). |
| `identifyByPlot` | agent answer text | `{ funnyFact, movieList[] }` | `{ sourceId: int, sourceTitle: string }` | the source movie is in the answer per FR-023 rule 1. |
| `genreDecade` | agent answer text | `{ funnyFact, movieList[] }` | `{ genreId: int, genreName: string, decadeStart: int }` | `movieList` non-empty AND, per entry, (`genreId` in `entry.genreIds` OR `genreName` in `entry.genreNames`) AND `entry.releaseDate` year in `[decadeStart, decadeStart+9]`. |
| `rating` | agent answer text | `{ funnyFact, movieList[] }` | `{ imdbRating: string }` | answer contains `imdbRating` (case-insensitive). |
| `trailer` | agent answer text | `{ funnyFact, movieList[] }` | `{ videoKeys: string[] }` | the answer contains at least one `[TRAILER]<url>[/TRAILER]` substring whose YouTube `v=` key is in `videoKeys` (multiple tagged URLs are allowed; any one qualifying key passes). |
| `followUpDirector` | turn 2 answer text | `{ funnyFact, movieList[] }` (from the turn 2 response) | `{ directorName: string }` | turn 2 answer contains `directorName` (case-insensitive). |
| `funnyFactNotEmpty` | agent answer text | `{ funnyFact, movieList[] }` | (ExpectedOutput null) | `funnyFact` (from the deserialized Context) is non-null and non-empty. |

**Why the previous "captured variable" `CheckEvaluator` is gone**: the grading result's reason survives the `FunctionEvaluator` → `LocalEvaluator` → `AgentEvaluationResults` → `EvaluationResult.Metrics[name]` → `BooleanMetric.Reason` path; no closure capture is needed. One evaluator-per-group plus one check per evaluator keeps "every check runs every item" trivially correct (every item in the group is for that check).

**Alternatives considered**:

- **One `LocalEvaluator` holding all nine checks, applied to all items.** Rejected: each check would then need an internal `is this check for me?` guard (re-introducing a central dispatcher), and each item would be run through all nine evaluators (eight no-ops).
- **Capture the `EvalCheckResult` in a closure (previous CheckEvaluator design).** Rejected by rich-eval-prompt §3.
- **Put movieList in `ExpectedOutput`.** Rejected: `ExpectedOutput` is for expected (truth) data; actual response fields belong with the response.
- **Put movieList in a sidecar dictionary keyed by case id.** Rejected: that is a captured variable disguised as a service.

---

## Decision 3 — Where configuration comes from

**Decision**: Read `TheMovieDb:Api-Key` and `OpenMovieDb:Api-Key` from `factory.Services.GetRequiredService<IConfiguration>()` **after** the factory has been constructed. If either is null/empty, stop with a message naming the missing key before any case runs.

**Rationale**: Spec FR-013 carries forward the original rule. The API reads `TheMovieDb:Api-Key` at startup and `OpenMovieDb:Api-Key` lazily inside `OmdbTools`; the harness must verify both up front because `OpenMovieDb:Api-Key` is lazy on the API side and the harness needs it for every `rating` case.

---

## Decision 4 — TMDb endpoints the harness calls directly

**Decision**: The harness uses its own `HttpClient` (base `https://api.themoviedb.org/3/`) with a v3-key query parameter (`?api_key=<TheMovieDb:Api-Key>`) and calls these endpoints:

| Purpose | Request | Fields used |
|---|---|---|
| Regional popular selection (one title per region) | `GET /movie/popular?api_key=<key>&region=<US\|IN\|SE>&page=<randomPageIn1..5>` | pick one random entry from `results[]`; keep `id` and `title` |
| Overview + release date + IMDb id + genres for a selected title | `GET /movie/{id}?api_key=<key>&append_to_response=external_ids` | `overview`, `release_date`, `external_ids.imdb_id`, `genres[].{id,name}` |
| Director and lead actor | `GET /movie/{id}/credits?api_key=<key>` | first `crew[*]` with `job == "Director"` → `name`; `cast[*]` with `order == 0` → `name` and `id` |
| Actor filmography (for `actorFilmography`) | `GET /person/{person_id}/movie_credits?api_key=<key>` | `cast[*].{id, title}` (acting credits, as an id set + title set) |
| Trailer video keys (for `trailer`) | `GET /movie/{id}/videos?api_key=<key>` | `results[*]` filtered to `site == "YouTube"` AND `type == "Trailer"` → `key` |

**Random selection**: `System.Random.Shared` picks a page in `1..5` independently for each region and one random result index within that page. No seed exposed (rich-eval-prompt out-of-scope).

**Deduplication across regions**: the harness collects three titles across the three regions and compares TMDb ids. On any id collision the harness resamples a title from the same already-chosen page for the affected region (never rerolling the page) until three distinct titles are chosen (spec FR-006).

**Failure handling**:

- `GET /movie/popular` returning non-2xx, unparseable payload, or zero entries on the chosen page for a region → **precondition failure**. The run halts with a plain message naming the failure and writes no results file.
- `GET /movie/{id}`, `/movie/{id}/credits`, `/person/{id}/movie_credits`, `/movie/{id}/videos` returning non-2xx or unparseable payload for a successfully selected title → same precondition halt. HTTP failures during per-case lookups are owner-rerun events, not FR-016 "no truth value" cases.
- The same endpoints returning 2xx with a missing/empty target field for a case whose template produces a case regardless → FR-016 `no truth value` FAIL for that generated case. The run continues.
- `trailer` is additionally gated by qualification (FR-007): if the videos filter yields an empty set for the selected movie, no case is produced (not an FR-016 FAIL).

---

## Decision 5 — OMDb endpoint the harness calls directly

**Decision**: For the one `rating` case per run, the harness calls `GET https://www.omdbapi.com/?i=<imdbId>&apikey=<OpenMovieDb:Api-Key>` where `imdbId` is the IMDb id the TMDb `/movie/{id}?append_to_response=external_ids` call returned. The `imdbRating` field is the truth value loaded into `EvalItem.ExpectedOutput.imdbRating`; the agent's answer must contain that string (case-insensitive).

**Failure handling**:

- OMDb `imdbRating == "N/A"` or the field absent on an otherwise 2xx response → FR-016 `no truth value` FAIL for the `rating` case.
- The upstream TMDb `external_ids.imdb_id` null/empty on an otherwise 2xx TMDb response → same FR-016 path.
- Non-2xx or unparseable payload from OMDb → precondition halt, consistent with the TMDb policy.

---

## Decision 6 — Mvc.Testing and MAF package versions

**Decision**: `Microsoft.AspNetCore.Mvc.Testing` pinned to `10.0.10` (same major/minor/patch as the API's ASP.NET Core packages). `Microsoft.Agents.AI` pinned to `1.17.0` (same as the API).

**Rationale**: pinning matches the API's own references and avoids a cross-version host-builder / evaluator-type split. Explicit pins (not floating ranges) keep the restore deterministic.

---

## Decision 7 — Results file schema and per-check comparison semantics

**Decision**: `MovieTracker.Evals/results/last-run.json` is a UTF-8 JSON document shaped as `{ runAt, total, passed, perCheck[], cases[] }`:

- `runAt` — ISO-8601 UTC timestamp.
- `total` — produced-case count (typically 10; smaller **only** when `trailer` did not qualify — a case pre-marked FAIL under FR-016 is still produced and still counted, it just always fails).
- `passed` — count of `cases[*].passed == true`.
- `perCheck` — one entry per check kind that produced at least one case, each `{ check, passed, total }`.
- `cases` — one entry per produced case, each `{ id, check, passed, reason }`.

The full schema is in [`contracts/last-run.schema.json`](contracts/last-run.schema.json).

The next run's comparison walks a **fixed nine-check-kind order** (`listIncludes`, `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`) and prints `<check>: <prev>/<total> -> <cur>/<total>` for each, rendering `-` for a side where that check kind produced no cases. A `total:` line follows, sourced from each side's `RunResult.Passed`/`RunResult.Total`. No per-id flip list (spec Out-of-Scope).

---

## Decision 8 — Configuration-fail-fast order

**Decision**: Startup sequence:

1. Construct `WebApplicationFactory<global::Program>`. If the API's own fail-fast fires (missing `AzureOpenAI:*`, `TheMovieDb:Api-Key`, `Cosmos:*`), let it propagate — the message already names the missing key.
2. Resolve `IConfiguration` from `factory.Services`.
3. Check `TheMovieDb:Api-Key` and `OpenMovieDb:Api-Key`. Print the named missing key and exit with a non-zero status if either is absent.

---

## Decision 9 — Case identifiers

**Decision**:

- Plot-anchor case ids are the strings in `eval-movie-cases.json` (`plot-ship-sinks`, `plot-mob-family`), stable across runs.
- Generated case ids are `<templateId>-<slot>` where `templateId` is the template's `id` field and `slot` is `1..3` (the 1-based index of the selected run title). Different templates may pick different slots on the same run.

**Rationale**: matches spec FR-008. The per-check comparison keys by check kind, not by id, so the same `director-2` id referring to different titles across runs is not a problem.

---

## Decision 10 — Chat session lifecycle per case

**Decision**: One fresh chat session per executed case. For single-turn cases: `GET /api/chat/start` → `chatId`; `POST /api/chat/{chatId}/ask/v2` with `{ "input": "<question>" }` → parse `funnyFact`, `turn.text`, and `turn.movieList`. For `followUpDirector` (multi-turn): one `/api/chat/start` + two POSTs on the same `chatId` (turn 1 answer discarded, turn 2 graded). Cases pre-marked FAIL under FR-016 do not open a chat session.

---

## Decision 11 — Resolving the `MovieTracker.Evals` source folder at run time

**Decision**: Resolve the project folder with a utility type that captures `[CallerFilePath]` at compile time, and use `Path.GetDirectoryName` on that path. All three run-time file paths — `eval-movie-cases.json`, `eval-movie-templates.json`, `results/last-run.json` — are built from that folder. `bin/` is never a read or write location. (Unchanged from prior rework; see prior rationale.)

---

## Decision 12 — Loader checks unique ids only

**Decision**: The harness does **not** reference a JSON Schema validation package. The loader for `eval-movie-cases.json` and `eval-movie-templates.json` enforces only that `id` values are unique within each file. It does not enforce entry count, fixed id set, per-check-kind distribution, or a specific shape for the multi-turn `turns` field (owner-review per spec FR-004a).

The three `contracts/*.schema.json` files remain documentation only: they describe the shape of a well-formed file for human readers, not a shape the harness enforces at run time.

---

## Decision 13 — Picking one of the three run titles per template

**Decision**: Each title-based template picks one of the three run titles **at random using `System.Random.Shared`**, independently per template. The chosen slot is reflected in the generated case id (`<templateId>-<slot>`). The random choice is non-seeded (rich-eval-prompt out-of-scope rules out a random seed); demos are deliberately slightly variable per run.

**Rationale**: the rich-eval-prompt says "Each title-based check uses one of these 3 movies." The simplest rule that keeps demo output regionally diverse is per-template random choice. Deterministic round-robin was considered but rejected as adding unnecessary complexity for a demo-sized run.

---

## Open questions

None. All NEEDS CLARIFICATION items in the plan template are resolved.
