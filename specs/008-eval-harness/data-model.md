# Phase 1 Data Model: Movie Agent Evaluation Harness

**Feature**: 008-eval-harness | **Date**: 2026-11-09 | **Reworked**: 2026-11-10 (rich-eval)

Entities, fields, validation rules, and state transitions for every data shape the harness reads, builds, or writes.

---

## Entity: `PlotAnchorCase`

**Source**: committed file `MovieTracker.Evals/eval-movie-cases.json`. Read-only. See `contracts/eval-movie-cases.schema.json`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | Stable across runs. Unique across the file. Persisted as `cases[*].id` in the results file. |
| `question` | string | yes | Prompt sent to the agent. |
| `check` | enum `CheckKind` | yes | For plot anchors this is always `listIncludes`. |
| `expected` | `string[]` | yes | Titles that must appear in the agent's answer per the movie-in-answer rule (FR-023 rule 1). |

**File-shape invariants**:

- **[loader]** `id` values are unique across the file.
- **[owner-review]** Two entries verbatim: `plot-ship-sinks` expected `["Titanic"]` and `plot-mob-family` expected `["The Godfather"]`.

**Lifecycle**: immutable during a run.

---

## Entity: `CaseTemplate`

**Source**: committed file `MovieTracker.Evals/eval-movie-templates.json`. Read-only. See `contracts/eval-movie-templates.schema.json`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | Stable across runs. Unique across the file. Used as the prefix of every generated case id. |
| `template` | string | conditional | Question with placeholders — present for single-turn templates. |
| `turns` | `string[]` | conditional | Ordered turn texts — present for multi-turn templates (currently only `followUpDirector`). Mutually exclusive with `template`. |
| `check` | enum `TemplateCheck` | yes | The check kind that grades this template's case. One of `director`, `actorFilmography`, `identifyByPlot`, `genreDecade`, `rating`, `trailer`, `followUpDirector`, `funnyFactNotEmpty`. |

**File-shape invariants**:

- **[loader]** `id` values are unique across the file.
- **[owner-review]** Exactly eight entries, one per `TemplateCheck` value, with the exact question strings spec FR-005 names.
- **[owner-review]** Exactly one of `template` or `turns` is present per entry; multi-turn entries use `turns` and single-turn entries use `template`.

**Lifecycle**: immutable during a run.

---

## Entity: `RunTitle`

**Source**: run-time; selected per region from `GET /movie/popular?region=<CC>&page=<n>` where `CC ∈ {US, IN, SE}` and `n` is a random integer in `1..5` picked independently per region. Not persisted.

| Field | Type | Required | Notes |
|---|---|---|---|
| `tmdbId` | int | yes | TMDb movie id. |
| `title` | string | yes | Substituted into template placeholders. |
| `region` | enum `{US, IN, SE}` | yes | Tagged on the "Popular titles for this run" output line. |
| `slot` | int `1..3` | yes | 1-based slot in the fixed regional order US, IN, SE. |

**Validation**:

- Exactly three `RunTitle` values per run (one per region).
- `tmdbId` unique across the three; on a collision the harness resamples one title from the already-chosen page for the affected region (no page reroll; research.md Decision 4).
- Any region returning non-2xx, unparseable payload, or zero selectable entries on the chosen page triggers a **precondition failure** that halts the run.

---

## Entity: `GeneratedCase`

**Source**: built in memory each run; **never persisted** (FR-008).

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | `"<templateId>-<slot>"` where `slot` is `1..3`. |
| `question` | string | yes | Single-turn question with placeholders filled, OR the first turn text for multi-turn cases (the second turn is held in `turns[1]`). |
| `turns` | `string[]?` | conditional | Present only for multi-turn cases; holds the full ordered turn texts (`turns[0]` is the first turn, `turns[1]` the second). |
| `check` | enum `TemplateCheck` | yes | Copied from the owning template. |
| `expected` | typed per-check record | conditional | The typed expected-data payload for this case. Null only when `noTruthValue == true` or when the check does not need a truth value (`funnyFactNotEmpty`). Serialized to JSON for `EvalItem.ExpectedOutput`. |
| `noTruthValue` | bool | yes | True when a truth lookup returned no usable value for a case the template still produced (FR-016). Mutually exclusive with running the case through an evaluator. |

