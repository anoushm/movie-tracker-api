---
description: "Task list for 008-eval-harness implementation (rich-eval)"
---

# Tasks: Movie Agent Evaluation Harness (rich-eval)

**Input**: Design documents from `specs/008-eval-harness/`

**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/, quickstart.md — all Codex-approved for the rich-eval rework.

**Tests**: The rich-eval-prompt explicitly rejects a separate test project; the harness itself is the test. No xUnit/MSTest tasks are generated. Tasks marked **[owner]** are acceptance checks the owner executes after implementation — the implementer never runs the harness with real keys (rich-eval-prompt §Steps 6: "Do not run the harness with real keys; the owner does the live runs").

**Organization**: Tasks are grouped by user story (US1/US2/US3 from `spec.md`) so each story can be implemented and verified independently. Many tasks modify or delete existing files from the prior (now-superseded) rework; those files already live in `MovieTracker.Evals/`.

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: `[US1]`, `[US2]`, or `[US3]` — only on user-story phase tasks.
- **[owner]**: task is executed by the owner, not the implementer. Live-run acceptance checks only.
- Every task description names the file(s) it touches.

## Path Conventions

- Harness project root: `MovieTracker.Evals/` (sibling of `MovieTracker.Api/`, flat layout, one type per file).
- Spec folder: `specs/008-eval-harness/`.
- Solution file: `movie-tracker-api.slnx` — **unchanged** in this rework.
- Nothing under `MovieTracker.Api/`, `infrastructure/`, `.github/`, `CLAUDE.md`, or `specs/001..007/` is touched (FR-017).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: rewrite the input files to match the rich-eval design and verify the MAF 1.17.0 surface the new grading path reads from (which is a superset of what the prior rework verified).

- [X] T001 Rewrite `MovieTracker.Evals/eval-movie-templates.json` to hold **exactly eight** templates per spec FR-005: `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`. Single-turn templates carry a `template` string with the applicable placeholders from `{title}`/`{actor}`/`{overview}`/`{genre}`/`{decade}`; the only multi-turn template, `followUpDirector`, carries a `turns` array of exactly two strings (`"Tell me about {title}"`, `"Who directed it?"`). The file's shape MUST match `contracts/eval-movie-templates.schema.json`.
- [X] T002 Verify `MovieTracker.Evals/eval-movie-cases.json` still holds exactly the two plot anchors per spec FR-004 (`plot-ship-sinks` expected `["Titanic"]` and `plot-mob-family` expected `["The Godfather"]`). No change expected; confirm.
- [X] T003 Verify against the `Microsoft.Agents.AI` version `1.17.0` `PackageReference` already present in `MovieTracker.Evals/MovieTracker.Evals.csproj`, by inspecting the installed assembly (`<user-nuget-cache>/microsoft.agents.ai/1.17.0/lib/net10.0/Microsoft.Agents.AI.dll` and its accompanying XML docs), that every FR-020 verification item is present: `Microsoft.Agents.AI.LocalEvaluator` with `.ctor(params EvalCheck[])` + `EvaluateAsync(IReadOnlyList<EvalItem>, string, CancellationToken)`; `Microsoft.Agents.AI.FunctionEvaluator.Create(string, Func<EvalItem, EvalCheckResult>)`; `Microsoft.Agents.AI.EvalItem(string query, string response)` constructor and settable `Response` / `Context` / `ExpectedOutput` (string?) properties; `Microsoft.Agents.AI.EvalCheckResult(bool, string, string)` constructor; `Microsoft.Agents.AI.AgentEvaluationResults.Items` returning a per-item collection whose per-item `Metrics[checkName]` resolves to a `Microsoft.Extensions.AI.Evaluation.BooleanMetric` carrying both a readable pass-boolean member and a readable reason-string member. If any is missing, STOP and report to the owner.

**Checkpoint**: both JSON input files reflect the rich-eval design; the installed MAF surface is verified as adequate.

---

## Phase 2: Foundational (data shapes, host, updated TemplateLoader, per-check records)

**Purpose**: reshape the data model layer to match `data-model.md` for the rich-eval design. The composition root and the per-check evaluators depend on this phase.

**CRITICAL**: no user-story work can start until this phase is complete.

**Intermediate build state**: Phase 2 deliberately deletes types (`CheckEvaluator`, `EvalCheckRegistry`, `SearchMatch`) and rewrites enums / shapes that the current `Program.cs` and `GeneratedCaseBuilder.cs` still reference. The project **will not build** between the start of Phase 2a and the end of Phase 3 (T041 + T042). The next build-green checkpoint is at the end of Phase 3; keep this in mind when sequencing work.

### 2a. Deletes (land first)

- [X] T004 Delete `MovieTracker.Evals/CheckEvaluator.cs` (captured-variable approach replaced by grouped-LocalEvaluator + BooleanMetric read per research.md Decision 2). Blocks T024 and T041 (the composition root that used to invoke it).
- [X] T005 Delete `MovieTracker.Evals/EvalCheckRegistry.cs` (rich-eval-prompt §3 removes the registry; per-check logic moves into one `IEvalCheck` file each). Blocks T014–T023.
- [X] T006 Delete `MovieTracker.Evals/SearchMatch.cs` (the removed `asksClarification` template was its only consumer). Blocks T029 (updated TMDb client).

