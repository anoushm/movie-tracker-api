# Implementation Plan: Eval Cleanup

**Branch**: `012-eval-cleanup` | **Date**: 2026-11-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/012-eval-cleanup/spec.md`

## Summary

Make `MovieTracker.Evals` good enough to compare Option A vs Option B later. Two cases per template (18 cases per run instead of 10), `SKIPPED` kept apart from `FAIL`, popular titles from TMDb (no region), a subfolder layout, one source of truth for check names, orchestration extracted into `EvalRunner`, and a rename of `MovieTools` -> `MovieToolsRegistration` in the API. No behavior change to the API beyond that rename and no change to any check's grading logic.

Approach: in-place edits and renames inside `MovieTracker.Evals/`. New types (`EvalRunner`, the per-check `CheckName` constants) are added; the deleted types (`TemplateCheck`, `CheckOrder`, `CheckKind`, `Region`, `TrailerProducedCount`, `Program.TemplateCheckToString`, `FindOriginalOrder`, the tuple/dictionary bookkeeping) are removed outright, not deprecated. File moves into `Checks/`, `Cases/`, `Agent/`, `Tmdb/`, `Reporting/` happen alongside the body edits; namespaces follow folders. No test additions, no CI changes, no `.gitignore` changes.

## Technical Context

**Language/Version**: C# on .NET 10 (ASP.NET Core Web API for `MovieTracker.Api`, console app for `MovieTracker.Evals`).

**Primary Dependencies**: Microsoft.Agents.AI (agent-framework), System.Text.Json, Microsoft.Extensions.Configuration, Microsoft.Extensions.Hosting. TMDb and OMDb reached over HTTP.

**Storage**: Harness reads `eval-movie-cases.json` and `eval-movie-templates.json` at the `MovieTracker.Evals` project root. Writes `MovieTracker.Evals/results/last-run.json`. The API uses Cosmos but is only touched by the FR-021 rename in this slice.

**Testing**: no test project is added; verification is the evaluator run (`dotnet run --project MovieTracker.Evals`) plus `dotnet build movie-tracker-api.slnx` passing with no new warnings.

**Target Platform**: Windows developer workstation for the harness; Azure Container Apps (Linux) for the API. Not relevant to this slice beyond the rename.

**Project Type**: single solution with two .NET projects (`MovieTracker.Api`, `MovieTracker.Evals`) plus `infrastructure/` Bicep. This feature touches `MovieTracker.Evals/` and three lines of `MovieTracker.Api/` + `CLAUDE.md`.

**Performance Goals**: not relevant. Harness runtime is dominated by agent calls and TMDb/OMDb HTTP, both unchanged.

**Constraints**:
- `dotnet build movie-tracker-api.slnx` MUST show no new warnings compared with `main` (FR-023).
- `.gitignore`, specs 001–011, and the files listed outside the Scope MUST NOT be touched (spec Out of Scope, FR-024).
- Temp files MUST live only in `C:\projects\temp` and MUST be cleaned up (owner prompt).
- Code style: explicit types (no `var`), braces on every `if`, no code comments, private fields plain camelCase (no leading `_`), one type per file, file-scoped namespaces that match the folder (FR-024).

**Scale/Scope**: 18 graded cases per harness run, 8 templates, 9 checks. ~60 files inside `MovieTracker.Evals/` to move or edit; 3 touch points in `MovieTracker.Api/` (file rename + two `typeof` updates) plus one line in `CLAUDE.md`.

## Constitution Check

The constitution (v1.2.0) principles against this slice:

- **I. Managed Identity Over Secrets** — Not touched. The harness continues to read secrets via the Key Vault configuration provider using `AzureCliCredential`, exactly as 011 established. No new secrets, no key material, no SAS tokens.
- **II. Infrastructure Is Bicep, Not Clicks** — Not touched. No Azure resource changes; no Bicep changes.
- **III. Incremental Migration With Declared Boundaries** — Satisfied. The spec names what it imports (items 1–7 in the owner prompt) and what it does not (binding Out of Scope list). The slice is independently verifiable by running the harness and comparing output shape.
- **IV. Dependencies Prove Themselves** — Not touched. Cosmos and OpenAI probes are unchanged.
- **V. No Secret Material In Source** — Not touched.
- **Azure Platform Constraints** — No resource changes.
- **Development Workflow** — Followed: constitution, specify, plan, tasks, implement; Codex cross-review after each stage, explicit out-of-scope list, Windows working tree. The owner prompt caps rounds at 5 per stage; the constitution allows up to 6 — the lower cap wins here.

**Gates pass.** No violations to justify. Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/012-eval-cleanup/
├── prompt.md             # Owner prompt (authoritative scope)
├── spec.md               # Feature specification (approved)
├── plan.md               # This file
├── research.md           # Phase 0 output (JSON-schema compat, comparison composition, table column)
├── data-model.md         # Phase 1 output (types that change or are added)
├── quickstart.md         # Phase 1 output (how to run and verify)
├── contracts/            # Phase 1 output (eval-movie-templates.json + eval-movie-cases.json + last-run.json shapes)
├── checklists/
│   └── requirements.md   # Spec quality checklist
└── tasks.md              # Phase 2 output (NOT created here)
```

