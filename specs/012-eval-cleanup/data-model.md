# Phase 1 Data Model: Eval Cleanup

**Feature**: Eval Cleanup (012) — see [spec.md](./spec.md).

This document lists every C# type whose shape changes in this slice. Types that only move folder (namespace change only) are listed at the bottom for completeness. Deleted types are called out explicitly.

---

## New types

### `MovieTracker.Evals.EvalRunner`

- Root-level orchestration class (new in this slice).
- Owns what `Program.Main` runs today: title selection, case build, agent ask, per-check grading, reporting, and `last-run.json` save.
- Constructor takes only the collaborators the body uses: `TmdbClient`, `OmdbClient`, `EvalItemBuilder`, `CheckEvaluationRunner`, and the loaded templates + anchors. `AgentClient` is already captured by `EvalItemBuilder`, and the `IReadOnlyDictionary<string, IEvalCheck>` discovery is already held by `CheckEvaluationRunner`, so neither is passed to `EvalRunner`.
- Exposes a single `public Task<int> RunAsync()` returning the desired `Environment.ExitCode` (`0` on success; `1` on precondition failure — see plan's "Exit code contract").

Not shown: internal helpers for building the ordered result list, which replace the deleted `preMarked`/`toGrade`/`graded` tuples, `byId` dictionary, and `FindOriginalOrder`.

---

## Changed types

### `MovieTracker.Evals.Reporting.CaseResult`

**Before**: `public sealed record CaseResult(string Id, string Check, bool Passed, string Reason);`

**After**: positional record with the same four parameters plus an init-only `bool Skipped` property defaulting to `false`:

- `Id`: unchanged.
- `Check`: unchanged (string).
- `Passed`: unchanged; MUST be `false` whenever `Skipped == true` so counts never double-count.
- `Reason`: unchanged; when `Skipped == true` the reason is `"no truth value"`.
- `Skipped` *(new)*: `true` only for cases with no truth value. All other paths leave this `false`. Set via object-initializer syntax: `new CaseResult(id, check, false, "no truth value") { Skipped = true }`.

### `MovieTracker.Evals.Reporting.PerCheckCount`

**Before**: `public sealed record PerCheckCount(string Check, int Passed, int Total);`

**After**: `public sealed record PerCheckCount(string Check, int Passed, int Total, int Skipped = 0);`

- `Skipped` *(new)*: count of skipped cases for this check in the current run. Absent from older `last-run.json` -> `0`.
- `Passed` and `Total` count only cases with `Skipped == false` (FR-005). Invariant: `Total >= Passed` and `Skipped + Total = (count of cases for this check in this run)`.

### `MovieTracker.Evals.Reporting.RunResult`

**Before**: `public sealed record RunResult(DateTimeOffset RunAt, int Total, int Passed, IReadOnlyList<PerCheckCount> PerCheck, IReadOnlyList<CaseResult> Cases);`

**After**: positional record with `int Skipped` appended at the end:

```
public sealed record RunResult(
    DateTimeOffset RunAt,
    int Total,
    int Passed,
    IReadOnlyList<PerCheckCount> PerCheck,
    IReadOnlyList<CaseResult> Cases,
    int Skipped);
```

`Skipped` has no default here — C# forbids a non-optional parameter (`Cases`) after an optional one. The reader still accepts older files without the `skipped` property because `System.Text.Json` passes `default(int) = 0` by default when a constructor parameter is missing from the JSON. See research Decision 1.

- `Total` and `Passed` count only non-skipped cases. `Skipped` is the run total of `CaseResult.Skipped == true`.
- JSON ordering mirrors declaration order; older files without `skipped` deserialise with `Skipped = 0` (research Decision 1).

### `MovieTracker.Evals.Reporting.CheckComparison`

**Before**: five-tuple record `(Check, PreviousPassed?, PreviousTotal?, CurrentPassed?, CurrentTotal?)`.

**After**: six-tuple record with `int? CurrentSkipped` appended:

```
public sealed record CheckComparison(
    string Check,
    int? PreviousPassed,
    int? PreviousTotal,
    int? CurrentPassed,
    int? CurrentTotal,
    int? CurrentSkipped);
```

- `CurrentSkipped` is `null` when the check is not in the current run; `0+` otherwise. The printer uses `CurrentSkipped > 0` as the trigger for the suffix.

### `MovieTracker.Evals.Cases.CaseTemplate`

**Before**: `sealed class` with public setters; `Check` is `TemplateCheck` (enum).

**After**: still a `sealed class` with public setters (same shape as today — `Id`, `Template`, `Turns` are untouched; not converted to a record). Only the `Check` property changes: it is now `string` carrying the raw JSON value. Validated in `TemplateLoader.Load` against the discovered check-name set (FR-014).

### `MovieTracker.Evals.Cases.GeneratedCase`

**Before**: `Check` is `TemplateCheck`.

**After**: `Check` is `string`. The id format `{templateId}-{k}` is unchanged. `NoTruthValue` stays as the carrier that `EvalRunner` reads to decide `Skipped = true`.

### `MovieTracker.Evals.Cases.GeneratedCaseBatch`

**Before**: `public sealed record GeneratedCaseBatch(IReadOnlyList<GeneratedCase> Cases, int TrailerProducedCount);`

**After**: `public sealed record GeneratedCaseBatch(IReadOnlyList<GeneratedCase> Cases);`

`TrailerProducedCount` is removed (FR-004).

### `MovieTracker.Evals.Cases.GeneratedCaseBuilder`

- `BuildAsync` loops `templates` outer, `runTitles` inner — producing exactly 2 cases per template with ids `{templateId}-1`, `{templateId}-2` (FR-001).
- The inner `switch` on `template.Check` becomes a switch on the `*Check.CheckName` string constants (FR-013). Default arm throws `InvalidOperationException` with the unknown check name.
- The trailer arm now returns a `GeneratedCase` with `NoTruthValue=true` when TMDb has no trailer, instead of returning `null` from `TryBuildTrailerAsync` and dropping the case (FR-004).

### `MovieTracker.Evals.Cases.RunTitle`

**Before**: `RunTitle(int TmdbId, string Title, Region Region, int Slot)`.

**After**: `RunTitle(int TmdbId, string Title, int Slot)`. `Region` removed (FR-010).

### `MovieTracker.Evals.Cases.RunTitleSelector`

- Drops the per-region loop. Calls `tmdbClient.GetPopularAsync()` once (new signature, no region).
- Picks 2 distinct titles from the returned list at random (FR-009). Throws with the existing precondition-message style if fewer than 2 entries are available.

### `MovieTracker.Evals.Tmdb.TmdbClient`

- `GetPopularAsync(Region region, int page)` -> `GetPopularAsync()`: requests `movie/popular?page=1` with no `region` query parameter (FR-009). All other TMDb methods are unchanged.

### `MovieTracker.Evals.Cases.PlotAnchorCase`

**Before**: `sealed class` with public setters and properties `Id`, `Question`, `Check` (enum `CheckKind`), `Expected`.

**After**: still a `sealed class` with public setters (not converted to a record). Only the `Check` property is removed (FR-015); `Id`, `Question`, `Expected` are untouched.

### `MovieTracker.Evals.Cases.PlotAnchorCaseFile`

- Unchanged in shape. It is a `sealed class` holding `List<PlotAnchorCase>` and carries no `check` reference of its own — the `check` field is removed from the JSON entries themselves (see FR-015 and the `PlotAnchorCase` change above). The JSON schema for each entry becomes `{id, question, expected}`.

### `MovieTracker.Evals.Reporting.Reporter`

- `PrintPopularTitles`: formats a single line as `Popular titles for this run: slot 1 = {title}, slot 2 = {title}` (FR-010). The one-line preamble is kept; only the region text and its trailing space are removed.
- `PrintTable`: prints `SKIP` when `CaseResult.Skipped`, else `PASS`/`FAIL` (FR-005).
- `PrintTotals`: new shape `Total: {passed} / {total} ({skipped} skipped)` (FR-005). The `(skipped)` suffix is always printed, including when `skipped == 0`, to match the prompt's example exactly.
- `PrintCheckComparison`: appends ` ({n} skipped)` after the current side when `CurrentSkipped > 0` (FR-006).
- `PrintTrailerProducedCount` *(removed)*.

### `MovieTracker.Evals.Reporting.CheckComparisonComputer`

- `Compute(RunResult? previous, RunResult current, IReadOnlyList<string> checkOrder)`: third parameter added (FR-017). The method walks `checkOrder` first (current-run order), then appends checks present only in the previous run in their previous order.
- Fills `CurrentSkipped` from `current.PerCheck`.

### `MovieTracker.Evals.Checks.*Check`

- Every `*Check` class gets `public const string CheckName = "<existing-name>";` and `public string Name => CheckName;` (FR-012). Names: `listIncludes`, `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`.

### `MovieTracker.Evals.Agent.EvalItemBuilder`

- The hard-coded `"listIncludes"` string in `BuildAnchorAsync`'s error path becomes `ListIncludesCheck.CheckName` (FR-016).

### `MovieTracker.Evals.Cases.TemplateLoader`

- After JSON parse, validates every template's `check` string against the discovered `IEvalCheck.Name` set (FR-014). Throws `InvalidOperationException` naming the unknown check.

### `MovieTracker.Evals.Program`

- Short composition root. Creates `HarnessHost`, runs `ConfigChecker`, builds the collaborators, calls `new EvalRunner(...).RunAsync()`, and sets `Environment.ExitCode` from the returned value (FR-018). No case bookkeeping.

### `MovieTracker.Api.Tools.MovieToolsRegistration`

- File renamed from `MovieTools.cs`; class renamed from `MovieTools`. The extension method `AddMovieTools` keeps its name (FR-021). The two `typeof(MovieTools)` usages inside the file become `typeof(MovieToolsRegistration)`. No behavioral change.

---

## Deleted types

- `MovieTracker.Evals.TemplateCheck` (enum) — replaced by string check names.
- `MovieTracker.Evals.CheckKind` (enum) — unused once `TemplateCheck` is gone.
- `MovieTracker.Evals.CheckOrder` (static class) — replaced by the ordered list parameter passed to `BuildPerCheck` and `CheckComparisonComputer`.
- `MovieTracker.Evals.Region` (enum) — region selection removed.
- `MovieTracker.Evals.Program.TemplateCheckToString` (private static method) — no callers once `GeneratedCase.Check` is a string.
- `MovieTracker.Api.Tools.MovieTools` (static class) — renamed to `MovieToolsRegistration`.

---

## Namespace-only moves

Every file listed in Story 3's folder table that is not changed above moves to its target folder with only the namespace line and necessary `using` lines edited. Namespaces take the form `MovieTracker.Evals.<Folder>` matching the containing folder (FR-011, FR-024).

## Invariants after this slice

- `anchors.Count + generatedCases.Count` equals the number of rows in the printed table (18 with current data).
- For every `CaseResult`: `Skipped => !Passed` (a skipped case never counts as passed).
- For every `PerCheckCount`: `Passed <= Total` (totals exclude skipped).
- `RunResult.Total` = sum of `PerCheckCount.Total`. `RunResult.Passed` = sum of `PerCheckCount.Passed`. `RunResult.Skipped` = sum of `PerCheckCount.Skipped`.
- `GeneratedCase.Check` and `CaseTemplate.Check` only ever hold a value that appears in the discovered `IEvalCheck.Name` set.
