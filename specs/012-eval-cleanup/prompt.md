# 012 Eval Cleanup: Spec Kit Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api` (branch `main`).
Goal: make `MovieTracker.Evals` good enough to compare Option A vs Option B later. More cases per check, skipped cases kept apart from failures, only well-known titles, and a cleaner harness.
No behavior change to `MovieTracker.Api` (item 7 is a rename only).

Why: the last run (`MovieTracker.Evals/results/last-run.json`, Nov 12) had 10 cases, 1 per check, 6/10 passed. One answer swings a check between 0% and 100%, and `rating-2` failed only with `no truth value`, which is a harness data gap, not an agent failure.

---

## Rules

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly. Use the Spec Kit flow (specify, plan, tasks, implement).
- No commit, no push, no branch, no git commands of any kind. The owner does all git.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces that match the folder.
- Do not add anything not listed in Scope.
- Leave specs `001` to `011` unchanged.

## Scope

### 1. Two generated cases per check
- Every template produces 2 cases, one per run title.
- Case `k` of every template uses run title `k` (k = 1, 2). Case id stays `{templateId}-{k}`.
- The 2 plot anchors in `eval-movie-cases.json` stay as they are (not multiplied).
- Result: 8 templates x 2 + 2 anchors = 18 cases per run.

### 2. SKIPPED is not a failure
- A case with no truth value is reported as `SKIPPED`, not `FAIL`.
- A trailer case where TMDb has no trailer is also `SKIPPED` (no truth value). It is no longer silently dropped. Remove `TrailerProducedCount`, `GeneratedCaseBatch.TrailerProducedCount` and `Reporter.PrintTrailerProducedCount`.
- `CaseResult` gets `bool Skipped`. `PerCheckCount` and `RunResult` get `int Skipped`.
- `Passed` and `Total` count only cases that were not skipped.
- Table result column: `PASS`, `FAIL` or `SKIP`.
- Totals line: `Total: {passed} / {total} ({skipped} skipped)`.
- Per-check comparison: unchanged format `p/t -> p/t`, plus ` ({skipped} skipped)` after the current side when it is above 0.
- An agent error (HTTP error or exception) stays `FAIL`.
- `last-run.json` carries the new `skipped` fields. An older file without them must still load (missing = 0).

### 3. Run titles from TMDb popular only
- `TmdbClient.GetPopularAsync()` calls `movie/popular?page=1` with no region parameter.
- `RunTitleSelector` picks 2 distinct titles at random from that page. If the page has fewer than 2 entries, fail with the existing precondition message style.
- Delete `Region.cs`. Remove `Region` from `RunTitle` and from `Reporter.PrintPopularTitles` (print `slot {k} = {title}`).

### 4. Subfolders in `MovieTracker.Evals`
Move files with no other change except the namespace (`MovieTracker.Evals.<Folder>`) and the `using` lines this needs:

| Folder | Files |
|---|---|
| (root) | `Program`, `HarnessHost`, `EvalRunner` (new), `ConfigChecker`, `ProjectRoot`, `eval-movie-cases.json`, `eval-movie-templates.json`, `results/` |
| `Checks/` | `IEvalCheck`, `CheckDiscovery`, `CheckEvaluationRunner`, all 9 `*Check`, all 7 `*Expected`, `DirectorNameMatcher`, `MovieMatcher`, `ActualResponseParser`, `ActualResponsePayload` |
| `Cases/` | `CaseTemplate`, `CaseTemplateFile`, `TemplateLoader`, `GeneratedCase`, `GeneratedCaseBatch`, `GeneratedCaseBuilder`, `PlotAnchorCase`, `PlotAnchorCaseFile`, `PlotAnchorCaseLoader`, `RunTitle`, `RunTitleSelector`, `ReleaseYearExtractor` |
| `Agent/` | `AgentClient`, `AgentReply`, `MovieListEntry`, `EvalItemBuilder` |
| `Tmdb/` | `TmdbClient`, `OmdbClient`, `MovieDetails`, `MovieCredits`, `MovieGenre`, `MovieSummary`, `ActorFilmography` |
| `Reporting/` | `Reporter`, `ResultsStore`, `RunResult`, `CaseResult`, `PerCheckCount`, `CheckComparison`, `CheckComparisonComputer` |