### Source Code (repository root — changed by this slice)

```text
MovieTracker.Evals/
├── Program.cs                         # Composition only; calls EvalRunner.RunAsync
├── EvalRunner.cs                      # NEW: orchestration (title select, build, ask, grade, report, save)
├── HarnessHost.cs                     # unchanged body; stays at root
├── ConfigChecker.cs                   # unchanged body; stays at root
├── ProjectRoot.cs                     # unchanged body; stays at root
├── eval-movie-cases.json              # drop "check" field on anchors
├── eval-movie-templates.json          # unchanged
├── results/
│   └── last-run.json                  # overwritten per run; new baseline carries "skipped" fields
├── Checks/
│   ├── IEvalCheck.cs
│   ├── CheckDiscovery.cs
│   ├── CheckEvaluationRunner.cs
│   ├── ActualResponseParser.cs
│   ├── ActualResponsePayload.cs
│   ├── DirectorNameMatcher.cs
│   ├── MovieMatcher.cs
│   ├── ActorFilmographyCheck.cs       # + public const string CheckName
│   ├── ActorFilmographyExpected.cs
│   ├── DirectorCheck.cs               # + CheckName
│   ├── DirectorExpected.cs
│   ├── FollowUpDirectorCheck.cs       # + CheckName
│   ├── FunnyFactNotEmptyCheck.cs      # + CheckName
│   ├── GenreDecadeCheck.cs            # + CheckName
│   ├── GenreDecadeExpected.cs
│   ├── IdentifyByPlotCheck.cs         # + CheckName
│   ├── IdentifyByPlotExpected.cs
│   ├── ListIncludesCheck.cs           # + CheckName = "listIncludes"
│   ├── ListIncludesExpected.cs
│   ├── RatingCheck.cs                 # + CheckName
│   ├── RatingExpected.cs
│   ├── TrailerCheck.cs                # + CheckName
│   └── TrailerExpected.cs
├── Cases/
│   ├── CaseTemplate.cs                # Check becomes string
│   ├── CaseTemplateFile.cs
│   ├── TemplateLoader.cs              # fail-fast validation vs. discovered check names
│   ├── GeneratedCase.cs               # Check becomes string
│   ├── GeneratedCaseBatch.cs          # drops TrailerProducedCount
│   ├── GeneratedCaseBuilder.cs        # 2 cases per template; switches on CheckName
│   ├── PlotAnchorCase.cs              # drops Check property
│   ├── PlotAnchorCaseFile.cs
│   ├── PlotAnchorCaseLoader.cs
│   ├── RunTitle.cs                    # drops Region
│   ├── RunTitleSelector.cs            # picks 2 distinct titles from popular page 1
│   └── ReleaseYearExtractor.cs
├── Agent/
│   ├── AgentClient.cs
│   ├── AgentReply.cs
│   ├── MovieListEntry.cs
│   └── EvalItemBuilder.cs             # uses ListIncludesCheck.CheckName
├── Tmdb/
│   ├── TmdbClient.cs                  # GetPopularAsync: no region param
│   ├── OmdbClient.cs
│   ├── MovieDetails.cs
│   ├── MovieCredits.cs
│   ├── MovieGenre.cs
│   ├── MovieSummary.cs
│   └── ActorFilmography.cs
└── Reporting/
    ├── Reporter.cs                    # PrintPopularTitles: "slot k = title"; PrintTable prints SKIP; PrintTotals adds "(n skipped)"; comparison appends " (n skipped)" when n>0; PrintTrailerProducedCount removed
    ├── ResultsStore.cs                # reads older files with skipped=0
    ├── RunResult.cs                   # + int Skipped
    ├── CaseResult.cs                  # + bool Skipped
    ├── PerCheckCount.cs               # + int Skipped
    ├── CheckComparison.cs             # + int? CurrentSkipped
    └── CheckComparisonComputer.cs     # takes ordered check-name list as parameter

MovieTracker.Api/Tools/
└── MovieToolsRegistration.cs          # renamed from MovieTools.cs (class renamed too; AddMovieTools kept)

CLAUDE.md                              # one-line update to Tools/ entry
```

