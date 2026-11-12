# Implementation Plan: Movie Agent Evaluation Harness

**Branch**: `008-eval-harness` | **Date**: 2026-11-09 | **Reworked**: 2026-11-10 (rich-eval) | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-eval-harness/spec.md`; authoritative source `specs/008-eval-harness/rich-eval-prompt.md` (supersedes `rework-prompt.md` and the original `prompt.md` wherever they conflict).

## Summary

A standalone console program `MovieTracker.Evals` that hosts the movie-tracker API in-process via `WebApplicationFactory<global::Program>`, asks the agent one case question (two for the multi-turn `followUpDirector` case, on the same chat) through the API's HTTP contract, and grades each answer by routing the chat response through `Microsoft.Agents.AI.LocalEvaluator`. There is **one `IEvalCheck` per check kind**; the composition root groups cases by check kind and calls `EvaluateAsync` **once per group** with a `LocalEvaluator` holding that check's single `FunctionEvaluator`. The harness reads pass/fail and reason straight off `AgentEvaluationResults.Items[i].Metrics[checkName]` as a `Microsoft.Extensions.AI.Evaluation.BooleanMetric` — no captured variables, no pipe-delimited strings, no central grader. Each run produces roughly ten cases: 2 plot anchors from `eval-movie-cases.json` + up to 8 templated cases built from 3 regional run titles (one each from `US`, `IN`, `SE`, random page 1..5). Each produced case is asked **once** — the previous two-repetition logic is removed. No change to the API project; the only edit outside `MovieTracker.Evals/` and `specs/008-eval-harness/` is the one-line project registration already present in `movie-tracker-api.slnx`.

The technical approach the slice commits to:

- Host the API via `WebApplicationFactory<global::Program>`; the harness's own `Program` lives in namespace `MovieTracker.Evals` and does not shadow the global one.
- Grade with MAF `LocalEvaluator` + one `IEvalCheck` per check kind. Group cases by check kind; call `EvaluateAsync` once per group so the one `FunctionEvaluator` in that evaluator runs once per item without a captured side channel. Read `EvaluationResult.Metrics[checkName]` (a `BooleanMetric`) off the returned `AgentEvaluationResults.Items` and translate `boolean.Value` + `boolean.Reason` into the per-case result.
- Map one case's shape to the framework item shape: `EvalItem.Response` = `turn.text`; `EvalItem.Context` = JSON of `{ funnyFact, movieList[] }` (projected to the fields any check kind needs — per-entry TMDb id, title, release date, genre names, genre ids); `EvalItem.ExpectedOutput` = JSON of the per-check expected-data record. "funnyFact stays in Context" from the authoritative prompt is preserved by carrying funnyFact inside the Context JSON alongside the movie-list.
- Each per-check `IEvalCheck` deserializes `EvalItem.Context` into a shared actual-response record and `EvalItem.ExpectedOutput` into the check-specific expected record, then runs a small synchronous predicate.
- Resolve TMDb/OMDb keys through `factory.Services.GetRequiredService<IConfiguration>()`.
- Keep configuration-fail-fast at startup: before asking any question, verify both `TheMovieDb:Api-Key` and `OpenMovieDb:Api-Key` are present on the hosted API's `IConfiguration` and halt with the missing-key name if either is absent.
- Each case runs **once**. Multi-turn `followUpDirector` opens one `chatId`, POSTs turn 1 (discards the answer), POSTs turn 2 on the same `chatId`, grades only the turn 2 response.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`, matching `MovieTracker.Api`).

