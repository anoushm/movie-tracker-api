# Feature Specification: Eval Cleanup

**Feature Branch**: `012-eval-cleanup`

**Created**: 2026-11-17

**Status**: Draft

**Input**: Owner prompt at `specs/012-eval-cleanup/prompt.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two cases per check with SKIPPED cleanly separated (Priority: P1)

A maintainer runs `dotnet run --project MovieTracker.Evals`. The harness generates two cases for every template (one per run title), grades them, and prints a table where every row's `result` column shows `PASS`, `FAIL`, or `SKIP` (followed by the usual `reason` column). A case that has no truth value (for example, a rating template whose title has no IMDb rating, or a trailer template whose title has no TMDb trailer) shows as `SKIP`, is counted separately, and never inflates the failure count. The totals line reads `Total: {passed} / {total} ({skipped} skipped)` and the per-check comparison keeps its `p/t -> p/t` shape with ` ({skipped} skipped)` appended to the current side when `skipped > 0`.

**Why this priority**: this is the whole point of the slice. Option A vs Option B comparisons are meaningless when a single answer swings a check between 0% and 100% and when harness data gaps are reported as agent failures. Both problems are fixed together here.

**Independent Test**: on a working `az login` session, run `dotnet run --project MovieTracker.Evals`. The console shows 18 rows (2 anchors + 8 templates x 2), every row's `result` column shows `PASS`, `FAIL`, or `SKIP`, the totals line matches the new shape, and the per-check comparison appends ` ({n} skipped)` only on checks where skipped cases exist in the current run.

**Acceptance Scenarios**:

1. **Given** TMDb popular page 1 returns at least two titles, **When** the harness runs, **Then** every one of the 8 templates produces 2 cases (ids `{templateId}-1` and `{templateId}-2`, using popular title slot 1 and slot 2 respectively), and the 2 plot anchors from `eval-movie-cases.json` are added as-is for a run total of 18 cases.
2. **Given** a template case has no truth value (for example `rating-2` when OMDb has no rating for the slot-2 title, or `trailer-1` when TMDb has no trailer for the slot-1 title), **When** the harness grades the run, **Then** that case's result column is `SKIP`, the case is counted in `skipped`, and it is NOT counted in `passed` or in `total`.
3. **Given** an agent call returns an HTTP error or throws, **When** the harness grades the run, **Then** that case's result column is `FAIL` (never `SKIP`), and the case is counted in `total` and not in `skipped`.
4. **Given** 3 passed, 1 failed, 2 skipped cases in a run, **When** the totals line is printed, **Then** it reads exactly `Total: 3 / 4 (2 skipped)`.
5. **Given** a check had previous counts `0/1` and current counts `1/1` with 1 skipped case on the current run, **When** the comparison is printed, **Then** that line reads `{check} 0/1 -> 1/1 (1 skipped)`.
6. **Given** a check has `skipped = 0` on the current run, **When** the comparison is printed, **Then** no ` ({n} skipped)` suffix appears on that line.
7. **Given** the code compiles after this slice, **When** `CaseResult`, `PerCheckCount`, and `RunResult` are inspected, **Then** `CaseResult` has a `bool Skipped`, `PerCheckCount` has an `int Skipped`, and `RunResult` has an `int Skipped`.
8. **Given** a run completes, **When** `results/last-run.json` is written, **Then** the file carries the new `skipped` field on `RunResult` and on every `PerCheckCount`.
9. **Given** a previous `results/last-run.json` written before this slice (no `skipped` fields on `RunResult` and no `skipped` fields on any `PerCheckCount`), **When** the harness reads it, **Then** it loads successfully with `skipped = 0` on `RunResult` and `skipped = 0` on every `PerCheckCount`, and the comparison still renders.
10. **Given** the harness runs to completion, **When** the pre-run console messages are printed, **Then** there is no "Produced N trailer cases" line (the removed `TrailerProducedCount` is gone), and no file under `MovieTracker.Evals/` references `TrailerProducedCount` or `PrintTrailerProducedCount`.

---

### User Story 2 - Only well-known titles from TMDb popular, no region (Priority: P1)

A maintainer running the eval sees the two popular titles the harness chose printed on a single preamble line of the form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}`. Both come from TMDb's unfiltered `movie/popular?page=1` (no `region` query parameter), which is the globally-ranked popular list. No `Region` enum exists in the codebase, and the precondition failure message still appears if the TMDb page has fewer than 2 entries.

