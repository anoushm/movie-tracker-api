---
description: "Task list for feature 012-eval-cleanup"
---

# Tasks: Eval Cleanup

**Input**: Design documents in `specs/012-eval-cleanup/` (prompt, spec, plan, research, data-model, contracts, quickstart).

**Prerequisites**: plan.md and spec.md (both approved by Codex).

**Tests**: NO new tests. The feature's verification path is a single `dotnet build movie-tracker-api.slnx` (zero new warnings) as a mid-slice integration checkpoint, a repeat at the end, and a single `dotnet run --project MovieTracker.Evals` (T051) producing the output shape in `quickstart.md`. T051 is the only live eval run of the slice, so the Nov 12 `results/last-run.json` is preserved on disk right up to T051 and T051 is the explicit backwards-compat check for the old-schema `last-run.json` (research Decision 1).

**Organization**: Tasks are grouped by user story (US1..US6 in spec.md). The code-style constraints from the owner prompt apply to every C# task: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces matching the folder.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: User story label (US1..US6) for traceability. Foundational and Polish tasks have no story label.

---

## Phase 1: Foundational - CheckName constants

**Purpose**: give every check a single-source-of-truth name constant that later stories can reference.

- [X] T001 [P] Add `public const string CheckName = "listIncludes";` and `public string Name => CheckName;` to `MovieTracker.Evals/ListIncludesCheck.cs` (replacing the existing `Name` property).
- [X] T002 [P] Add `public const string CheckName = "director";` and `Name => CheckName;` to `MovieTracker.Evals/DirectorCheck.cs`.
- [X] T003 [P] Add `public const string CheckName = "actorFilmography";` and `Name => CheckName;` to `MovieTracker.Evals/ActorFilmographyCheck.cs`.
- [X] T004 [P] Add `public const string CheckName = "identifyByPlot";` and `Name => CheckName;` to `MovieTracker.Evals/IdentifyByPlotCheck.cs`.
- [X] T005 [P] Add `public const string CheckName = "genreDecade";` and `Name => CheckName;` to `MovieTracker.Evals/GenreDecadeCheck.cs`.
- [X] T006 [P] Add `public const string CheckName = "rating";` and `Name => CheckName;` to `MovieTracker.Evals/RatingCheck.cs`.
- [X] T007 [P] Add `public const string CheckName = "trailer";` and `Name => CheckName;` to `MovieTracker.Evals/TrailerCheck.cs`.
- [X] T008 [P] Add `public const string CheckName = "followUpDirector";` and `Name => CheckName;` to `MovieTracker.Evals/FollowUpDirectorCheck.cs`.
- [X] T009 [P] Add `public const string CheckName = "funnyFactNotEmpty";` and `Name => CheckName;` to `MovieTracker.Evals/FunnyFactNotEmptyCheck.cs`.

**Checkpoint**: every `*Check` class exposes `CheckName`. No build gate here — the project may not compile cleanly until Phase 5 rewrites `Program.cs` to pick up the new types coming in Phases 2–4.

---

## Phase 2: User Story 4 - Single source of truth for check names (Priority: P2, foundational for US1 and US5)

**Goal**: delete the `TemplateCheck` / `CheckKind` enums and `CheckOrder`, make `CaseTemplate.Check` and `GeneratedCase.Check` strings, make `TemplateLoader.Load` validate against discovered check names, drop `PlotAnchorCase.Check`, and thread the ordered check-name list through `CheckComparisonComputer`. All `Program.cs` edits that would otherwise be interim in this phase are deferred to Phase 5; the rewrite there picks up the new types.

**Independent Verification (deferred to T051)**: when T051 runs, temporarily setting `eval-movie-templates.json`'s `director` entry's `check` to `directorXYZ` would surface a fast failure naming the unknown check and no agent call. Not executed as a separate run here — documented so the owner can repeat it manually later.