### 2b. Enum / core-type rewrites

- [X] T007 [P] Rewrite `MovieTracker.Evals/TemplateCheck.cs` with the eight rich-eval values: `Director`, `ActorFilmography`, `IdentifyByPlot`, `GenreDecade`, `Rating`, `Trailer`, `FollowUpDirector`, `FunnyFactNotEmpty`. File-scoped namespace `MovieTracker.Evals`.
- [X] T008 [P] Rewrite `MovieTracker.Evals/CheckOrder.cs` with the fixed nine-check-kind print order (spec FR-011; research.md Decision 7): `listIncludes`, `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`.
- [X] T009 [P] Update `MovieTracker.Evals/RunTitle.cs` — slot is `1..3` with regional order US, IN, SE (data-model.md "RunTitle"). The shape `public sealed record RunTitle(int TmdbId, string Title, Region Region, int Slot)` stays the same; only the slot-value contract changes.
- [X] T010 [P] Update `MovieTracker.Evals/CaseTemplate.cs` to carry both `Template` (`string?`, single-turn) and `Turns` (`IReadOnlyList<string>?`, multi-turn) alongside `Id` and `Check` (`TemplateCheck`). Exactly one of `Template` / `Turns` is populated per entry; the JSON converter on `Check` MUST accept the new enum names.
- [X] T011 [P] Update `MovieTracker.Evals/CaseTemplateFile.cs` so the deserialization carrier matches the top-level `{ "templates": [...] }` shape in `contracts/eval-movie-templates.schema.json`.
- [X] T012 [P] Update `MovieTracker.Evals/GeneratedCase.cs` per data-model.md "GeneratedCase": fields `Id` string, `Question` string (single-turn question with placeholders filled, OR first turn for multi-turn), `Turns` `IReadOnlyList<string>?` (populated only for multi-turn), `Check` `TemplateCheck`, `ExpectedJson` `string?` (the per-check expected record pre-serialized to JSON, or null for `funnyFactNotEmpty` and for `NoTruthValue == true` cases), `NoTruthValue` bool.
- [X] T013 Update `MovieTracker.Evals/GeneratedCaseBatch.cs` to carry the per-template produced-count the Reporter needs — now only one: `TrailerProducedCount` (the other templates always produce a case). Shape: `public sealed record GeneratedCaseBatch(IReadOnlyList<GeneratedCase> Cases, int TrailerProducedCount)`.

### 2c. New per-check record types (typed `ExpectedOutput` payloads)

- [X] T014 [P] Create `MovieTracker.Evals/ActualResponsePayload.cs`: `public sealed record ActualResponsePayload(string? FunnyFact, IReadOnlyList<MovieListEntry> MovieList)`. Serialized to JSON for `EvalItem.Context`; every `IEvalCheck` that reads structured response data deserializes this.
- [X] T015 [P] Create `MovieTracker.Evals/MovieListEntry.cs`: `public sealed record MovieListEntry(int Id, string Title, string? ReleaseDate, IReadOnlyList<int> GenreIds, IReadOnlyList<string> GenreNames)`.
- [X] T016 [P] Create `MovieTracker.Evals/ListIncludesExpected.cs`: `public sealed record ListIncludesExpected(IReadOnlyList<string> ExpectedTitles)`.
- [X] T017 [P] Create `MovieTracker.Evals/DirectorExpected.cs`: `public sealed record DirectorExpected(string DirectorName)`. Reused by both `director` and `followUpDirector` checks (data-model.md "GeneratedCase").
- [X] T018 [P] Create `MovieTracker.Evals/ActorFilmographyExpected.cs`: `public sealed record ActorFilmographyExpected(string ActorName, IReadOnlyList<int> FilmographyIds, IReadOnlyList<string> FilmographyTitles)`.
- [X] T019 [P] Create `MovieTracker.Evals/IdentifyByPlotExpected.cs`: `public sealed record IdentifyByPlotExpected(int SourceId, string SourceTitle)`.
- [X] T020 [P] Create `MovieTracker.Evals/GenreDecadeExpected.cs`: `public sealed record GenreDecadeExpected(int GenreId, string GenreName, int DecadeStart)`.
- [X] T021 [P] Create `MovieTracker.Evals/RatingExpected.cs`: `public sealed record RatingExpected(string ImdbRating)`.
- [X] T022 [P] Create `MovieTracker.Evals/TrailerExpected.cs`: `public sealed record TrailerExpected(IReadOnlyList<string> VideoKeys)`.

### 2d. `IEvalCheck` interface

- [X] T023 Create `MovieTracker.Evals/IEvalCheck.cs`: `public interface IEvalCheck { string Name { get; } Microsoft.Agents.AI.EvalCheckResult Evaluate(Microsoft.Agents.AI.EvalItem item); }`. File-scoped namespace. One type per file. Blocks every `*Check.cs` task in Phase 3.

### 2e. Sequenced foundational updates