**Why this priority**: the eval is only useful when the underlying titles are ones the agent can reasonably know. Regional filtering was adding noise and the harness held onto a `Region` concept that no longer serves it.

**Independent Test**: run the harness and inspect the console line preceding the table. The output contains exactly one line of the form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` (no `region` text), both titles correspond to entries on `https://api.themoviedb.org/3/movie/popular?page=1`, and `MovieTracker.Evals/Region.cs` no longer exists on disk.

**Acceptance Scenarios**:

1. **Given** the harness starts, **When** `TmdbClient.GetPopularAsync()` is called, **Then** the outgoing URL path is `movie/popular?page=1` with no `region` query parameter present.
2. **Given** TMDb returns 20 titles on page 1, **When** `RunTitleSelector` runs, **Then** it picks exactly 2 distinct titles at random from that page.
3. **Given** TMDb returns fewer than 2 titles, **When** `RunTitleSelector` runs, **Then** it throws with the existing precondition message style, and `Program` surfaces it as `Precondition failure (popular title selection): ...`.
4. **Given** the chosen titles are printed, **When** `Reporter.PrintPopularTitles` runs, **Then** a single line is printed of the form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` with no region text.
5. **Given** the slice is complete, **When** the project is listed, **Then** there is no `Region.cs` file, no `Region` property on `RunTitle`, and no file in the project contains the identifier `Region` as a type name.

---

### User Story 3 - Harness organised into subfolders without behavior change (Priority: P2)

A reader opening `MovieTracker.Evals/` sees the files grouped by responsibility (`Checks/`, `Cases/`, `Agent/`, `Tmdb/`, `Reporting/`) with only orchestration, host plumbing, and the eval data files at the root. Every moved file compiles under the folder-matching namespace. The only intentional edits on the moved files are the namespace line and the `using` lines the move forces. No behavior changes.

The folder layout is:

| Folder | Files |
|---|---|
| (root) | `Program`, `HarnessHost`, `EvalRunner` (new), `ConfigChecker`, `ProjectRoot`, `eval-movie-cases.json`, `eval-movie-templates.json`, `results/` |
| `Checks/` | `IEvalCheck`, `CheckDiscovery`, `CheckEvaluationRunner`, all 9 `*Check`, all 7 `*Expected`, `DirectorNameMatcher`, `MovieMatcher`, `ActualResponseParser`, `ActualResponsePayload` |
| `Cases/` | `CaseTemplate`, `CaseTemplateFile`, `TemplateLoader`, `GeneratedCase`, `GeneratedCaseBatch`, `GeneratedCaseBuilder`, `PlotAnchorCase`, `PlotAnchorCaseFile`, `PlotAnchorCaseLoader`, `RunTitle`, `RunTitleSelector`, `ReleaseYearExtractor` |
| `Agent/` | `AgentClient`, `AgentReply`, `MovieListEntry`, `EvalItemBuilder` |
| `Tmdb/` | `TmdbClient`, `OmdbClient`, `MovieDetails`, `MovieCredits`, `MovieGenre`, `MovieSummary`, `ActorFilmography` |
| `Reporting/` | `Reporter`, `ResultsStore`, `RunResult`, `CaseResult`, `PerCheckCount`, `CheckComparison`, `CheckComparisonComputer` |

The JSON files and `results/` stay at the root, so `ProjectRoot` paths do not change. "Namespace-only edit" applies to the move itself; where another authorized item in this spec (Stories 1, 2, 4, 5) changes the body of a moved file, that change is allowed on top of the move.

**Why this priority**: the layout change makes the next slices (Option A/B comparison, LLM grader) readable. On its own it has no user value, so it is P2 behind the output-shape change but still in this slice.

**Independent Test**: after the slice lands, `dotnet build movie-tracker-api.slnx` succeeds with no new warnings, and the folder tree in `MovieTracker.Evals/` matches the table above exactly. Opening a moved file shows a namespace of the form `MovieTracker.Evals.<Folder>` matching its containing folder.

**Acceptance Scenarios**:

1. **Given** the file moves land, **When** the project directory is listed, **Then** each file named in the table above lives in the folder listed next to it; no file listed under `Checks/`, `Cases/`, `Agent/`, `Tmdb/`, or `Reporting/` remains at the project root; and the only files remaining at the project root are those listed in the `(root)` row plus the `.csproj`.
2. **Given** the file moves land, **When** the solution is built, **Then** every moved file's namespace is `MovieTracker.Evals.<Folder>` matching its folder, and the only intentional edits on each moved file beyond the body changes required by other stories are the namespace line and the `using` lines the move forces.
3. **Given** `ProjectRoot` resolves paths, **When** it reads `eval-movie-cases.json`, `eval-movie-templates.json`, and files under `results/`, **Then** those paths are unchanged because the data files and `results/` stay at the project root.
4. **Given** `Program.cs` is at the root, **When** the harness runs, **Then** orchestration is delegated to a root-level `EvalRunner` class (see Story 5) and the harness output matches the shape described in Stories 1 and 2 (table, `Total: ...` line, per-check comparison with ` (n skipped)` where applicable, and the single-line `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` preamble).

---

### User Story 4 - Single source of truth for check names (Priority: P2)

A developer adding a new eval check writes a single `public const string CheckName = "..."` on the check class. `GeneratedCaseBuilder` switches on those constants instead of enum arms, `EvalItemBuilder`'s current `"listIncludes"` literal becomes `ListIncludesCheck.CheckName`, and plot anchors are evaluated under `ListIncludesCheck.CheckName`. `CaseTemplate.Check` and `GeneratedCase.Check` carry the `check` value from `eval-movie-templates.json` as a string. The `TemplateCheck` enum, the `CheckKind` enum, the `CheckOrder` list, and `Program.TemplateCheckToString` are all gone. `TemplateLoader` validates every template's `check` string against the set of discovered `IEvalCheck.Name` values and fails fast if a template names an unknown check.

**Why this priority**: it removes a class of silent breakage (renaming a check leaves a stale enum arm) and makes the ordering rule explicit: `listIncludes` first, then templates in the order they appear in `eval-movie-templates.json`. P2 because it is enabling cleanup, not user-visible output.

**Independent Test**: temporarily change `director` to `directorXYZ` in `eval-movie-templates.json` and run the harness; the harness fails fast before asking any agent, reporting that `directorXYZ` is not a discovered check name. Revert the change. Separately, inspecting the compiled project shows no `TemplateCheck`, `CheckOrder`, `CheckKind`, or `TemplateCheckToString` identifiers remain.

**Acceptance Scenarios**:

1. **Given** every `*Check` class defines `public const string CheckName = "..."` and `Name => CheckName`, **When** the code compiles, **Then** every hard-coded `"listIncludes"` string previously in the harness (for example in `EvalItemBuilder`) has been replaced by `ListIncludesCheck.CheckName`, and `GeneratedCaseBuilder` switches on the `*Check.CheckName` constants rather than enum arms.
2. **Given** the current check names are `listIncludes`, `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, and `funnyFactNotEmpty`, **When** the slice is complete, **Then** every corresponding `*Check.CheckName` matches that string exactly and no name has changed.
3. **Given** `CaseTemplate.Check` and `GeneratedCase.Check` are `string`, **When** `GeneratedCaseBuilder` builds cases, **Then** it switches on the `*Check.CheckName` constants.
4. **Given** `eval-movie-templates.json` contains an unknown `check` value, **When** `TemplateLoader.Load` runs, **Then** it throws before any agent call with a clear message that names the unknown check.
5. **Given** plot anchors always use list-includes, **When** `eval-movie-cases.json` is read, **Then** the file has no `"check"` field on anchors and `PlotAnchorCase` has no `Check` property; the harness uses `ListIncludesCheck.CheckName` directly.
6. **Given** the check order is `ListIncludesCheck.CheckName` first then the order of `eval-movie-templates.json`, **When** `BuildPerCheck` and `CheckComparisonComputer` run, **Then** both receive that order as a parameter; checks that appear only in the previous `last-run.json` come after, in their previous order.
7. **Given** the slice is complete, **When** the project is listed, **Then** files named `TemplateCheck.cs`, `CheckOrder.cs`, and `CheckKind.cs` do not exist, and `Program` contains no method named `TemplateCheckToString`.