- [X] T010 [US4] Change `MovieTracker.Evals/CaseTemplate.cs` so the `Check` property is `string` instead of `TemplateCheck`. `CaseTemplate` remains a `sealed class` with public setters (same shape as today — `Id`, `Template`, `Turns` untouched); do not convert it to a record.
- [X] T011 [US4] Change `MovieTracker.Evals/GeneratedCase.cs` so `Check` is `string` instead of `TemplateCheck`.
- [X] T012 [US4] Update `MovieTracker.Evals/TemplateLoader.cs`: after `JsonSerializer.Deserialize`, build the discovered check-name set from `CheckDiscovery.DiscoverAll().Keys`, and throw `InvalidOperationException` naming any template whose `check` is not in the set. Must throw before any TMDb or agent call. Remove any `JsonStringEnumConverter` carried only for the old enum `check` field.
- [X] T013 [US4] Update `MovieTracker.Evals/GeneratedCaseBuilder.cs`: switch on `template.Check` using string cases matching the `*Check.CheckName` constants (e.g. `case DirectorCheck.CheckName:`). Default arm throws naming the unknown check. Keep each existing `BuildXxxAsync` body intact aside from the switch label change.
- [X] T014 [US4] Update `MovieTracker.Evals/PlotAnchorCase.cs`: remove the `Check` property. `PlotAnchorCase` remains a `sealed class` with public setters (same shape as today — `Id`, `Question`, `Expected` untouched); do not convert it to a record.
- [X] T015 [US4] Update `MovieTracker.Evals/eval-movie-cases.json`: remove the `"check": "listIncludes"` field from every entry. File stays at project root. The wrapper `PlotAnchorCaseFile.cs` holds a `List<PlotAnchorCase>` and has no `check` reference to touch. In `PlotAnchorCaseLoader.cs`, remove any `JsonStringEnumConverter` that was carried only for the old enum `check` field if present; duplicate-id validation is untouched.
- [X] T016 [US4] Update `MovieTracker.Evals/EvalItemBuilder.cs`: replace the hard-coded `"listIncludes"` string in `BuildAnchorAsync`'s `CaseResult` error path with `ListIncludesCheck.CheckName`.
- [X] T017 [US4] Update `MovieTracker.Evals/CheckComparisonComputer.cs`: `Compute` takes a third parameter `IReadOnlyList<string> checkOrder`. Walk that order first, then append checks present only in the previous `last-run.json` in their previous order. Drop the use of `CheckOrder.All`.
- [X] T018 [US4] Delete `MovieTracker.Evals/TemplateCheck.cs`.
- [X] T019 [US4] Delete `MovieTracker.Evals/CheckKind.cs`.
- [X] T020 [US4] Delete `MovieTracker.Evals/CheckOrder.cs`.

**Checkpoint**: check names are now string-driven; `TemplateLoader` fails fast on unknown checks; `CheckOrder`, `TemplateCheck`, `CheckKind` are gone. `Program.cs` is deliberately left referring to the deleted enum and the old signatures; Phase 5 rewrites it.

---

## Phase 3: User Story 2 - Only well-known titles from TMDb popular, no region (Priority: P1)

**Goal**: delete `Region.cs`; `TmdbClient.GetPopularAsync()` takes no arguments and calls `movie/popular?page=1`; `RunTitleSelector.SelectAsync` picks 2 distinct titles at random from that single page; `Reporter.PrintPopularTitles` prints the single-line preamble `Popular titles for this run: slot 1 = {title}, slot 2 = {title}`.

**Independent Verification (deferred to T051)**: T051's harness output shows exactly one preamble line of that form (no region text) and `MovieTracker.Evals/Region.cs` is absent on disk.

