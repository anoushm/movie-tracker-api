# Phase 0 Research: Eval Cleanup

**Feature**: Eval Cleanup (012) — see [spec.md](./spec.md) and [prompt.md](./prompt.md).

Only three decisions are novel. Everything else in the slice is a mechanical rename, move, or deletion.

---

## Decision 1 — How `ResultsStore` loads an older `last-run.json` without `skipped` fields

**Decision**: add positional `int Skipped = 0` to `RunResult`'s primary constructor and `int Skipped = 0` to `PerCheckCount`'s primary constructor. `CaseResult` becomes a non-positional record with `public bool Skipped { get; init; }` (default `false`). No `JsonSerializerOptions` changes in `ResultsStore`.

**Rationale**: `System.Text.Json` deserialises a record's primary-constructor parameters by position-and-name; when a camelCase JSON property for a constructor parameter is absent, it passes `default(T)` to the constructor. For `int`, that is `0`; for `bool`, that is `false`. An explicit parameter default (`= 0`) makes the intent visible to a reader and lets `JsonSerializer` succeed even if `JsonSerializerOptions.RespectRequiredConstructorParameters` is ever toggled on. For `CaseResult`, keeping `Skipped` as an init-only property (not a ctor parameter) is simpler — every grading path that produces a `CaseResult` either keeps the default (`false`) or sets `Skipped = true` once via object-initializer syntax, and older JSON omits the property silently.

**Alternatives considered**:

- Custom `JsonConverter` for `RunResult` / `PerCheckCount` to tolerate missing fields — unnecessary complexity; the default behavior already handles the case.
- Keep `RunResult`/`PerCheckCount` positional and add `Skipped` as the last parameter with no default — works but loses the "0 when missing" signal at the call site; a reader seeing a bare `Skipped` ctor parameter has to go read `ResultsStore` to know how older files behave.
- Make `Skipped` a nullable `int?` and coerce to `0` later — unnecessary; `0` means `0` in both old and new files.

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

**Verification**: Story 1 scenarios 2 and 3 observe that each row ends in exactly one of `PASS`, `FAIL`, `SKIP`; no explicit width check is required.

---

## Non-decisions

The following are mechanical and need no research:

- Which files move to which folder — enumerated in `plan.md` and in `spec.md` Story 3.
- Which identifiers delete — enumerated in `spec.md` FR-013 and SC-005.
- The content of each `CheckName` constant — nine strings taken from the current `TemplateCheck` enum mapping in `Program.TemplateCheckToString` and `ListIncludesCheck.Name`.
- The `GeneratedCaseBuilder` nested-loop shape — iterate `templates` outer, `runTitles` inner; the existing `BuildXxxAsync` method bodies move under the loop unchanged except for the `selected` variable now coming from the inner loop.
- The `MovieToolsRegistration` rename — both `typeof(MovieTools)` usages live inside the renamed file itself (`Program.cs:35` calls the extension method, not the type); no cross-file reference to update.