- [X] T024 Update `MovieTracker.Evals/AgentReply.cs` to carry the movie-list projection: `public sealed record AgentReply(string Answer, string? FunnyFact, IReadOnlyList<MovieListEntry> MovieList)`. Keeps `AgentClient` consumers and `RunRepetitionAsync`-equivalent code using `MovieListEntry` records directly.
- [X] T025 Update `MovieTracker.Evals/AgentClient.cs` so `AskAsync` parses `turn.movieList[*]` into `MovieListEntry` records (ids coerced from string to int; `releaseDate` kept as the raw ISO string; `genreIds` and `genreNames` as arrays, defaulting to empty arrays when absent) and returns them on the enriched `AgentReply`. Keep `StartChatAsync` unchanged.
- [X] T026 Update `MovieTracker.Evals/MovieDetails.cs` to carry the additional fields the rich-eval truth resolution needs: `public sealed record MovieDetails(string? ReleaseDate, string? ImdbId, string? Overview, IReadOnlyList<MovieGenre> Genres)`. Also create `MovieTracker.Evals/MovieGenre.cs` (`public sealed record MovieGenre(int Id, string Name)`) in the same wave (one type per file).
- [X] T027 Update `MovieTracker.Evals/MovieCredits.cs` to also carry the lead-actor TMDb id needed by `actorFilmography`'s person-credits lookup: `public sealed record MovieCredits(string? DirectorName, int? LeadActorId, string? LeadActorName)`.
- [X] T028 Update `MovieTracker.Evals/TemplateLoader.cs` so it enforces only unique ids (spec FR-004a). No shape or count enforcement. It MUST deserialize entries carrying `template` OR `turns` without error.

### 2f. HTTP client additions / rewrites

- [X] T029 Rewrite `MovieTracker.Evals/TmdbClient.cs` for the rich-eval endpoint set (research.md Decision 4):
  - Keep `Task<IReadOnlyList<MovieSummary>> GetPopularAsync(Region region, int page)` — unchanged, used by `RunTitleSelector`.
  - Update `Task<MovieDetails?> GetMovieDetailsAsync(int tmdbId)` to call `GET /movie/{id}?api_key=<key>&append_to_response=external_ids` and project `release_date`, `external_ids.imdb_id`, `overview`, and `genres[].{id, name}` into the updated `MovieDetails` + `MovieGenre` shapes. **Preserve each field independently**: a missing `imdb_id` does not null the whole result — set that specific field to `null` and keep the others; an empty `genres[]` becomes an empty list. The method returns a `MovieDetails` on any 2xx response whose body parses as a JSON **object** (fields absent inside the object simply map to `null`/empty). It **throws** `InvalidOperationException` on (a) non-2xx responses, (b) bodies that cannot be parsed as JSON at all (malformed payload), and (c) 2xx responses whose JSON parses but is **not an object** (e.g. `null`, `[]`, `42`, `"text"` — invalid-shape responses TMDb never returns for this endpoint, treated as unparseable-for-our-purposes). The HTTP error messages MUST redact `api_key` (keep the Sanitize helper from the prior rework). T040 is responsible for turning any per-check-required missing field into a per-case `NoTruthValue = true`.
  - Update `Task<MovieCredits?> GetCreditsAsync(int tmdbId)` to also project the cast-order-0 `id` into `MovieCredits.LeadActorId`.
  - Add `Task<ActorFilmography?> GetPersonMovieCreditsAsync(int personId)` calling `GET /person/{personId}/movie_credits?api_key=<key>`, returning `cast[*].{id, title}` projected into a new `ActorFilmography` record (create `MovieTracker.Evals/ActorFilmography.cs`: `public sealed record ActorFilmography(IReadOnlyList<int> MovieIds, IReadOnlyList<string> MovieTitles)`). Return `null` on 2xx-with-empty-credits; throw on non-2xx.
  - Add `Task<IReadOnlyList<string>?> GetTrailerVideoKeysAsync(int tmdbId)` calling `GET /movie/{id}/videos?api_key=<key>`, filtering `results[*]` to `site == "YouTube"` AND `type == "Trailer"`, projecting `key`. Return `null` on 2xx-with-empty-filtered-set; throw on non-2xx.
  - Remove `SearchByTitleAsync` and `GetKeywordsAsync` (the superseded templates are gone).

**Checkpoint**: DTOs match data-model.md. `TmdbClient` exposes every method the rich-eval truth resolution needs. The project **does not build** yet — `Program.cs` and the current `GeneratedCaseBuilder.cs` still reference deleted / reshaped types; the next build-green checkpoint is at the end of Phase 3 after T041 + T042 wire the new composition root.

---

## Phase 3: User Story 1 — Score the current agent on demand (Priority: P1) 🎯 MVP

**Goal**: one run produces ~10 cases (2 anchors + up to 8 templated) with grades that flow through `LocalEvaluator` and are read back as `BooleanMetric` from `EvaluationResult.Metrics`. No captured variables. The run writes `results/last-run.json` in the rich-eval schema.

### 3a. `IEvalCheck` implementations (one file each, all parallel)