- [X] T021 [US2] Update `MovieTracker.Evals/TmdbClient.cs`: change `GetPopularAsync(Region region, int page)` to parameterless `GetPopularAsync()`. URL path becomes `movie/popular?api_key=...&page=1` (no `region` parameter). Error messages drop the `region=` part.
- [X] T022 [US2] Update `MovieTracker.Evals/RunTitle.cs`: remove the `Region Region` positional parameter. Positional record becomes `(int TmdbId, string Title, int Slot)`.
- [X] T023 [US2] Rewrite `MovieTracker.Evals/RunTitleSelector.cs`: call `tmdbClient.GetPopularAsync()` once; if `< 2` results, throw `InvalidOperationException("TMDb /movie/popular?page=1 returned fewer than 2 entries; need at least 2")`; pick 2 distinct titles at random from the list; return `RunTitle(id, title, slot)` with `slot` = 1 for the first pick, 2 for the second.
- [X] T024 [US2] Update `MovieTracker.Evals/Reporter.cs`: `PrintPopularTitles` keeps the single-line preamble form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` — only the region text and its trailing space are removed. Keep the method signature.
- [X] T025 [US2] Delete `MovieTracker.Evals/Region.cs`.

**Checkpoint**: popular titles come from a single unfiltered page and the harness no longer carries a `Region` concept.

---

## Phase 4: User Story 1 - Two cases per check with SKIPPED cleanly separated (Priority: P1) 🎯 Primary MVP

**Goal**: 18 cases per run; `SKIP` reported as a third outcome; `skipped` carried on `CaseResult` / `PerCheckCount` / `RunResult`; `TrailerProducedCount` removed; totals line + per-check comparison match the new shape.

**Independent Verification (deferred to T051)**: T051 shows 18 rows ending in `PASS`/`FAIL`/`SKIP`; totals line is `Total: {p} / {t} ({s} skipped)`; per-check comparison appends ` ({n} skipped)` only where `n > 0`.

- [X] T026 [US1] Update `MovieTracker.Evals/CaseResult.cs`: positional record keeps `(Id, Check, Passed, Reason)` and gains `public bool Skipped { get; init; }` defaulting to `false`.
- [X] T027 [US1] Update `MovieTracker.Evals/PerCheckCount.cs`: positional record is `(string Check, int Passed, int Total, int Skipped = 0)`.
- [X] T028 [US1] Update `MovieTracker.Evals/RunResult.cs`: positional record becomes `(DateTimeOffset RunAt, int Total, int Passed, IReadOnlyList<PerCheckCount> PerCheck, IReadOnlyList<CaseResult> Cases, int Skipped)` (Skipped at the end, no default).
- [X] T029 [US1] Update `MovieTracker.Evals/CheckComparison.cs`: add `int? CurrentSkipped` as the final positional parameter.
- [X] T030 [US1] Update `MovieTracker.Evals/GeneratedCaseBatch.cs`: drop `TrailerProducedCount`; record becomes `(IReadOnlyList<GeneratedCase> Cases)`.
- [X] T031 [US1] Update `MovieTracker.Evals/GeneratedCaseBuilder.cs`: `BuildAsync(templates, runTitles)` now iterates `templates` outer, `runTitles` inner, producing `templates.Count * runTitles.Count` cases with ids `{template.Id}-{runTitle.Slot}`. The trailer arm returns a `GeneratedCase` with `NoTruthValue = true` instead of returning `null` when `keys.Count == 0`; `cases.Add(produced)` is unconditional. `trailerProducedCount` and its return-value participation are removed.
- [X] T032 [US1] Update `MovieTracker.Evals/Reporter.cs`: `PrintTable` prints `SKIP` when `result.Skipped` is true, otherwise `PASS`/`FAIL` as today. `PrintTotals` signature becomes `PrintTotals(int passed, int total, int skipped)` and prints `Total: {passed} / {total} ({skipped} skipped)`. `PrintCheckComparison` appends ` ({n} skipped)` after the current side when `row.CurrentSkipped is int n && n > 0`. `PrintTrailerProducedCount` method deleted from the file.
- [X] T033 [US1] Update `MovieTracker.Evals/CheckComparisonComputer.cs`: populate `CurrentSkipped` from `current.PerCheck` for each per-check row (null when the check is not present in the current run). For the appended `"total"` summary row, set `CurrentSkipped` from `current.Skipped` so `Reporter.PrintCheckComparison` emits the ` ({n} skipped)` suffix on the total row too when skips exist.

**Checkpoint**: MVP types are in place. `Program.cs` is still unchanged — Phase 5 rewrites it to pick up the new shapes and emit the new output.

---

## Phase 5: User Story 5 - Program.cs is only wiring; EvalRunner does the work (Priority: P2, integration checkpoint)

**Goal**: extract the orchestration from `Program.Main` into a new `EvalRunner` class at the project root. `Program.Main` only creates the host, runs `ConfigChecker`, builds dependencies, calls `EvalRunner.RunAsync()`, and sets `Environment.ExitCode`. The result list is a single ordered list with pre-decided (skipped/errored) cases written directly at their position, and graded cases written back at their original index — no `preMarked`/`toGrade`/`graded` tuple collections, no `byId` dictionary, no `FindOriginalOrder`. This is also the phase where the project first builds again after Phases 2–4 (hence the gated build at T040).

**Independent Verification (deferred to T051)**: `Program.cs` reads as composition-only; T051 output matches US1/US2.

- [X] T034 [US5] Create `MovieTracker.Evals/EvalRunner.cs`. Constructor takes only what it uses: `TmdbClient`, `OmdbClient`, `EvalItemBuilder`, `CheckEvaluationRunner`, `IReadOnlyList<PlotAnchorCase> plotAnchors`, `IReadOnlyList<CaseTemplate> templates`. Do NOT inject `AgentClient` (`EvalItemBuilder` already holds it) or the `discovery` dictionary (`CheckEvaluationRunner` already holds it). Exposes `public async Task<int> RunAsync()` returning `0` on success and `1` on precondition failure.
- [X] T035 [US5] In `EvalRunner.RunAsync`: capture `DateTimeOffset runAt = DateTimeOffset.UtcNow;` BEFORE any agent call (preserving today's timing). Run `RunTitleSelector` (catch `InvalidOperationException`, print `Precondition failure (popular title selection): {ex.Message}. Nothing was asked.`, return `1`), print popular titles, run `GeneratedCaseBuilder.BuildAsync` (same catch pattern with "generated-case assembly"), then build a `List<CaseResult?> results` of length `plotAnchors.Count + batch.Cases.Count` initialised to `null`.
- [X] T036 [US5] In `EvalRunner.RunAsync`: walk the anchor list once. For each anchor at index `i`, call `evalItemBuilder.BuildAnchorAsync(anchor, serializedExpected)`. If it returns a `CaseResult preMark` (agent error), write that `CaseResult` to `results[i]` directly. If it returns an `item`, keep both `item` and its index in a local grouping dictionary keyed by `ListIncludesCheck.CheckName`. Walk the generated-case list once. For each case at list-index `plotAnchors.Count + j`, if `generated.NoTruthValue == true`, write `new CaseResult(generated.Id, generated.Check, false, "no truth value") { Skipped = true }` directly to `results[plotAnchors.Count + j]`. Otherwise call `BuildGeneratedAsync` and, like anchors, either write the error preMark to its slot or add the `item` + index to the same grouping dictionary under `generated.Check`.
- [X] T037 [US5] In `EvalRunner.RunAsync`: the grouping dictionary is `Dictionary<string, List<(int Index, string CaseId, EvalItem Item)>>`. For each entry, call `checkRunner.EvaluateGroupAsync(checkName, group.Select(g => (g.CaseId, g.Item)).ToList())`; the call returns `IReadOnlyList<CaseResult>` in input order. Write each returned `CaseResult` into `results[group[k].Index]`. The grouping dictionary disappears once writing is done. Structurally, no `preMarked`/`toGrade`/`graded` tuple collections, no `byId` dictionary, no `FindOriginalOrder` helper may remain in the file (local variable naming is otherwise free).
- [X] T038 [US5] In `EvalRunner.RunAsync`: after grading, the `results` list is fully populated. Build the final `IReadOnlyList<CaseResult> ordered = results.Select(r => r ?? throw new InvalidOperationException("EvalRunner: result slot was not populated")).ToList();`. Compute `passed` (count of `!r.Skipped && r.Passed`), `skipped` (count of `r.Skipped`), `total = ordered.Count - skipped`. Compute `checkOrder = [ListIncludesCheck.CheckName, ..templates.Select(t => t.Check)]`. Call a private `BuildPerCheck(ordered, checkOrder)` that iterates `checkOrder` first, then appends checks present in results but not in `checkOrder`, and computes per-check `Passed`, `Total`, and `Skipped` (skipped cases do NOT contribute to `Passed` or `Total`). Construct `RunResult(runAt, total, passed, perCheck, ordered, skipped)` using the captured `runAt`. Call `Reporter.PrintTable(ordered)`, `Reporter.PrintTotals(passed, total, skipped)`. Load previous via `ResultsStore.ReadPreviousAsync`. Call `CheckComparisonComputer.Compute(previous, current, checkOrder)` and `Reporter.PrintCheckComparison(...)`. `await ResultsStore.WriteAsync(current)`. `Console.WriteLine($"Wrote {Path.Combine("MovieTracker.Evals", "results", "last-run.json")}");` Return `0`.
- [X] T039 [US5] Rewrite `MovieTracker.Evals/Program.cs`: `Main` creates the `HarnessHost`, runs `ConfigChecker.FindMissingKey` (print and exit 1 on miss), constructs `TmdbClient`, `OmdbClient`, loads templates and anchors, creates `AgentClient` from `host.CreateClient()`, creates `EvalItemBuilder`, `CheckEvaluationRunner(discovery)`, then constructs `EvalRunner` passing only `TmdbClient`, `OmdbClient`, `EvalItemBuilder`, `CheckEvaluationRunner`, `plotAnchors`, `templates` (no `AgentClient`, no `discovery` — see T034). Awaits `EvalRunner.RunAsync()` and assigns the return value to `Environment.ExitCode`. Delete the local `BuildPerCheck`, `FindOriginalOrder`, the tuple lists, the `byId` dictionary, `TemplateCheckToString`, and the `Reporter.PrintTrailerProducedCount(...)` call.
- [X] T040 [US5] Integration-checkpoint build: `dotnet build movie-tracker-api.slnx` zero new warnings. The first build of the slice. No eval run here; T051 is the single live run.

**Checkpoint**: `Program.cs` is composition-only; `EvalRunner` owns the orchestration. The build succeeds, confirming Phases 1–5 are consistent end-to-end.

---

## Phase 6: User Story 3 - Harness organised into subfolders (Priority: P2)

**Goal**: move every file into its target folder (as listed in spec Story 3) and update namespaces. No body changes beyond the namespace line and the `using` lines the moves force.

**Independent Verification (deferred to T050)**: directory listing matches the Story 3 table; every moved file's namespace is `MovieTracker.Evals.<Folder>`; the final solution build in T050 confirms the moves compile.

- [X] T041 [P] [US3] Move the following files into `MovieTracker.Evals/Checks/` (create the folder if it does not already exist) and change their namespace to `MovieTracker.Evals.Checks`: `IEvalCheck.cs`, `CheckDiscovery.cs`, `CheckEvaluationRunner.cs`, `ActualResponseParser.cs`, `ActualResponsePayload.cs`, `DirectorNameMatcher.cs`, `MovieMatcher.cs`, `ActorFilmographyCheck.cs`, `ActorFilmographyExpected.cs`, `DirectorCheck.cs`, `DirectorExpected.cs`, `FollowUpDirectorCheck.cs`, `FunnyFactNotEmptyCheck.cs`, `GenreDecadeCheck.cs`, `GenreDecadeExpected.cs`, `IdentifyByPlotCheck.cs`, `IdentifyByPlotExpected.cs`, `ListIncludesCheck.cs`, `ListIncludesExpected.cs`, `RatingCheck.cs`, `RatingExpected.cs`, `TrailerCheck.cs`, `TrailerExpected.cs`.
- [X] T042 [P] [US3] Move the following files into `MovieTracker.Evals/Cases/` (create the folder if needed) and change their namespace to `MovieTracker.Evals.Cases`: `CaseTemplate.cs`, `CaseTemplateFile.cs`, `TemplateLoader.cs`, `GeneratedCase.cs`, `GeneratedCaseBatch.cs`, `GeneratedCaseBuilder.cs`, `PlotAnchorCase.cs`, `PlotAnchorCaseFile.cs`, `PlotAnchorCaseLoader.cs`, `RunTitle.cs`, `RunTitleSelector.cs`, `ReleaseYearExtractor.cs`.
- [X] T043 [P] [US3] Move the following files into `MovieTracker.Evals/Agent/` (create the folder if needed) and change their namespace to `MovieTracker.Evals.Agent`: `AgentClient.cs`, `AgentReply.cs`, `MovieListEntry.cs`, `EvalItemBuilder.cs`.
- [X] T044 [P] [US3] Move the following files into `MovieTracker.Evals/Tmdb/` (create the folder if needed) and change their namespace to `MovieTracker.Evals.Tmdb`: `TmdbClient.cs`, `OmdbClient.cs`, `MovieDetails.cs`, `MovieCredits.cs`, `MovieGenre.cs`, `MovieSummary.cs`, `ActorFilmography.cs`.
- [X] T045 [P] [US3] Move the following files into `MovieTracker.Evals/Reporting/` (create the folder if needed) and change their namespace to `MovieTracker.Evals.Reporting`: `Reporter.cs`, `ResultsStore.cs`, `RunResult.cs`, `CaseResult.cs`, `PerCheckCount.cs`, `CheckComparison.cs`, `CheckComparisonComputer.cs`.
- [X] T046 [US3] Add the `using` lines the moves force. Guidance (not exhaustive — add any additional usings the solution build demands): `Checks/CheckEvaluationRunner.cs` needs `using MovieTracker.Evals.Reporting;` (for `CaseResult`); `Cases/GeneratedCaseBuilder.cs` needs `using MovieTracker.Evals.Tmdb;` (for `TmdbClient`) and `using MovieTracker.Evals.Checks;` (for `*Check.CheckName` constants and `*Expected` records); `Agent/EvalItemBuilder.cs` needs `using MovieTracker.Evals.Cases;` (for `PlotAnchorCase`, `GeneratedCase`), `using MovieTracker.Evals.Checks;` (for `ListIncludesCheck`), and `using MovieTracker.Evals.Reporting;` (for `CaseResult`); `Reporting/Reporter.cs` needs `using MovieTracker.Evals.Cases;` (for `RunTitle`). In the two root-level files that stay in namespace `MovieTracker.Evals` — `Program.cs` and `EvalRunner.cs` — add `using MovieTracker.Evals.Agent; using MovieTracker.Evals.Cases; using MovieTracker.Evals.Checks; using MovieTracker.Evals.Reporting; using MovieTracker.Evals.Tmdb;`. Confirm `MovieTracker.Evals/eval-movie-cases.json`, `eval-movie-templates.json`, and `results/` remain at the project root (unmoved); `ProjectRoot.Path` resolution is unchanged.

**Checkpoint**: layout matches spec Story 3 exactly; no behavior change. Build is gated at T050.

---

## Phase 7: User Story 6 - Rename `MovieTools` to `MovieToolsRegistration` (Priority: P3)

**Goal**: cosmetic rename in `MovieTracker.Api/Tools/`. One-line `CLAUDE.md` update.

**Independent Verification (deferred to T050)**: T050's solution build covers the API; grep for `MovieTools` in the API finds no references outside the renamed file plus the `AddMovieTools` extension method call in `Program.cs`.

- [X] T047 [US6] Rename `MovieTracker.Api/Tools/MovieTools.cs` to `MovieTracker.Api/Tools/MovieToolsRegistration.cs`. In that file, rename the class `MovieTools` to `MovieToolsRegistration`. Both `typeof(MovieTools)` usages inside become `typeof(MovieToolsRegistration)`. The extension method keeps the name `AddMovieTools`.
- [X] T048 [US6] Update `CLAUDE.md`: in the `Tools/` line under `### Project Structure`, replace `MovieTools (DI registration)` with `MovieToolsRegistration (DI registration)`. No other `CLAUDE.md` changes.