---

### User Story 5 - Program.cs is only wiring; EvalRunner does the work (Priority: P2)

`Program.Main` creates the host, calls `ConfigChecker`, builds the dependencies, calls `EvalRunner.RunAsync()`, and sets the exit code. Everything else moves into a new `EvalRunner` class at the project root. The orchestration keeps one ordered list of cases: anchors first, then generated cases. Any case that is already decided (skipped or errored up front) gets its result written straight into the list; remaining cases are grouped by check for grading and written back into the same slot. The `preMarked` / `toGrade` / `graded` tuples, the `byId` dictionary, and `FindOriginalOrder` are gone.

**Why this priority**: without this, the output changes in Story 1 and the file moves in Story 3 would live next to a rats' nest of bookkeeping. Rewriting that bookkeeping while keeping the output stable is the point of this item.

**Independent Test**: inspecting `Program.cs` shows only host creation, config check, dependency construction, `EvalRunner.RunAsync()`, and exit-code assignment, with no case bookkeeping. Running the harness still produces the table, totals, and per-check comparison described in Stories 1 and 2.

**Acceptance Scenarios**:

1. **Given** `Program.Main` runs, **When** it reaches orchestration, **Then** it calls `EvalRunner.RunAsync()` and does nothing else beyond host creation, config check, dependency construction, and exit-code assignment.
2. **Given** a case is skipped or errored up front, **When** `EvalRunner` builds the ordered result list, **Then** that case's result is written at its own position without being buffered in a separate list, grouped, or re-sorted.
3. **Given** a case needs grading, **When** `EvalRunner` runs the check group, **Then** the group's results are written back at each case's original position without a dictionary lookup by id.
4. **Given** the slice is complete, **When** the project is read, **Then** no `preMarked`, `toGrade`, or `graded` tuple collection exists, no `byId` dictionary exists for assembling the final ordered results, and no `FindOriginalOrder` method exists.