- [X] T030 [P] [US1] Create `MovieTracker.Evals/ListIncludesCheck.cs` implementing `IEvalCheck` with `Name = "listIncludes"`. Deserializes `EvalItem.ExpectedOutput` to `ListIncludesExpected` and `EvalItem.Context` to `ActualResponsePayload`. For every `ExpectedTitles` entry, pass per FR-023 rule 1 title-fallback path: a match on any `MovieList[*].Title` OR on `EvalItem.Response` (case-insensitive) counts. Returns `new EvalCheckResult(passed, reason, Name)`.
- [X] T031 [P] [US1] Create `MovieTracker.Evals/DirectorCheck.cs` implementing `IEvalCheck` with `Name = "director"`. Deserializes `EvalItem.ExpectedOutput` to `DirectorExpected`. Passes when `EvalItem.Response` contains `DirectorName` (case-insensitive).
- [X] T032 [P] [US1] Create `MovieTracker.Evals/ActorFilmographyCheck.cs` implementing `IEvalCheck` with `Name = "actorFilmography"`. Deserializes expected to `ActorFilmographyExpected` and context to `ActualResponsePayload`. Passes when `MovieList` is non-empty AND every entry matches per FR-023 rule 2 (id first in `FilmographyIds`, title fallback in `FilmographyTitles`, case-insensitive). Reason names the first offending entry on failure.
- [X] T033 [P] [US1] Create `MovieTracker.Evals/IdentifyByPlotCheck.cs` implementing `IEvalCheck` with `Name = "identifyByPlot"`. Deserializes expected to `IdentifyByPlotExpected` and context to `ActualResponsePayload`. Passes per FR-023 rule 1 (SourceId in `MovieList[*].Id` first; else `SourceTitle` in `MovieList[*].Title` OR in `EvalItem.Response`).
- [X] T034 [P] [US1] Create `MovieTracker.Evals/GenreDecadeCheck.cs` implementing `IEvalCheck` with `Name = "genreDecade"`. Deserializes expected to `GenreDecadeExpected` and context to `ActualResponsePayload`. Passes when `MovieList` is non-empty AND, for every entry, (`GenreId in entry.GenreIds` OR `GenreName in entry.GenreNames`) AND the four-digit year prefix of `entry.ReleaseDate` is in `[DecadeStart, DecadeStart + 9]`.
- [X] T035 [P] [US1] Create `MovieTracker.Evals/RatingCheck.cs` implementing `IEvalCheck` with `Name = "rating"`. Deserializes expected to `RatingExpected`. Passes when `EvalItem.Response` contains `ImdbRating` (case-insensitive).
- [X] T036 [P] [US1] Create `MovieTracker.Evals/TrailerCheck.cs` implementing `IEvalCheck` with `Name = "trailer"`. Deserializes expected to `TrailerExpected`. Scan `EvalItem.Response` for every `[TRAILER]<url>[/TRAILER]` substring (compiled regex); extract the YouTube `v=` key from each URL; pass when any extracted key is in `VideoKeys` (spec FR-005 trailer — multiple tags allowed, any qualifying key passes). Reason on failure names the failing tag count or "no trailer tag found".
- [X] T037 [P] [US1] Create `MovieTracker.Evals/FollowUpDirectorCheck.cs` implementing `IEvalCheck` with `Name = "followUpDirector"`. Reuses `DirectorExpected` (data-model.md). Passes when `EvalItem.Response` (the turn 2 answer text) contains `DirectorName` (case-insensitive).
- [X] T038 [P] [US1] Create `MovieTracker.Evals/FunnyFactNotEmptyCheck.cs` implementing `IEvalCheck` with `Name = "funnyFactNotEmpty"`. Deserializes `EvalItem.Context` to `ActualResponsePayload`; passes when `FunnyFact` is not null and not empty (`ExpectedOutput` is unused for this check).

### 3b. Run-title selection and generated-case assembly

- [X] T039 [US1] Rewrite `MovieTracker.Evals/RunTitleSelector.cs` for three titles — one per region in the fixed order US, IN, SE. For each region: pick a random page in `1..5` via `System.Random.Shared`, call `TmdbClient.GetPopularAsync`, pick one random entry. On a TMDb id collision across regions, resample within the same already-chosen page (never reroll the page; spec FR-006). On a popular-list failure (non-2xx, unparseable, or zero entries on the chosen page), throw a named exception that halts the run before any case is asked.
- [X] T040 [US1] Rewrite `MovieTracker.Evals/GeneratedCaseBuilder.cs`. For each of the eight templates in `eval-movie-templates.json`:
  - Pick a run-title slot `1..3` at random via `System.Random.Shared` (research.md Decision 13).
  - Resolve the per-check truth (data-model.md "Truth-resolution rules per `TemplateCheck`"), cache TMDb details / credits per `tmdbId` so each is fetched at most once per run.
  - For `trailer`: call `GetTrailerVideoKeysAsync`; if it returns null (no YouTube-Trailer videos), **produce no case** for `trailer` (qualification rule, not FR-016) and increment the `TrailerProducedCount` only for the produced case. Otherwise produce the case with `TrailerExpected { VideoKeys }`.
  - For every other template: produce a case. If the truth lookup returned no usable value (per data-model.md), set `NoTruthValue = true` and leave `ExpectedJson` null.
  - For `funnyFactNotEmpty`: `ExpectedJson` is null regardless (no truth needed).
  - For `followUpDirector`: populate `Turns = ["Tell me about <title>", "Who directed it?"]` and set `Question` to the first turn (used only for the EvalItem.Query slot).
  - Generated case id: `"<templateId>-<slot>"`.
  - Return `GeneratedCaseBatch(cases, trailerProducedCount)`.

