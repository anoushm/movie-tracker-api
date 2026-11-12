# Quickstart: Movie Agent Evaluation Harness

**Feature**: 008-eval-harness

A one-page runbook that proves the harness works end-to-end after the rich-eval rework.

## Prerequisites

1. .NET 10 SDK installed.
2. Local configuration the API already needs to run:
   - `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, `AzureOpenAI:DeploymentName`
   - `TheMovieDb:Api-Key`
   - `Cosmos:Endpoint`, `Cosmos:Database`, `Cosmos:Container`
3. Configuration key for OMDb ratings (used by the `rating` check):
   - `OpenMovieDb:Api-Key` — set it where you already set `TheMovieDb:Api-Key` (env var `OpenMovieDb__Api-Key` is fine). The API tolerates its absence; the harness does not.
4. Cosmos reachable from Windows with `DefaultAzureCredential` and `AZURE_CLIENT_ID` **unset** — see the `README.md` "Cosmos DB Local Development" section. A run writes one chat-session document per executed case.
5. Git branch `008-eval-harness` checked out.

## Build

```powershell
dotnet build .\movie-tracker-api.slnx
```

Expected: build succeeds with no new warnings.

## First run (no previous results file)

```powershell
dotnet run --project .\MovieTracker.Evals
```

Expected console output (shape, not exact values):

```
=== Movie agent eval run — 2026-11-10T18:42:17Z ===

Popular titles for this run: US slot 1 = <title>, IN slot 2 = <title>, SE slot 3 = <title>
trailer produced 1 case(s)

id                        check                       result   reason
------------------------- --------------------------- -------- -------------------------------
plot-ship-sinks           listIncludes                PASS     ok
plot-mob-family           listIncludes                PASS     ok
director-2                director                    PASS     ok
actorFilmography-1        actorFilmography            FAIL     movie 'X' is not in Y's filmography
identifyByPlot-3          identifyByPlot              PASS     ok
genreDecade-1             genreDecade                 FAIL     movie 'Z' releaseDate 2001 outside 1990..1999
rating-2                  rating                      PASS     ok
trailer-3                 trailer                     PASS     ok
followUpDirector-2        followUpDirector            PASS     ok
funnyFactNotEmpty-1       funnyFactNotEmpty           PASS     ok

Total: 8 / 10
(No previous run on disk; skipping per-check comparison.)
Wrote MovieTracker.Evals/results/last-run.json
```

(Row count = 2 anchors + up to 8 templated cases. Smaller **only** when the `trailer` template does not qualify (no case produced). A case pre-marked FAIL under FR-016 still appears in the table as FAIL and still counts toward the total.)

Verify:

- One "Popular titles for this run" block with three regional titles.
- A "trailer produced <n> case(s)" line (0 or 1).
- The two plot-anchor rows always appear.
- `MovieTracker.Evals/results/last-run.json` exists and matches `contracts/last-run.schema.json`.

## Second run (per-check comparison)

```powershell
dotnet run --project .\MovieTracker.Evals
```

Expected: the table reprints, then a per-check comparison block for every known check kind plus `total:`:

```
...
Per-check comparison (previous -> current):
  listIncludes:          2/2 -> 2/2
  director:              1/1 -> 1/1
  actorFilmography:      0/1 -> 1/1
  identifyByPlot:        1/1 -> 1/1
  genreDecade:           0/1 -> 0/1
  rating:                1/1 -> 1/1
  trailer:               1/1 -> 1/1
  followUpDirector:      1/1 -> 1/1
  funnyFactNotEmpty:     1/1 -> 1/1
  total:                 8/10 -> 9/10
Wrote MovieTracker.Evals/results/last-run.json
```

Check kinds that produced no cases in a run render `-` on that side (e.g. `trailer: - -> 1/1` when the previous run had no trailer qualifier). A check kind absent on both sides prints `- -> -`.

## Missing-key behavior

```
OpenMovieDb:Api-Key is missing from the hosted API's IConfiguration. Nothing was asked.
```

Process exits non-zero. Same applies to a missing `TheMovieDb:Api-Key`, except the message comes from the API's own fail-fast.

## Per-case failure continuation

If a chat-call failure (FR-015) fires for one case, the harness records it `FAIL` with the error message and continues. If an FR-016 `no truth value` short-circuit fires (missing director / lead actor / overview / genre / IMDb id / rating / actor credits — see data-model.md "Truth-resolution rules"), the case is recorded `FAIL` with reason `no truth value` and no chat call is made. Non-2xx responses from TMDb or OMDb during truth lookup are **precondition halts** (the run stops with a named error and no results file).

### FR-015 runtime check (per-case chat failure keeps the run alive) — owner-executed

Set an obviously invalid Azure OpenAI key and re-run; every row past truth resolution is FAIL with an HTTP status in the reason; `results/last-run.json` is still written.

```powershell
$env:AzureOpenAI__ApiKey = "invalid"
dotnet run --project .\MovieTracker.Evals
Remove-Item Env:\AzureOpenAI__ApiKey
```

### FR-016 verification — code walkthrough

Confirm via the composition root and the generated-case builder that:

- The builder sets `NoTruthValue = true` only on 2xx-with-missing-field for the truth-requiring templates.
- The composition root short-circuits `NoTruthValue == true` to a `FAIL` `CaseResult` with reason `no truth value` **before** any `/api/chat/start` call.
- No `IEvalCheck` runs for those cases.

## Done-when (from `rich-eval-prompt.md`)

- `dotnet build .\movie-tracker-api.slnx` succeeds with no new warnings.
- `dotnet run --project .\MovieTracker.Evals` (owner-executed with real keys) prints the "Popular titles for this run" block, the per-template `trailer` count, the per-case table, the totals line, and on a second run the per-check comparison.
- Nine `IEvalCheck` files exist (one per check kind), each with a `Name` and `EvalCheckResult Evaluate(EvalItem item)`. No `EvalCheckRegistry`, no `CheckEvaluator`, no captured variables.
- `eval-movie-cases.json` holds only the two plot anchors; `eval-movie-templates.json` holds the eight templates.
- Each produced case runs exactly once (no two-rep logic).
- `results/last-run.json` is written in the new schema with the per-check pass/total counts.
- Only files under `MovieTracker.Evals/` and `specs/008-eval-harness/` differ (plus the already-present `movie-tracker-api.slnx` line).
- No `var`, no `_` field prefix, no code comments in `MovieTracker.Evals/`.

## References

- Entity shapes: [`data-model.md`](data-model.md)
- File shapes: [`contracts/`](contracts/)
- Decisions behind the design: [`research.md`](research.md)
- Full requirements: [`spec.md`](spec.md)
- Authoritative prompt: [`rich-eval-prompt.md`](rich-eval-prompt.md) (supersedes `rework-prompt.md` and `prompt.md`)