---

### User Story 6 - Rename `MovieTools` to `MovieToolsRegistration` in the API (Priority: P3)

A reader of `MovieTracker.Api/Tools/` sees registration-only classes named `*Registration` (matching the existing `ChatSessionRegistration` and `TelemetryRegistration`). The file previously named `MovieTools.cs` with class `MovieTools` is now `MovieToolsRegistration.cs` with class `MovieToolsRegistration`. The extension method keeps the name `AddMovieTools`. The two `typeof(MovieTools)` usages (both inside the renamed file) are updated to `typeof(MovieToolsRegistration)`. `CLAUDE.md`'s `Tools/` line is updated to reflect the rename.

**Why this priority**: pure consistency change, no runtime effect; parked at P3 so it does not compete with the eval work.

**Independent Test**: after the rename, `dotnet build movie-tracker-api.slnx` succeeds with no new warnings. The API's behavior at every endpoint is unchanged.

**Acceptance Scenarios**:

1. **Given** the rename lands, **When** the project is read, **Then** `MovieTracker.Api/Tools/MovieToolsRegistration.cs` contains the class `MovieToolsRegistration`, no file named `MovieTools.cs` remains in `Tools/`, and the extension method is still named `AddMovieTools`.
2. **Given** the rename lands, **When** the project is searched, **Then** the two `typeof(MovieTools)` usages that existed before the rename are now `typeof(MovieToolsRegistration)` and no `MovieTools` identifier remains in the API code.
3. **Given** DI registration runs at startup, **When** the agent tools are resolved, **Then** the set of registered movie tools is identical to before the rename.
4. **Given** `CLAUDE.md` describes the `Tools/` folder, **When** a reader reads it, **Then** the previous `MovieTools (DI registration)` entry now reads `MovieToolsRegistration` (with the same parenthetical).