### 3c. Composition root

- [X] T040a [US1] Create `MovieTracker.Evals/CheckDiscovery.cs`: one static method `IReadOnlyDictionary<string, IEvalCheck> DiscoverAll()` that uses reflection on the executing assembly (`typeof(IEvalCheck).Assembly.GetTypes()`) to find every non-abstract class implementing `IEvalCheck`, instantiates each one via `Activator.CreateInstance`, and returns a dictionary keyed by `check.Name`. This is how `Program.cs` obtains the nine `IEvalCheck` instances without a hand-maintained central list for the **grading** side (spec FR-022; rich-eval-prompt §3: "One check per file ... Adding a check = one new file plus its template"). Throws if two implementations report the same `Name`.

> **Scope of FR-022 (central-grader ban)**: spec FR-022 explicitly scopes its ban to the **grading side** — no registry, dispatcher, or if-chain that maps check names to grading logic — and explicitly allows the non-grading pieces (the `TemplateCheck` data-shape enum, the fixed presentation order, and the per-check truth-fetch branches inside `GeneratedCaseBuilder`). Adding a new check kind is therefore a short checklist of all the files that may need editing: one new `IEvalCheck` file (grading side, auto-discovered via T040a — no central grader edit); one new `*Expected.cs` record; one new template entry in `eval-movie-templates.json`; one new truth-fetch branch in `GeneratedCaseBuilder` (`T040` equivalent for the new check); one new entry in the `TemplateCheck` enum (`T007` equivalent); and one new entry in the `CheckOrder.All` fixed presentation order (`T008` equivalent). This checklist is **non-exhaustive** — it covers the expected edit surface; unusual check kinds (e.g. one that needs a brand-new TMDb endpoint or a new per-entry movie-list field) may require touching `TmdbClient.cs` or `MovieListEntry.cs` as well. Phase 6 does not cross-check this checklist (`T051` only verifies the current file inventory); this list is a forward-looking guide for the owner.
- [X] T041 [US1] Rewrite `MovieTracker.Evals/Program.cs` (composition root in file-scoped namespace `MovieTracker.Evals`, explicit class `Program` so it does not shadow `global::Program`). Flow in strict order:
  1. Construct `HarnessHost`. Call `ConfigChecker.FindMissingKey`; if non-null, print the named-missing-key line, set `Environment.ExitCode = 1`, return.
  2. Call `CheckDiscovery.DiscoverAll()` to obtain the `IReadOnlyDictionary<string, IEvalCheck>` keyed by check name. Any legacy enum-to-name switch MUST be removed from the composition root — the dictionary is the single source of `IEvalCheck` instances.
  3. Load the two plot anchors and the eight templates.
  4. Select the three run titles via `RunTitleSelector`. On precondition failure, print the named error, set `Environment.ExitCode = 1`, return (no results file written).
  5. Print the one-line popular-titles summary (`"Popular titles for this run: US slot 1 = …, IN slot 2 = …, SE slot 3 = …"`).
  6. Build generated cases via `GeneratedCaseBuilder`. On precondition failure propagated from TMDb/OMDb lookups, print the named error, set `Environment.ExitCode = 1`, return.
  7. Print `trailer produced <count> case(s)` from `GeneratedCaseBatch.TrailerProducedCount`.
  8. Open the API client via `HarnessHost.CreateClient()`.
  9. For each case (two plot anchors first, then generated in template-order):
     - For plot anchors: `expectedJson = JsonSerializer.Serialize(new ListIncludesExpected(anchor.Expected))` (NOT a pipe-joined string).
     - For generated cases: `expectedJson = case.ExpectedJson` (pre-serialized by the builder; may be null for `funnyFactNotEmpty`).
     - For a `GeneratedCase` with `NoTruthValue == true`, record `CaseResult(id, checkName, passed: false, reason: "no truth value")` directly and **skip** the chat call.
     - Otherwise execute one chat turn (single-turn cases) or two chat turns on the same `chatId` (multi-turn `followUpDirector`), parse `AgentReply`, build the `EvalItem` with `Query = question`, `Response = reply.Answer`, `Context = JsonSerializer.Serialize(new ActualResponsePayload(reply.FunnyFact, reply.MovieList))`, and `ExpectedOutput = expectedJson`. For an FR-015 chat-call failure (exception thrown from `StartChatAsync` or `AskAsync`), record `CaseResult(id, checkName, passed: false, reason: ex.Message)` and continue. Otherwise keep `(caseId, EvalItem, checkName)` aside for grouped grading in step 10.
  10. Group the surviving `(caseId, EvalItem)` pairs by `checkName`. For each group, call `checkRunner.EvaluateGroupAsync(checkName, groupPairs)` (T042; the injected `IReadOnlyDictionary<string, IEvalCheck>` resolves the check internally) and append its returned `CaseResult`s.
  11. Merge all `CaseResult`s (step 9 pre-marked + step 10 graded) back into the original case order (anchors first, generated by template-order, matching the order produced by T040). Compute `perCheck` by grouping on `CaseResult.Check` and ordering with `CheckOrder.All`. Build `RunResult(runAt, total, passed, perCheck, cases)`.
  12. Call `Reporter.PrintTable(results)` and `Reporter.PrintTotals(passed, results.Count)`.
  13. Call `ResultsStore.ReadPreviousAsync` → `CheckComparisonComputer.Compute` → `Reporter.PrintCheckComparison` (US2 flow).
  14. Call `ResultsStore.WriteAsync(current)`. Print the written-file line.