**Checkpoint**: naming consistency restored in `Tools/`. Build confirmation in T050.

---

## Phase 8: Polish and End-to-End Verification

- [X] T049 Grep sweep (scoped to C# source only): no `.cs` file under `MovieTracker.Evals/` contains the identifiers `TemplateCheck`, `CheckOrder`, `CheckKind`, `Region`, or `TemplateCheckToString`. No `.cs` file under `MovieTracker.Evals/` contains a hard-coded `"listIncludes"` string literal outside `ListIncludesCheck` itself (the `CheckName` constant's initialiser). JSON data files (`eval-movie-templates.json`, `results/last-run.json`) legitimately contain `listIncludes` as a check value — JSON is out of scope for this sweep.
- [X] T050 Final solution build: `dotnet build movie-tracker-api.slnx`. Warning count equals that of `main` (zero new warnings). This is the only final build and covers all phases (incl. the subfolder moves and the API rename).
- [X] T051 Single live eval run: on a fresh shell with `az login` session, `dotnet run --project MovieTracker.Evals`. Capture: the single preamble line `Popular titles for this run: slot 1 = {title}, slot 2 = {title}`, the table (18 rows with PASS/FAIL/SKIP), the totals line (`Total: p / t (s skipped)`), the per-check comparison (with ` ({n} skipped)` suffix on current side when n>0), and the `Wrote MovieTracker.Evals\results\last-run.json` line. This is also the backwards-compat check for the existing Nov 12 `MovieTracker.Evals/results/last-run.json` (schema without `skipped` fields) — it MUST load without a `Warning: could not parse previous results file: ...` line (research Decision 1). The plan is a single scheduled run — a surprising score goes into the report in T053 as-is rather than being chased with extra runs.
- [X] T052 Open the freshly-written `MovieTracker.Evals/results/last-run.json`: top-level has `skipped: <int>`; every `perCheck[*]` has `skipped: <int>`; every `cases[*]` has `skipped: <bool>`. This file is the new baseline the owner will commit.
- [X] T053 Report: console table, totals line, per-check comparison, Codex rounds per stage (specify: 4, plan: 3, tasks: 3, implement: 1), and anything left open.

---

## Dependencies and Execution Order

- **Phase 1 (Foundational)**: T001–T009 add `CheckName` constants and can run in parallel. Must complete before Phase 2.
- **Phase 2 (US4)**: T010–T020. Must complete before Phase 4 (needs string `Check`) and Phase 5 (needs string-based orchestration). Phase 2 intentionally leaves `Program.cs` broken; the project will not compile until Phase 5 completes.
- **Phase 3 (US2)**: T021–T025. Independent of US4 content-wise; sequenced before Phase 4 so `RunTitle` and `TmdbClient.GetPopularAsync()` are their final shape when Phase 4 touches `GeneratedCaseBuilder`.
- **Phase 4 (US1)**: T026–T033. Needs Phase 2 (string-based orchestration) and Phase 3 (new `RunTitle` shape).
- **Phase 5 (US5)**: T034–T040. First build of the slice; needs all preceding phases. T040 is the integration-checkpoint build.
- **Phase 6 (US3)**: T041–T046. After Phase 5 so `Program.cs` / `EvalRunner.cs` are in their final shape when their new usings are added.
- **Phase 7 (US6)**: T047–T048. Independent of the evals work; could run at any point in the pipeline.
- **Phase 8 (Polish)**: T049–T053. After all previous phases.

### Parallel opportunities

- T001–T009 in Phase 1 (nine `CheckName` additions in nine separate files).
- T041–T045 in Phase 6 (five folder moves, each a disjoint set of files).
- Within Phase 2, T010–T016 touch mostly separate files and could run in parallel until T017 touches `CheckComparisonComputer.cs`.

---

## Notes

- Only two solution builds are required for the slice: T040 as the integration checkpoint and T050 as the final gate. T051 is the single live eval run — deliberately only once, so the Nov 12 `last-run.json` is preserved on disk right up to it and T051 serves as the explicit backwards-compat check for the old-schema `last-run.json` (research Decision 1).
- No new tests, no CI changes, no `.gitignore` edits, no changes to specs 001–011, no agent/prompt/tool/endpoint behavior changes outside the rename.
- Code style (owner prompt): explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces matching the folder.
- The final committed `results/last-run.json` is the new baseline for future Option A vs Option B comparisons. If the new total looks much worse than the Nov 12 baseline, the swing can be explained by this slice stopping counting skips as fails, so the apples-to-apples comparison is harder than it looks — inspect the table, do not re-run.