---

### Edge Cases

- TMDb popular page 1 has fewer than 2 entries: `RunTitleSelector` throws; `Program` prints `Precondition failure (popular title selection): ...`. Same shape as today.
- A template case cannot find an expected value for its slot (OMDb has no rating for slot-2 title; TMDb has no trailer for slot-1 title): the case is reported `SKIP` in the table and in the `skipped` counter. The case id is still `{templateId}-{k}`.
- An agent call throws or returns an HTTP error: the case is `FAIL`, never `SKIP`. The run continues for remaining cases.
- A previous `results/last-run.json` written before this slice has no `skipped` fields on `RunResult` or on any `PerCheckCount`: the harness loads it with `skipped = 0` on `RunResult` and `skipped = 0` on every `PerCheckCount`.
- `eval-movie-templates.json` names a check that does not exist in the `IEvalCheck` discovery set: `TemplateLoader.Load` throws before any agent call, naming the unknown check.
- A check appears only in the previous run (e.g. retired between slices): the comparison includes it after the current-order checks, in its previous order.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every template in `eval-movie-templates.json` MUST produce exactly 2 generated cases per harness run, one per selected popular title. Case `k` MUST use popular-title slot `k` (k = 1, 2). The case id MUST be `{templateId}-{k}`.
- **FR-002**: The plot anchors in `eval-movie-cases.json` MUST be used as-is (not multiplied by run titles). The total case count per run MUST be `2 x (number of templates) + (number of plot anchors)`, which is 18 with the current data.
- **FR-003**: `CaseResult` MUST gain a `bool Skipped` property. `PerCheckCount` MUST gain an `int Skipped`. `RunResult` MUST gain an `int Skipped`.
- **FR-004**: A case with no truth value MUST be reported as `SKIP` (not `FAIL`). This includes the rating case when OMDb returns no rating AND the trailer case when TMDb returns no trailer. The trailer case MUST NOT be silently dropped: `TrailerProducedCount`, `GeneratedCaseBatch.TrailerProducedCount`, and `Reporter.PrintTrailerProducedCount` MUST be removed, and no "Produced N trailer cases" line is printed.
- **FR-005**: The table result column MUST print `PASS`, `FAIL`, or `SKIP`. The totals line MUST print `Total: {passed} / {total} ({skipped} skipped)`. `passed` and `total` MUST count only cases that were NOT skipped.
- **FR-006**: The per-check comparison MUST keep the current `p/t -> p/t` format and MUST append ` ({skipped} skipped)` after the current side when `skipped > 0` for that check in the current run.
- **FR-007**: An agent HTTP error or thrown exception MUST produce a `FAIL` result, never `SKIP`.
- **FR-008**: `results/last-run.json` MUST include the new `skipped` fields on `RunResult` and on each `PerCheckCount`. The reader MUST load older files without those fields with `skipped = 0` on `RunResult` and `skipped = 0` on every `PerCheckCount`.
- **FR-009**: `TmdbClient.GetPopularAsync()` MUST request `movie/popular?page=1` with no `region` query parameter. `RunTitleSelector.SelectAsync` MUST pick 2 distinct titles at random from that page and MUST throw with the existing precondition-message style if fewer than 2 entries are available.
- **FR-010**: `MovieTracker.Evals/Region.cs` MUST be deleted. `RunTitle` MUST NOT carry a `Region`. `Reporter.PrintPopularTitles` MUST print a single line of the form `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` (the one-line preamble is kept; only the region text and its trailing space are removed).
- **FR-011**: The files MUST be moved into the folders in the Story 3 table (root, `Checks/`, `Cases/`, `Agent/`, `Tmdb/`, `Reporting/`). Each moved file's namespace MUST be `MovieTracker.Evals.<Folder>` matching its new folder. The only intentional edits on the move itself are the namespace line and the `using` lines the move forces; body changes required by the other items in this spec are allowed on top. `eval-movie-cases.json`, `eval-movie-templates.json`, and the `results/` directory MUST stay at the project root and `ProjectRoot` paths MUST NOT change.
- **FR-012**: Every `*Check` class MUST define `public const string CheckName = "<name>";` and `Name => CheckName`. Each check's `CheckName` string MUST equal the name that check uses today: `listIncludes`, `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`.
- **FR-013**: `TemplateCheck.cs`, `CheckOrder.cs`, `CheckKind.cs`, and `Program.TemplateCheckToString` MUST be deleted. `CaseTemplate.Check` and `GeneratedCase.Check` MUST become `string`. `GeneratedCaseBuilder` MUST switch on the `*Check.CheckName` constants.
- **FR-014**: `TemplateLoader.Load` MUST validate every template's `check` string against the discovered `IEvalCheck.Name` set and MUST fail fast when a template names a check that is not discovered. The error MUST name the unknown check.
- **FR-015**: `eval-movie-cases.json` MUST no longer carry a `check` field on plot anchors. `PlotAnchorCase` MUST drop its `Check` property. Plot anchors MUST be evaluated under `ListIncludesCheck.CheckName`.
- **FR-016**: Hard-coded `"listIncludes"` string literals elsewhere in the harness (for example in `EvalItemBuilder`) MUST be replaced by `ListIncludesCheck.CheckName`.
- **FR-017**: The ordered list of check names used by `BuildPerCheck` and `CheckComparisonComputer` MUST be computed as: `ListIncludesCheck.CheckName` first, then the order of `eval-movie-templates.json`. Both functions MUST take this order as a parameter. Checks present only in the previous `last-run.json` MUST appear after that list, in their previous order.
- **FR-018**: A new `EvalRunner` class at the `MovieTracker.Evals` project root MUST hold the orchestration that `Program.Main` runs today (title selection, case build, ask, grade, report, save). `Program.Main` MUST only create the host, run `ConfigChecker`, build the dependencies, call `EvalRunner.RunAsync()`, and set `Environment.ExitCode`.
- **FR-019**: The new orchestration MUST build one ordered list of cases (anchors, then generated). Each skipped-up-front or errored-up-front case MUST get its result written at its own position. Remaining cases MUST be graded per check group and written back at each case's position. The `preMarked` / `toGrade` / `graded` tuples, the `byId` dictionary, and `FindOriginalOrder` MUST be removed.
- **FR-020**: Apart from items 1-7 of the owner prompt and the FRs above, console output, agent prompts, tool behavior, API endpoints, chat flow, Cosmos, telemetry, and `results/last-run.json` semantics MUST NOT change. The order of cases in the table, the per-case diagnostic text, and the write path MUST stay the same.
- **FR-021**: `MovieTracker.Api/Tools/MovieTools.cs` MUST be renamed to `MovieToolsRegistration.cs` and the class to `MovieToolsRegistration`. The `AddMovieTools` extension method name MUST NOT change. Both `typeof(MovieTools)` usages MUST be updated to `typeof(MovieToolsRegistration)`. The `MovieTools (DI registration)` entry in `CLAUDE.md` MUST be updated to `MovieToolsRegistration (DI registration)`; no other `CLAUDE.md` content changes.
- **FR-022**: `MovieTracker.Api` behavior (endpoints, prompts, tools, chat flow, Cosmos, telemetry) MUST NOT change in this slice except as required by FR-021.
- **FR-023**: `dotnet build movie-tracker-api.slnx` MUST succeed with no new warnings compared with `main`.
- **FR-024**: Changed code MUST conform to the owner prompt's code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces matching the folder.