- [X] T042 [US1] Create `MovieTracker.Evals/CheckEvaluationRunner.cs` as a non-static sealed class with constructor-injected `IReadOnlyDictionary<string, IEvalCheck> discovery` (NET-36/NET-37 compliance). One instance async method `EvaluateGroupAsync(string checkName, IReadOnlyList<(string caseId, EvalItem item)> group) -> IReadOnlyList<CaseResult>`. Internally: look up `discovery[checkName]` to get the `IEvalCheck`; build `EvalCheck wrappedCheck = FunctionEvaluator.Create(checkName, check.Evaluate)`; build `LocalEvaluator localEvaluator = new LocalEvaluator(wrappedCheck)`; call `AgentEvaluationResults evalResults = await localEvaluator.EvaluateAsync(group.Select(p => p.item).ToList(), checkName, CancellationToken.None)`; walk `evalResults.Items` positionally alongside the input group list; for each pair, pull `evalResults.Items[i].Metrics[checkName]` as `Microsoft.Extensions.AI.Evaluation.BooleanMetric`; emit `CaseResult(group[i].caseId, checkName, boolean.Value ?? false, string.IsNullOrEmpty(boolean.Reason) ? (boolean.Value == true ? "ok" : "fail") : boolean.Reason)`. No captured variables (the boolean metric carries the reason directly).

### 3d. Reporter updates

- [X] T043 [US1] Update `MovieTracker.Evals/Reporter.cs`:
  - `PrintPopularTitles(IReadOnlyList<RunTitle>)` prints one line per FR-006: `"Popular titles for this run: US slot 1 = {t}, IN slot 2 = {t}, SE slot 3 = {t}"` (replace the previous multi-line block).
  - Replace the old `PrintTemplateCounts(int asksClarification, int plotSearch)` with `PrintTrailerProducedCount(int count)` printing `"trailer produced <count> case(s)"`.
  - Keep `PrintTable`, `PrintTotals`, `PrintCheckComparison` unchanged (they already walk `CheckOrder.All` via `CheckComparisonComputer` and tolerate the new nine-kind ordering).
- [X] T044 [US1] Update `MovieTracker.Evals/CheckComparisonComputer.cs` — no logic change, but confirm the comparison iterates `CheckOrder.All` and, for the overall-total row, sources `(prev.Passed, prev.Total)` from `RunResult` directly (not from `perCheck` sums) per research.md Decision 7. Previously verified for the prior rework; re-verify after the updated `CheckOrder` lands in T008.

**Checkpoint**: `dotnet run --project MovieTracker.Evals` (owner-executed) prints the one-line popular-titles summary, the trailer-produced count, the per-case table with ~10 rows, the totals line, and a per-check comparison block on the second run. Every grading decision for a non-short-circuited case comes from a `BooleanMetric` read off `AgentEvaluationResults.Items`.

---

## Phase 4: User Story 2 — See what changed since the last run (Priority: P2)

The per-check comparison path (`CheckComparisonComputer` + `Reporter.PrintCheckComparison` + the `ResultsStore.ReadPreviousAsync` wiring in `Program`) already exists from the prior rework. The only updates for rich-eval are the new check-kind list (T008) and the new `RunResult` schema (unchanged shape). This phase is small.

- [X] T045 [US2] Re-verify the comparison flow end-to-end by code walkthrough of `MovieTracker.Evals/Program.cs` + `MovieTracker.Evals/CheckComparisonComputer.cs` + `MovieTracker.Evals/Reporter.cs`: `Program` step 13 calls `ResultsStore.ReadPreviousAsync`, then `CheckComparisonComputer.Compute`, then `Reporter.PrintCheckComparison`, and the write happens afterwards (so the comparison compares against the file as it existed before this run). Confirm nothing still references the removed check-kind strings (`leadActor`, `releaseYear`, `trailerFormat`, `asksClarification`, `plotSearch`, `listIncludesSourceTitle`).

**Checkpoint**: owner-executed second run prints the per-check comparison with the nine rich-eval check kinds plus the `total:` row.

---

## Phase 5: User Story 3 — Grade against live third-party truth (Priority: P3)

