# Phase 0 Research: Eval Cleanup

**Feature**: Eval Cleanup (012) — see [spec.md](./spec.md) and [prompt.md](./prompt.md).

Only three decisions are novel. Everything else in the slice is a mechanical rename, move, or deletion.

---

## Decision 1 — How `ResultsStore` loads an older `last-run.json` without `skipped` fields

**Decision**: append `int Skipped` as the final positional parameter of `RunResult` (after `Cases`) and append `int Skipped = 0` as the final positional parameter of `PerCheckCount`. `CaseResult` stays positional with four constructor params and gains `public bool Skipped { get; init; }` as an init-only property that defaults to `false`. `ResultsStore.ReadPreviousAsync` keeps its current `JsonSerializerOptions` (no changes).

**Rationale**: with default `JsonSerializerOptions` (which `ResultsStore` uses — `PropertyNameCaseInsensitive = true`, `PropertyNamingPolicy = CamelCase`, nothing about required-param strictness), `System.Text.Json` deserialises a positional record's constructor parameters by name; when a camelCase JSON property for a constructor parameter is absent, it passes `default(T)` to the constructor. For `int`, that is `0`. On `PerCheckCount` we attach `= 0` because `Skipped` is at the end of the parameter list (no non-optional parameter follows it), so the default is legal and makes the "missing-means-zero" intent explicit at the type. On `RunResult` the final parameter position is held by `Cases`, which has no default; C# forbids a non-optional parameter after an optional parameter, so `Skipped` goes at the end with no default and relies on `System.Text.Json`'s mainline behavior (pass `default(int) = 0` when the JSON property is missing). For `CaseResult`, keeping `Skipped` as an init-only property (not a ctor parameter) is simpler: no call site needs to update, older JSON omits the property and it stays `false`, and the one grading path that produces a skipped `CaseResult` uses object-initializer syntax (`new CaseResult(...) { Skipped = true }`).

**Alternatives considered**:

- Put `Skipped` in the middle of `RunResult`'s parameter list — impossible with a default (compiler error) and awkward without one; the final-position approach keeps declaration order aligned with "logical grouping" (header counts first, details last) and sidesteps the ordering rule.
- Custom `JsonConverter` for `RunResult` / `PerCheckCount` to tolerate missing fields — unnecessary; the default behavior already handles the case.
- Make `Skipped` a nullable `int?` and coerce to `0` later — unnecessary; `0` means `0` in both old and new files and avoids a nullable round-trip in `last-run.json`.

**Verification**: the existing `MovieTracker.Evals/results/last-run.json` (committed 2026-11-12, no `skipped` fields anywhere) must load without a `JsonException` and produce `RunResult.Skipped = 0`, `PerCheckCount.Skipped = 0` for every entry. This is checked in `quickstart.md` step "Previous baseline compatibility".

---

## Decision 2 — Where the `(n skipped)` suffix is composed on the per-check comparison line

**Decision**: extend `CheckComparison` with `int? CurrentSkipped` and let `CheckComparisonComputer.Compute` populate it from the ordered per-check list of the current run. `Reporter.PrintCheckComparison` appends ` ({n} skipped)` to the current side only when `CurrentSkipped` is non-null and `> 0`.

**Rationale**: `CheckComparison` already carries `PreviousPassed`, `PreviousTotal`, `CurrentPassed`, `CurrentTotal` as nullables. Adding `CurrentSkipped` as a fifth nullable keeps the shape consistent — the reporter stays dumb (just formats a row) and the computer stays the single place that knows how to derive comparison rows from the two sides. The `PreviousSkipped` counterpart is *not* added: the prompt only asks for the current side's suffix, and older `last-run.json` files load with `0` skipped anyway.

**Alternatives considered**:

- Put `PrintCheckComparison` in charge of looking up `current.PerCheck[c].Skipped` — crosses the layer boundary (the reporter would need `RunResult`, not just a list of rows) and duplicates lookup logic the computer already owns.
- Add `PreviousSkipped` to `CheckComparison` as well — not asked for and would clutter the display logic for no visible effect; drop until a future slice needs it.

**Verification**: Story 1 scenarios 5 and 6 in `spec.md` require the suffix to appear when `n > 0` and be absent when `n = 0`.

---

## Decision 3 — Result column width in `Reporter.PrintTable` after adding `SKIP`

**Decision**: leave `resultWidth = 6`. Print `SKIP` (4 chars) with the existing `PadRight` routine — it aligns to width 6 the same way `PASS` and `FAIL` do.

**Rationale**: `PASS` and `FAIL` are 4 characters; so is `SKIP`. The constant `resultWidth = 6` already gives two trailing spaces before the `reason` column. No layout change required.

**Alternatives considered**:

- Recompute `resultWidth = max(len("PASS"), len("FAIL"), len("SKIP"), len("result"))` — adds code that does not change the value.
- Switch `result` column to a fixed width equal to 4 and rely on the separator — would break alignment with the `result` header (6 chars).

**Verification**: Story 1 scenarios 2 and 3 observe that each row's result column shows exactly one of `PASS`, `FAIL`, `SKIP`; no explicit width check is required.

---

## Non-decisions

The following are mechanical and need no research:

- Which files move to which folder — enumerated in `plan.md` and in `spec.md` Story 3.
- Which identifiers delete — enumerated in `spec.md` FR-013 and SC-005.
- The content of each `CheckName` constant — nine strings taken from the current `TemplateCheck` enum mapping in `Program.TemplateCheckToString` and `ListIncludesCheck.Name`.
- The `GeneratedCaseBuilder` nested-loop shape — iterate `templates` outer, `runTitles` inner; the existing `BuildXxxAsync` method bodies move under the loop unchanged except for the `selected` variable now coming from the inner loop.
- The `MovieToolsRegistration` rename — both `typeof(MovieTools)` usages live inside the renamed file itself; the single cross-file reference is `builder.Services.AddMovieTools(...)` in `MovieTracker.Api/Program.cs`, which is unaffected because the extension-method name is unchanged.