**Deleted** (no longer exist after this slice): `MovieTracker.Evals/TemplateCheck.cs`, `MovieTracker.Evals/CheckOrder.cs`, `MovieTracker.Evals/CheckKind.cs`, `MovieTracker.Evals/Region.cs`, `MovieTracker.Api/Tools/MovieTools.cs` (renamed, not kept as a shim).

**Structure Decision**: the layout above is the target after this slice. It groups `MovieTracker.Evals/` files by responsibility while keeping the top-level composition surface (`Program`, `EvalRunner`, `HarnessHost`, `ConfigChecker`, `ProjectRoot`) and the data files at the project root so `ProjectRoot` paths do not change (FR-011). Every C# file keeps the one-type-per-file rule; namespaces are file-scoped and match the folder (FR-024).

## Phase 0 Output

See [research.md](./research.md). Three decisions are resolved there:

- How `ResultsStore` handles an older `last-run.json` without `skipped` fields (System.Text.Json default-value behavior).
- How the `(n skipped)` suffix joins the existing per-check comparison line (string composition inside `Reporter.PrintCheckComparison`, fed by a new `CurrentSkipped` field on `CheckComparison`).
- The exact format of `Reporter.PrintTable`'s result column after adding `SKIP` (width and alignment).

## Phase 1 Output

See [data-model.md](./data-model.md), [contracts/](./contracts/), and [quickstart.md](./quickstart.md).

- `data-model.md` lists every type that changes shape (adds/removes a field, changes a property type, is deleted, or is new).
- `contracts/` captures the three contract files the slice touches: `eval-movie-cases.schema.md`, `eval-movie-templates.schema.md`, and `last-run.schema.md`. These are the surfaces outside the C# code: the JSON that templates ship in, the JSON anchors ship in, and the JSON the harness writes between runs.
- `quickstart.md` is the step-by-step verification plan for the owner: a single run, what to look for in the console, and what to compare against the previous `last-run.json`.

## Complexity Tracking

Not needed; Constitution Check passes with no violations.

## Exit code contract for `Program` + `EvalRunner`

Current `Program.Main` returns `Task` and sets `Environment.ExitCode = 1` on precondition failure or missing configuration; normal completion leaves it at `0`. This slice keeps that semantics: `EvalRunner.RunAsync()` returns `Task<int>` (`0` on success, `1` on the precondition failures `Program.Main` currently handles). `Program.Main` assigns the returned value to `Environment.ExitCode`. Agent grading failures do NOT change the exit code (same as today).