### Out of Scope (binding)

- No new checks, no changes to existing check logic, no LLM grader.
- No changes to agent prompts, tool behavior, API endpoints, chat flow, Cosmos, or telemetry (except the rename in FR-021).
- No CI changes, no workflow file changes, no Option B work.
- No changes to question templates or anchor questions.
- No edits to `.gitignore`, to specs `001` through `011`, or to docs outside of the one-line `CLAUDE.md` edit in FR-021.

### Key Entities

- **Check name constant**: a `public const string CheckName` on each `*Check` class, surfaced via `Name => CheckName`. Single source of truth for check names.
- **Check-order parameter**: the ordered list `[ListIncludesCheck.CheckName, ...templates-in-file-order]` passed to `BuildPerCheck` and `CheckComparisonComputer`. Replaces the deleted `CheckOrder` static class.
- **CaseResult / PerCheckCount / RunResult**: result records that gain a `Skipped` field (boolean on `CaseResult`, integer on the counts). Older `last-run.json` files load with `skipped = 0`.
- **GeneratedCase**: holds `{Id, Check, ...}` where `Check` becomes `string` (was `TemplateCheck`). The id follows the `{templateId}-{k}` pattern.
- **RunTitle**: the per-slot popular title used to produce case `k`. Loses any region information in this slice.
- **EvalRunner**: a new class at the `MovieTracker.Evals` project root that owns the orchestration today found in `Program.Main`. Takes dependencies by constructor and exposes `RunAsync()`.
- **MovieToolsRegistration**: the renamed DI-registration class in `MovieTracker.Api/Tools/`. Exposes the existing `AddMovieTools` extension method unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A harness run produces exactly `2 x (templates in eval-movie-templates.json) + (anchors in eval-movie-cases.json)` cases per run (18 with current data), with case ids `{templateId}-1` and `{templateId}-2` for every template.
- **SC-002**: Every case row's `result` column in the console table is exactly one of `PASS`, `FAIL`, `SKIP`. The totals line matches the shape `Total: {passed} / {total} ({skipped} skipped)`, and `passed + fails = total` (skipped excluded from both sides).
- **SC-003**: The per-check comparison lines match the shape `{name} {p}/{t} -> {p}/{t}` and MUST append ` ({n} skipped)` when `n > 0` for that check in the current run; the suffix MUST be absent when `n = 0`.
- **SC-004**: A harness run started on an `az login` session completes end to end and writes a `results/last-run.json` that contains `skipped` on `RunResult` and on every `PerCheckCount`. Rerunning the harness with that file as the previous baseline loads without error.
- **SC-005**: No file under `MovieTracker.Evals/` contains the identifiers `TemplateCheck`, `CheckOrder`, `CheckKind`, `Region`, or `TemplateCheckToString` (as a type, method, or field name). No file in the harness contains a hard-coded `"listIncludes"` string literal outside the `ListIncludesCheck.CheckName` constant itself.
- **SC-006**: `MovieTracker.Evals/Program.cs` contains only host creation, config check, dependency wiring, the `EvalRunner.RunAsync` call, and exit-code assignment; no case bookkeeping is in `Program`.
- **SC-007**: `dotnet build movie-tracker-api.slnx` succeeds with zero new warnings compared with `main`.
- **SC-008**: The only paths changed by this slice are inside `MovieTracker.Evals/`, `MovieTracker.Api/Tools/MovieToolsRegistration.cs` (as the renamed file), the matching one-line edit in `CLAUDE.md`, and files under `specs/012-eval-cleanup/`. `.gitignore` and specs `001` through `011` are not touched.

## Assumptions

- The agent, prompts, tool set, Cosmos data, and OpenAI model are held constant for this slice; this feature only reshapes the eval harness.
- The test data (plot anchor cases, template questions) is unchanged. Changing a question or an anchor title is explicitly out of scope.
- TMDb's popular page 1 reliably returns at least 2 entries during a normal run; if it does not, the harness surfaces the precondition failure and exits non-zero, matching today's behavior.
- The previous `results/last-run.json` in the repository (schema without `skipped` fields) is loaded by this slice's reader without error; after the first run on this slice, the file carries the new `skipped` fields.
- The `MovieTools` -> `MovieToolsRegistration` rename is code-cosmetic and does not change the registered DI graph; both `typeof` usages continue to resolve the same assembly type.
- Codex cross-review (constitution's Development Workflow) runs after each Spec Kit stage in this slice.
- CI, GitHub Actions, and deployment infrastructure MUST NOT change here; Option A/B comparison and an LLM grader are deferred to later slices.