**Per-check `expected` record shapes** (research.md Decision 2):

- `director` / `followUpDirector`: `{ DirectorName: string }`.
- `actorFilmography`: `{ ActorName: string, FilmographyIds: int[], FilmographyTitles: string[] }` (title list kept case-insensitive by convention).
- `identifyByPlot`: `{ SourceId: int, SourceTitle: string }`.
- `genreDecade`: `{ GenreId: int, GenreName: string, DecadeStart: int }`.
- `rating`: `{ ImdbRating: string }`.
- `trailer`: `{ VideoKeys: string[] }` (YouTube `v=` keys from TMDb videos filtered to `site == "YouTube"` AND `type == "Trailer"`).
- `funnyFactNotEmpty`: `null` (no truth value needed; the check reads only `EvalItem.Context.funnyFact`).

**Truth-resolution rules per `TemplateCheck`**:

- `director`: TMDb `/movie/{id}/credits` → first `crew[*]` with `job == "Director"` → `name`. Missing → `noTruthValue = true`.
- `actorFilmography`: TMDb `/movie/{id}/credits` → cast order 0 → actor's `id` + `name`, then TMDb `/person/{id}/movie_credits` → `cast[*].{id, title}`. Missing cast order 0 or empty credits → `noTruthValue = true`.
- `identifyByPlot`: TMDb `/movie/{id}?append_to_response=external_ids` → `overview` (with the movie's title words removed, case-insensitive word-level strip) + `id` + `title`. Missing/empty overview → `noTruthValue = true`.
- `genreDecade`: TMDb `/movie/{id}?append_to_response=external_ids` → pick the first entry in `genres[]` (`{id, name}`) and the first-four of `release_date` → decade start `yyyy - (yyyy % 10)`. Missing genres or missing release_date → `noTruthValue = true`.
- `rating`: TMDb `/movie/{id}?append_to_response=external_ids` → `external_ids.imdb_id`, then OMDb `/?i=<imdbId>` → `imdbRating`. Missing IMDb id, missing rating, or `imdbRating == "N/A"` → `noTruthValue = true`.
- `trailer`: TMDb `/movie/{id}/videos` → `results[*]` filtered to `site == "YouTube"` AND `type == "Trailer"` → `key` set. **Empty set → no case produced** (qualification rule, not FR-016).
- `followUpDirector`: resolves truth the same way `director` does (first `crew[*]` with `job == "Director"`), applied to its own independently-selected run title (research.md Decision 13 — every title-based template picks a slot at random, so `followUpDirector`'s slot is not tied to `director`'s slot).
- `funnyFactNotEmpty`: no truth lookup.

**Validation**:

- Each template produces 0 or 1 case per run. `trailer` produces 0 when the selected movie has no YouTube trailer. Other templates always produce a case (possibly with `noTruthValue = true`).
- Unique `id` across the generated set.

**Lifecycle**: created after the regional popular selection, graded, discarded at process exit.

---

## Entity: `IEvalCheck`

**Source**: harness-defined interface implemented by one file per check kind (research.md Decision 2).

```
public interface IEvalCheck
{
    string Name { get; }

    Microsoft.Agents.AI.EvalCheckResult Evaluate(Microsoft.Agents.AI.EvalItem item);
}
```

Nine concrete implementations in nine files (one per check kind): `ListIncludesCheck`, `DirectorCheck`, `ActorFilmographyCheck`, `IdentifyByPlotCheck`, `GenreDecadeCheck`, `RatingCheck`, `TrailerCheck`, `FollowUpDirectorCheck`, `FunnyFactNotEmptyCheck`. Each `Evaluate` reads `item.Context` (parsed as `ActualResponsePayload`) and `item.ExpectedOutput` (parsed as that check's expected record type) and returns `new EvalCheckResult(passed, reason, Name)`.

---

## Entity: `ActualResponsePayload`

**Source**: built by the composition root after each chat response; serialized to JSON for `EvalItem.Context`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `funnyFact` | string? | no | The chat response's `funnyFact` field. The `funnyFactNotEmpty` check reads this directly. |
| `movieList` | `MovieListEntry[]` | yes | Projection of the chat response's `turn.movieList` to the fields any check kind needs. Empty when the agent returned no movies. |

**`MovieListEntry`** fields:

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | int | yes | TMDb id parsed from the `MovieViewModel.Id` string (the API-side type carries it as string; the harness coerces to int for matching). |
| `title` | string | yes | `MovieViewModel.Title`. |
| `releaseDate` | string? | no | `MovieViewModel.ReleaseDate` serialized to ISO date. |
| `genreIds` | `int[]` | yes | `MovieViewModel.GenreIds`. May be empty. |
| `genreNames` | `string[]` | yes | `MovieViewModel.GenreNames` if set; otherwise an empty array. |

---

## Entity: `EvalItem` (framework type)

**Source**: `Microsoft.Agents.AI.EvalItem` version 1.17.0.

| Member | Loaded from | Notes |
|---|---|---|
| `Query` | the case question | Set via the `EvalItem(string query, string response)` constructor; MAF does not use it for grading. |
| `Response` | `turn.text` on the chat response | The agent's answer text. |
| `Context` | JSON serialization of `ActualResponsePayload` | Checks deserialize this to read `funnyFact` and/or `movieList` without captured variables. |
| `ExpectedOutput` | JSON serialization of the case's per-check expected record (or `null` for `funnyFactNotEmpty`) | Checks deserialize to the expected record type. |

---

## Entity: `CaseResult`

**Source**: emitted by the composition root per case. Persisted as an element of `results/last-run.json`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | From the owning case. |
| `check` | string | yes | Stable check-kind name. |
| `passed` | bool | yes | True iff the case passed (as returned by the `BooleanMetric` for cases that ran through `LocalEvaluator`, or `false` for `noTruthValue` short-circuits and FR-015 chat-call failures). |
| `reason` | string | yes | Short human-readable one-liner; `"ok"` on pass. |

---

## Entity: `PerCheckCount`

**Source**: assembled at end-of-run.

| Field | Type | Required | Notes |
|---|---|---|---|
| `check` | string | yes | Check-kind name. |
| `passed` | int | yes | Pass count among produced cases of this check kind. |
| `total` | int | yes | Produced-case count for this check kind. |

Persisted in `results/last-run.json` → `perCheck[]`. Only check kinds that produced at least one case appear in the file; the per-check comparison printer walks the full known list of nine check kinds and renders `-` for sides that have no entry.

---

## Entity: `RunResult`

**Source**: assembled at end-of-run; persisted to `results/last-run.json`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `runAt` | string (ISO-8601 UTC) | yes | `DateTimeOffset.UtcNow.ToString("o")` at run start. |
| `total` | int | yes | Number of produced cases. |
| `passed` | int | yes | Count of `cases[*].passed == true`. |
| `perCheck` | `PerCheckCount[]` | yes | One entry per check kind that produced at least one case. |
| `cases` | `CaseResult[]` | yes | One entry per produced case. |

---

## Entity (ephemeral): `CheckComparison`

**Source**: computed in memory before writing the new file. Not persisted.

| Field | Type | Required | Notes |
|---|---|---|---|
| `check` | string | yes | Check-kind name, or the literal `"total"` for the overall row. |
| `previousPassed` | int? | no | Null when no previous file existed or when the check kind had no cases in the previous run. |
| `previousTotal` | int? | no | Same conditions. |
| `currentPassed` | int? | no | Null when the check kind has no cases in the current run. |
| `currentTotal` | int? | no | Same condition. |

The printer walks the fixed nine-check-kind order from research.md Decision 7 plus the `total` row, rendering `-` for null sides and `<passed>/<total>` otherwise.

---

## Cross-entity invariants

1. The union of plot-anchor and generated case ids in a run is unique (`CaseResult` validation).
2. Plot-anchor case ids are stable across runs; generated case ids share their template-id prefix but may map to different titles between runs.
3. The committed `eval-movie-cases.json` and `eval-movie-templates.json` files are never written to by the harness.
4. `perCheck` in the persisted `RunResult` covers every check kind that produced at least one case in this run and no others; the printer fills in `-` for absent check kinds.

---

## Why no `Program` / `HostingPivot` entity

The in-process API host is a transient resource. Its only output (chat sessions in Cosmos) is owned by the API and acknowledged in the spec Assumptions.