The JSON files and `results/` stay at the root, so `ProjectRoot` paths do not change.

### 5. One source of truth for check names
- Each check class gets `public const string CheckName = "<name>";` and `Name => CheckName`. The names stay exactly as today.
- Delete `TemplateCheck.cs`, `CheckOrder.cs` and `CheckKind.cs`, plus `Program.TemplateCheckToString`.
- `CaseTemplate.Check` and `GeneratedCase.Check` become `string`. `GeneratedCaseBuilder` switches on the `*Check.CheckName` constants.
- `TemplateLoader` fails fast if a template's `check` is not a discovered `IEvalCheck.Name`.
- Plot anchors are always `listIncludes`: remove `PlotAnchorCase.Check` and the `"check"` field from `eval-movie-cases.json`. Anchors use `ListIncludesCheck.CheckName`.
- Hard-coded `"listIncludes"` strings (for example in `EvalItemBuilder`) use `ListIncludesCheck.CheckName`.
- Check order = `listIncludes` first, then the order of `eval-movie-templates.json`. `BuildPerCheck` and `CheckComparisonComputer` take this order as a parameter. Checks found only in the previous run come after, in their previous order.

### 6. `EvalRunner`
- New `EvalRunner` class at the root holds the orchestration that is in `Program.Main` today (title selection, case build, ask, grade, report, save).
- `Program.Main` only creates the host, checks config, builds the dependencies, calls `EvalRunner.RunAsync()` and sets the exit code.
- Simplify ordering: build one ordered list of cases (anchors, then generated). Each skipped or errored case gets its result right away. Other cases are graded per check group and written back into the same index. The output is that list in order. Remove the `preMarked`/`toGrade`/`graded` tuples, the `byId` dictionary and `FindOriginalOrder`.
- Console output stays as it is except for the changes in items 2 and 3.

### 7. Rename `MovieTools`
- `MovieTracker.Api/Tools/MovieTools.cs` becomes `MovieToolsRegistration.cs`, class `MovieToolsRegistration`. Keep the `AddMovieTools` method name.
- Update the two `typeof(MovieTools)` usages and the `MovieTools (DI registration)` entry in `CLAUDE.md`. Nothing else in the API changes.

## Out of scope
- New checks, an LLM grader, CI or workflow changes, any agent, prompt, tool or endpoint behavior change, Option B work.
- Changing check logic, question templates or anchor questions.
- Updating docs in specs `001` to `011`.

---

## Steps

1. Spec Kit: `/speckit-specify` (feature number 12, short name `eval-cleanup`, so the artifacts land in `specs/012-eval-cleanup/` next to this prompt), then `/speckit-plan`, `/speckit-tasks` and `/speckit-implement`.
2. After each stage, run a Codex review with `codex exec`, stdin closed (append `< /dev/null`).
   Guardrails: stay focused on items 1 to 7, cut inflated or overkill work, and keep it simple without losing any listed change.
   Fix the findings and review again. Repeat until Codex approves, up to 5 rounds per stage. If round 5 is not approved, stop and report the open findings.
3. Build: `dotnet build movie-tracker-api.slnx`. There must be no new warnings compared with `main`.
4. Run the eval: `dotnet run --project MovieTracker.Evals` (needs `az login`). The new `results/last-run.json` is the new baseline.
5. Report to the owner: the console table, the totals line, the per-check comparison, Codex rounds per stage, and anything left open.

## Done when
- `dotnet build movie-tracker-api.slnx` succeeds with no new warnings.
- `dotnet run --project MovieTracker.Evals` prints the table (with PASS, FAIL and SKIP), the totals with the skipped count, and the per-check comparison.
- There are 18 cases per run: 2 per template plus 2 anchors.
- `TemplateCheck`, `CheckOrder`, `CheckKind`, `Region` and `TemplateCheckToString` are gone.
- `Program.cs` only wires things up and calls `EvalRunner`.
- `MovieTracker.Evals/results/last-run.json` holds the new baseline.
- Only `MovieTracker.Evals/`, `MovieTracker.Api/Tools/MovieToolsRegistration.cs` (renamed), `CLAUDE.md` (one line) and `specs/012-eval-cleanup/` change.
