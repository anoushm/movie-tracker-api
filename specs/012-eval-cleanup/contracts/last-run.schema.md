# Contract: `last-run.json`

Shape of the file written by `ResultsStore.WriteAsync` to `MovieTracker.Evals/results/last-run.json`. Previous run is read from the same path by `ResultsStore.ReadPreviousAsync` at the start of the next run.

**Structural change in this slice**: add `skipped` to `RunResult` and to every `PerCheckCount`; add `skipped` to each `CaseResult` (FR-003, FR-008). The reader loads older files without these fields by treating the missing value as `0` (for the int fields) or `false` (for the bool field).

## New shape

```json
{
  "runAt": "<ISO-8601 datetime offset>",
  "total": <int>,
  "passed": <int>,
  "skipped": <int>,
  "perCheck": [
    { "check": "<string>", "passed": <int>, "total": <int>, "skipped": <int> }
  ],
  "cases": [
    { "id": "<string>", "check": "<string>", "passed": <bool>, "skipped": <bool>, "reason": "<string>" }
  ]
}
```

- `total` and `passed` count only non-skipped cases (`skipped` on each `CaseResult` is `false`). `total` = sum of per-check `total`.
- `skipped` is the count of `CaseResult.skipped == true` in `cases`, and the sum of per-check `skipped`.
- `perCheck` ordering follows the computed check order: `listIncludes` first (since there is always at least one anchor), then the templates in `eval-movie-templates.json` order, filtered to checks that appeared in the run.
- `cases` ordering follows the harness execution order: anchors first (in anchor-file order), then templates × slots (iteration of `templates` outer, slots 1..2 inner).

## Reader behavior (older file)

- An older file written before this slice has none of the four `skipped` fields anywhere. `System.Text.Json` deserialises the missing fields as `0` (ints) and `false` (bool). The comparison still renders.
- An older file was also written with a different case count (10 cases before this slice; 18 after). The computer handles differences by rendering `-` on the side that does not have that check.

## Example (new baseline, excerpt)

Illustrative run with 18 cases, 11 pass, 5 fail, 2 skip (one rating skip, one trailer skip). Sum of per-check `passed` = top-level `passed`; sum of per-check `total` = top-level `total`; sum of per-check `skipped` = top-level `skipped`. The `cases` array is shown here as an excerpt (just the first anchor and the skipped rating case); the real file contains all 18 entries.

```json
{
  "runAt": "2026-11-18T08:00:00.000000+00:00",
  "total": 16,
  "passed": 11,
  "skipped": 2,
  "perCheck": [
    { "check": "listIncludes",      "passed": 2, "total": 2, "skipped": 0 },
    { "check": "director",          "passed": 2, "total": 2, "skipped": 0 },
    { "check": "actorFilmography",  "passed": 1, "total": 2, "skipped": 0 },
    { "check": "identifyByPlot",    "passed": 1, "total": 2, "skipped": 0 },
    { "check": "genreDecade",       "passed": 2, "total": 2, "skipped": 0 },
    { "check": "rating",            "passed": 1, "total": 1, "skipped": 1 },
    { "check": "trailer",           "passed": 1, "total": 1, "skipped": 1 },
    { "check": "followUpDirector",  "passed": 0, "total": 2, "skipped": 0 },
    { "check": "funnyFactNotEmpty", "passed": 1, "total": 2, "skipped": 0 }
  ],
  "cases": [
    { "id": "plot-ship-sinks", "check": "listIncludes", "passed": true,  "skipped": false, "reason": "ok" },
    "... 16 more entries, including rating-2 ...",
    { "id": "funnyFactNotEmpty-2", "check": "funnyFactNotEmpty", "passed": true, "skipped": false, "reason": "ok" }
  ]
}
```