- [X] T046 [US3] Code-walkthrough verification at review time of `MovieTracker.Evals/RunTitleSelector.cs`, `MovieTracker.Evals/GeneratedCaseBuilder.cs`, `MovieTracker.Evals/TmdbClient.cs`, `MovieTracker.Evals/OmdbClient.cs`, and `MovieTracker.Evals/Reporter.cs`: confirm the three regional titles come from the live TMDb popular endpoint; the builder resolves truth from live TMDb/OMDb for each template; `Reporter.PrintPopularTitles` prints the required one-line regional summary. No new code.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T047 Verify style across every `*.cs` file under `MovieTracker.Evals/` **on lines added or changed by this rich-eval rework** (playbook CS-10): no `var`, no `_`-prefixed private fields, no new code comments, every new `if` has braces, no duplicated per-check predicate logic (shared logic at the lowest common layer — each `IEvalCheck` is standalone and small; any shared helpers live in a single file). Required HTTP calls are awaited (fire-and-forget does NOT apply). Also verify FR-024 — review every C# code example in `specs/008-eval-harness/{spec.md,plan.md,research.md,data-model.md,contracts/README.md,quickstart.md,tasks.md}` for the same no-`var` / braces / no-inline-comments standard. Fix any violation in the offending file.
- [X] T048 Run `dotnet build .\movie-tracker-api.slnx` and verify success with zero new warnings.
- [X] T049 Verify FR-021 doc consistency by reviewing every file: `specs/008-eval-harness/prompt.md` and `specs/008-eval-harness/rework-prompt.md` are supersession pointers; `specs/008-eval-harness/spec.md`, `specs/008-eval-harness/plan.md`, `specs/008-eval-harness/research.md`, `specs/008-eval-harness/data-model.md`, `specs/008-eval-harness/contracts/eval-movie-cases.schema.json`, `specs/008-eval-harness/contracts/eval-movie-templates.schema.json`, `specs/008-eval-harness/contracts/last-run.schema.json`, `specs/008-eval-harness/contracts/README.md`, `specs/008-eval-harness/quickstart.md`, `specs/008-eval-harness/tasks.md`, `specs/008-eval-harness/checklists/requirements.md` describe only the rich-eval design. No earlier **design instruction** (six titles, two repetitions, `CheckEvaluator`, `EvalCheckRegistry`, `plotSearch`, `asksClarification`, pipe-separated `ExpectedOutput`) remains as a live rule in these files. Historical mentions explicitly labeled as "superseded" or as deletion targets in this `tasks.md` are allowed — the check is for live obsolete instructions, not for references in the file-rename / file-delete tasks themselves.
- [X] T050 Verify FR-017 scope by reviewing the full pending change set: changes are allowed **only** under `MovieTracker.Evals/` and `specs/008-eval-harness/`; nothing under `MovieTracker.Api/`, `infrastructure/`, `.github/`, `CLAUDE.md`, `README.md`, `.gitignore`, `movie-tracker-api.slnx`, or `specs/001..007/` is modified by the rich-eval rework.
- [X] T051 Verify no stale file remains and every new file exists under `MovieTracker.Evals/`: `MovieTracker.Evals/CheckEvaluator.cs`, `MovieTracker.Evals/EvalCheckRegistry.cs`, `MovieTracker.Evals/SearchMatch.cs` are gone; all nine `MovieTracker.Evals/{ListIncludes,Director,ActorFilmography,IdentifyByPlot,GenreDecade,Rating,Trailer,FollowUpDirector,FunnyFactNotEmpty}Check.cs` files exist; typed expected records exist (`MovieTracker.Evals/{ListIncludesExpected,DirectorExpected,ActorFilmographyExpected,IdentifyByPlotExpected,GenreDecadeExpected,RatingExpected,TrailerExpected}.cs`); `MovieTracker.Evals/{ActualResponsePayload,MovieListEntry,ActorFilmography,MovieGenre,IEvalCheck,CheckDiscovery,CheckEvaluationRunner}.cs` exist.
- [ ] T052 [owner] Perform the FR-015 bulk-reproduction procedure from `specs/008-eval-harness/quickstart.md` against the already-built `MovieTracker.Evals/` (owner-executed because it runs the harness with real TMDb/OMDb/Cosmos keys with an invalidated Azure OpenAI key): set `$env:AzureOpenAI__ApiKey = "invalid"`, re-run, observe every row past truth resolution is FAIL with an HTTP status, `MovieTracker.Evals/results/last-run.json` is still written; revert the env var.
- [ ] T053 [owner] Walk through `specs/008-eval-harness/quickstart.md` end to end with real keys against the already-built `MovieTracker.Evals/`: first run produces the one-line popular-titles summary, the `trailer produced <count> case(s)` line, the per-case table, and the totals; second run adds the per-check comparison block with all nine check kinds plus `total:`. The final committed `MovieTracker.Evals/results/last-run.json` MUST come from a normal real-keys run (not from T052's invalid-key run).

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: T001 and T002 are independent. **T003 MUST complete before any task that references a MAF type**, including T023 (IEvalCheck exposes `EvalItem` / `EvalCheckResult`), every `*Check.cs` file (T030–T038), the composition root (T041), and the evaluation runner (T042). If T003 reports a missing MAF member, work stops before Phase 2.
- **Phase 2a (Deletes)**: T004, T005, T006 land first. T004 blocks T024 (updated AgentReply shape) and T041 (new composition root replaces the captured-variable path). T005 blocks T014–T023 (the typed expected / interface files that replace the registry). T006 blocks T029 (TmdbClient rewrite that no longer uses SearchMatch).
- **Phase 2b–2f**: all `[P]` tasks are file-disjoint. Sequencing within 2b/2c/2d/2e/2f matters only as documented per task.
- **Phase 3 (US1)**: depends on Phase 2 complete. The nine `*Check.cs` files (T030–T038) are `[P]` with each other but all depend on T023 (IEvalCheck) and their respective expected-record types. T039 depends on the updated `TmdbClient` (T029). T040 depends on T029, T026, T027, T012, T013, and every `*Expected.cs` type. T040a depends on T023 (IEvalCheck) and on T030–T038 (the concrete implementations to reflect over). T041 depends on T030–T038, T040, T040a, T042, T043, T044. T042 depends on T030–T038 and `CaseResult`. T043 depends on T015 (MovieListEntry) and T009 (RunTitle slot 1..3). T044 is a verification-only re-walk of the prior rework's `CheckComparisonComputer` against the new `CheckOrder` list (T008).
- **Phase 4 (US2)**: depends on Phase 3 complete. T045 is verification-only and belongs to US2 even though T044 (in Phase 3) is US1 wiring.
- **Phase 5 (US3)**: depends on Phase 3.
- **Phase 6 (Polish)**: T047, T049, T051 can run `[P]`. T048 (build) runs after T047's fixes. T050 (scope review) runs after every other mutation. T052 and T053 are `[owner]` tasks executed after implementer hand-off.

### Within-story order

- Deletes of pre-rework files land before the replacements (T004/T005/T006 at the top of Phase 2).
- DTO / typed-record changes in Phase 2 land before any IEvalCheck implementation.
- `TmdbClient` rewrite (T029) lands before `RunTitleSelector` / `GeneratedCaseBuilder`.
- `IEvalCheck` instances (T030–T038) land before the composition root (T041) and the runner (T042).
- The composition root (T041) and the runner (T042) land together; T041 cannot complete without T042.

### Parallel opportunities

- All of Phase 2b (`[P]` enum/core rewrites) can run after Phase 2a.
- All of Phase 2c (`[P]` typed expected / payload records) can run after T005 lands.
- All of Phase 3a (`[P]` IEvalCheck implementations T030–T038) can run after Phase 2 completes.
- T047 / T049 / T051 are `[P]` in Polish.

---

## Parallel Example: Phase 3a (`IEvalCheck` files)

```text
After Phase 2 is done, launch all nine IEvalCheck files in parallel:
Task: "Create MovieTracker.Evals/ListIncludesCheck.cs"      (T030)
Task: "Create MovieTracker.Evals/DirectorCheck.cs"          (T031)
Task: "Create MovieTracker.Evals/ActorFilmographyCheck.cs"  (T032)
Task: "Create MovieTracker.Evals/IdentifyByPlotCheck.cs"    (T033)
Task: "Create MovieTracker.Evals/GenreDecadeCheck.cs"       (T034)
Task: "Create MovieTracker.Evals/RatingCheck.cs"            (T035)
Task: "Create MovieTracker.Evals/TrailerCheck.cs"           (T036)
Task: "Create MovieTracker.Evals/FollowUpDirectorCheck.cs"  (T037)
Task: "Create MovieTracker.Evals/FunnyFactNotEmptyCheck.cs" (T038)
```

---

## Implementation Strategy

### MVP (US1 only)

1. Complete Phase 1 (both JSON inputs reworked, MAF surface verified).
2. Complete Phase 2 (deletes first; then DTOs, typed records, IEvalCheck interface, updated AgentReply/AgentClient, updated MovieDetails/MovieCredits, updated TemplateLoader, rewritten TmdbClient).
3. Complete Phase 3 (nine `*Check.cs` files, RunTitleSelector, GeneratedCaseBuilder, Program, CheckEvaluationRunner, Reporter, CheckComparisonComputer).
4. Hand off to owner for the first real-keys run per quickstart.md.

### Incremental delivery

1. MVP ships US1 — one-line popular-titles summary + trailer-produced line + per-case table + totals + saved results file.
2. US2 is already functional via the prior rework; T044 (US1 re-walk of `CheckComparisonComputer`) lands during Phase 3, and T045 (US2 code walkthrough) is the only US2-labeled check-in.
3. US3 is verified by owner review of the live-data path.
4. Polish enforces style, build cleanliness, scope, and owner-executed quickstart runs.

### Non-strategy

- No separate test project (rich-eval-prompt §Rules).
- No LLM grader, no retries, no CLI options, no parallel runs, no random seed, no per-id flip report.
- No edits outside `MovieTracker.Evals/` and `specs/008-eval-harness/`.
- The implementer does **not** run the harness with real keys (rich-eval-prompt §Steps 6); live-run acceptance (T052, T053) is owner-executed.

---

## Notes

- Every `[P]` task is file-disjoint from the other `[P]` tasks in the same phase.
- Every task names its file path.
- Verification tasks appear across multiple phases: T003 verifies the MAF surface in Phase 1; T044/T045 re-verify the US2 flow; T046 code-walks the US3 live-data path; T047/T049/T050/T051 review style/docs/scope/file-inventory in Polish; T052 and T053 are owner-executed live-run acceptance checks.
- The two JSON input files (T001, T002) are owner-authored contracts; the loaders enforce only unique ids (spec FR-004a).
- FR-020 (T003) blocks every subsequent MAF-dependent task until the installed package's surface is verified.
