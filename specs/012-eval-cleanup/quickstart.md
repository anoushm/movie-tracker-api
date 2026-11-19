# Quickstart: Eval Cleanup (012)

Validation guide for running the reshaped harness and confirming the new output shape. This is the owner's verification path; no new test project exists.

## Prerequisites

1. Azure CLI signed in to the right tenant: `az login` (prompt from a fresh shell is fine).
2. .NET 10 SDK installed and on `PATH`.
3. Working directory: `C:\projects\dev\movie-tracker-api` (Windows).
4. `MovieTracker.Api/appsettings.Development.json` populated as per spec 011 (gitignored; contains `Azure:TenantId`, `KeyVault:Uri`, `Cosmos:Endpoint`). Nothing new in this slice.

## Build (no new warnings)

```powershell
dotnet build .\movie-tracker-api.slnx
```

Expected: build succeeds; warning count matches `main`. (The `dotnet build` output lists `Warning(s)` at the end.)

## Run the eval

```powershell
dotnet run --project .\MovieTracker.Evals
```

What to look for, in order:

1. A single preamble line of the form `Popular titles for this run: slot 1 = <title>, slot 2 = <title>` (no region label, both titles come from TMDb popular page 1).
2. No `trailer produced N case(s)` line (removed).
3. Table with 18 rows: 2 anchors (ids `plot-ship-sinks`, `plot-mob-family`) + 8 templates × 2 slots (ids `director-1`, `director-2`, `actorFilmography-1`, ..., `funnyFactNotEmpty-2`).
4. Each row's `result` column shows `PASS`, `FAIL`, or `SKIP` (the `reason` column trails it). The three outcomes correspond to:
   - `PASS`: grader returned `true`.
   - `FAIL`: grader returned `false`, OR the agent call raised an HTTP / exception error.
   - `SKIP`: the case has no truth value (rating template without an IMDb rating; trailer template without a TMDb trailer).
5. Totals line in the exact form `Total: {passed} / {total} ({skipped} skipped)` where `{passed} + fails = {total}` (skipped excluded from both sides).
6. Per-check comparison lines in the form `{check}: p/t -> p/t` with an appended ` ({n} skipped)` after the current side when `n > 0`.
7. Final line `Wrote MovieTracker.Evals\results\last-run.json`.

## Previous baseline compatibility

Before running the eval the first time on this slice, confirm the repository's committed `MovieTracker.Evals/results/last-run.json` (Nov 12 baseline, no `skipped` fields) loads without a parse warning. The harness prints `Warning: could not parse previous results file: ...` if it fails. On success, the per-check comparison appears; the ` ({n} skipped)` suffix reflects the *current* run's skipped count per check (independent of what was in the old file), and is omitted when the current run has `n = 0` for that check.

After the first successful run, inspect the freshly written `MovieTracker.Evals/results/last-run.json`:

- Top-level object has `skipped: <int>`.
- Every entry in `perCheck` has `skipped: <int>`.
- Every entry in `cases` has `skipped: <bool>`.

## Spot-check deletes

```powershell
Get-ChildItem .\MovieTracker.Evals\ -Recurse |
  Where-Object { $_.Name -in 'TemplateCheck.cs','CheckOrder.cs','CheckKind.cs','Region.cs' }
Get-ChildItem .\MovieTracker.Api\Tools\ -Recurse |
  Where-Object { $_.Name -eq 'MovieTools.cs' }
```

Expected: no output from either command (all five files are gone: four under `MovieTracker.Evals/`, plus the renamed `MovieTools.cs` under `MovieTracker.Api/Tools/`).

## Spot-check folder layout

```powershell
Get-ChildItem .\MovieTracker.Evals\ -Directory
```

Expected: `Agent`, `Cases`, `Checks`, `Reporting`, `Tmdb`, `bin`, `obj`, `results`.

## API build check

```powershell
dotnet run --project .\MovieTracker.Api
```

Expected: starts cleanly; `GET /health` returns `200`. No functional change beyond the `MovieTools -> MovieToolsRegistration` rename. Stop with `Ctrl+C`.

## Failure recovery

- Precondition failure: `TmdbClient.GetPopularAsync()` returns fewer than 2 titles. The harness exits non-zero after printing `Precondition failure (popular title selection): ...`. Retry; TMDb page 1 reliably has 20 entries.
- Unknown check in `eval-movie-templates.json`: the harness exits non-zero before any agent call, printing the unknown `check` string. Fix the JSON.
- A single-run score below the prior baseline is not necessarily a regression — some of the gain is from the harness no longer counting skips as fails. Within this slice, the eval is intentionally run only once (so the Nov 12 `last-run.json` is still on disk when the new run reads it); if the score drops hard, inspect the table rather than re-running.