**Primary Dependencies**:
- `Microsoft.AspNetCore.Mvc.Testing` pinned to `10.0.10` (same major/minor/patch as the API's ASP.NET Core packages).
- `Microsoft.Agents.AI` pinned to `1.17.0` (same as the API). FR-020 requires verifying the exact types and members the harness uses are present before the grading path is written.
- Project reference to `MovieTracker.Api` (entry-point assembly only; the harness talks to it over its HTTP contract).
- `System.Net.Http.Json` (via the shared framework) for the TMDb/OMDb client calls the harness makes directly.
- `System.Text.Json` for case files, template files, the results file, and the typed JSON payloads carried in `EvalItem.Context` / `EvalItem.ExpectedOutput`.
- **No JSON Schema validation package.** `contracts/*.schema.json` are documentation only; the loader enforces only unique ids per file (spec FR-004a; research.md Decision 12).

**Storage**:
- Three committed JSON files under `MovieTracker.Evals/`:
  - `eval-movie-cases.json` (two plot anchors — unchanged shape; the owner edits this file directly).
  - `eval-movie-templates.json` (eight templates — rewritten for the eight rich-eval check kinds; multi-turn templates carry a `turns` array).
  - `results/last-run.json` (last run's output; feeds the per-check comparison on the next run).
- All three paths are rooted at the source folder via `[CallerFilePath]` (research.md Decision 11).

**Testing**: None added. The harness itself is the test; acceptance is the quickstart run producing the ~10-row table and a results file.

**Target Platform**: Windows developer workstation running `dotnet run --project MovieTracker.Evals`.

**Project Type**: single console project under the repo root, sibling of `MovieTracker.Api/`. Already present in `movie-tracker-api.slnx`.

**Performance Goals**: N/A. Single on-demand run per invocation, one case at a time, no concurrency, one repetition per case.

**Constraints**:
- No change outside `MovieTracker.Evals/`, `specs/008-eval-harness/`, and the already-present `.slnx` line (spec FR-017).
- No command-line options, retries, parallel runs, random seed, LLM graders, dashboards, or CI integration.
- No `var` and no code comments in the new project; braces on every `if`; private fields in plain camelCase with no `_` prefix; one type per file; `I` prefix on interfaces; `Async` suffix on async methods (rich-eval-prompt §Rules; spec FR-024).
- Each case runs exactly once per run.

**Scale/Scope**: ~10 cases per run (2 anchors + up to 8 templated). One results file. No backfill, no history management beyond git history on the single results file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates derived from `.specify/memory/constitution.md` v1.1.0:

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Managed Identity Over Secrets (NON-NEGOTIABLE) | **N/A** | Local developer tool; reads two existing API-local config keys through the API's own `IConfiguration` — no new identity or secret surface. |
| II | Infrastructure Is Bicep, Not Clicks | **N/A** | No Azure resource created or modified. |
| III | Incremental Migration With Declared Boundaries | **PASS** | Spec names what the slice adds (nine `IEvalCheck` files, one-per-check template registry, three regional run titles, per-check comparison) and what it does not (API changes, infra, CI, LLM graders, retries, parallel runs, fixing any failing check). FR-017 encodes the exclusion. |
| IV | Dependencies Prove Themselves | **PASS (with scope note)** | The harness takes two dependencies — `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.Agents.AI`. The former is exercised by the in-process API host, which fails fast if the API's required configuration is missing. The latter is exercised by every grading decision; the six verification items FR-020 lists are provisionally documented on Microsoft Learn for 1.17.0, and FR-020 halts the work before the grading path is written if any of them is missing from the installed assembly. |
| V | No Secret Material In Source | **PASS** | No secret introduced. Keys read from the API's existing `IConfiguration` chain. |

Workflow gates:

- Spec Kit flow: `/speckit-specify` done (Codex-approved, 3 rounds on rich-eval). This is `/speckit-plan`. **PASS**.
- Codex cross-review at each stage: the plan stage ends with a Codex round-based loop (up to 6 rounds). **PASS** (built into the workflow).
- Explicit out-of-scope list in spec: present. **PASS**.
- Windows CRLF: the new project is on Windows; the editor writes CRLF. **PASS**.

No complexity waivers required.

**Result**: all applicable gates pass. Proceed to Phase 0.

**Post-design recheck (after `research.md`, `data-model.md`, `contracts/`, `quickstart.md`)**: no new Azure resource, no new secret, no Bicep change, no new runtime dependency beyond the two already noted. Edits in this slice land in exactly two locations: files under `MovieTracker.Evals/` and files under `specs/008-eval-harness/`. The one-line `.slnx` project registration from the prior rework stays unchanged. Everything else — `MovieTracker.Api/`, `infrastructure/`, `.github/`, `CLAUDE.md`, and `specs/001..007/` — is unchanged (FR-017). All gates still pass; no complexity waiver introduced during design.

## Project Structure

### Documentation (this feature)

```text
specs/008-eval-harness/
├── spec.md                 # /speckit-specify output (Codex-approved, rich-eval round 3)
├── prompt.md               # Original source — superseded by rich-eval-prompt.md
├── rework-prompt.md        # 2026-11-10 rework prompt — superseded by rich-eval-prompt.md
├── rich-eval-prompt.md     # Authoritative source (supersedes the two prompts above on conflict)
├── checklists/
│   └── requirements.md     # Quality checklist
├── plan.md                 # This file
├── research.md             # Phase 0 output
├── data-model.md           # Phase 1 output
├── contracts/              # Phase 1 output
│   ├── eval-movie-cases.schema.json
│   ├── eval-movie-templates.schema.json
│   ├── last-run.schema.json
│   └── README.md
├── quickstart.md           # Phase 1 output
└── tasks.md                # Phase 2 output
```

### Source Code (repository root)

```text
MovieTracker.Evals/                       # Existing console project, sibling of MovieTracker.Api
├── MovieTracker.Evals.csproj             # net10.0; PackageReferences: Microsoft.AspNetCore.Mvc.Testing 10.0.10, Microsoft.Agents.AI 1.17.0; ProjectReference to MovieTracker.Api
├── Program.cs                            # Composition + run loop (file-scoped namespace MovieTracker.Evals)
├── eval-movie-cases.json                 # Two plot anchors (committed)
├── eval-movie-templates.json             # Eight templates for the eight rich-eval check kinds (committed)
├── results/
│   └── last-run.json                     # Last run's results (committed)
└── (one type per file; flat layout, file-scoped namespace, PascalCase types, camelCase fields with no `_` prefix,
   explicit types never `var`, braces on every `if`, no code comments — rich-eval-prompt §Rules; every IEvalCheck file sits at the project root alongside everything else)

movie-tracker-api.slnx                    # UNCHANGED in this rework (project registration added in an earlier rework)
MovieTracker.Api/                         # UNCHANGED (FR-017)
infrastructure/                           # UNCHANGED
.github/                                  # UNCHANGED
CLAUDE.md                                 # UNCHANGED
specs/001-…/ through specs/007-…/         # UNCHANGED
```

**Structure Decision**: single console project at repo root. Flat layout, no subfolders other than `results/` per rich-eval-prompt §Rules. The one-type-per-file rule means each `IEvalCheck`, DTO, loader, client, reporter, and composition root sits at `MovieTracker.Evals/<TypeName>.cs`. The concrete type list is produced during `/speckit-tasks`.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified.

No violations. Table omitted.
