# 008 Eval Harness: Demo Eval Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api` on branch `008-eval-harness`.
This prompt replaces the current 008 harness design. Where it conflicts with the current 008 documents, this prompt wins.

Goal: a small, easy-to-explain demo eval. One check per area, about 10 questions per run, each asked once,
graded by code from TMDb/OMDb truth. No LLM in the harness. Clean MAF (Microsoft Agent Framework) usage.

---

## Rules for every stage

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly.
- Work only in `MovieTracker.Evals/` and `specs/008-eval-harness/`. No change to `MovieTracker.Api/`, other specs, infrastructure or workflows.
- Do not add anything not listed in Scope. If something extra seems needed, stop and ask the owner in one line.
- No commit, no push, no git commands. The owner commits.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`),
  `I` prefix on interfaces, `Async` suffix, one type per file, file name = type name, file-scoped namespaces, flat folder (only `results/`).

---

## Scope

### 1. Checks (one per area)

Each run picks 3 movies: 1 random title from TMDb `movie/popular` for each region `US`, `IN`, `SE` (random page 1 to 5, no duplicates).
Each title-based check uses one of these 3 movies. People and genres come from those movies' TMDb data.

| Area | Check | Question | Pass when |
|---|---|---|---|
| People | `director` | `Who directed {title}?` | answer contains the TMDb `Director` |
| Filmography | `actorFilmography` | `Show me movies starring {actor}` ({actor} = cast `order` 0) | `movieList` is not empty and every movie in it is in that actor's TMDb acting credits |
| Story | `identifyByPlot` | `Which movie is this: {overview with the title words removed}` | the source movie is in the answer |
| Story | `listIncludes` (hand-written) | the 2 existing anchors (Titanic, The Godfather) | every expected title is in the answer |
| Dates and genres | `genreDecade` | `Show me {genre} movies from the {decade}s` (from one run movie) | `movieList` is not empty and every movie has that genre and a release date in that decade |
| Ratings | `rating` | `What is the IMDb rating of {title}?` | answer contains the OMDb `imdbRating` |
| Trailers | `trailer` | `Show me the trailer for {title}.` | the YouTube URL inside `[TRAILER]...[/TRAILER]` has a video key from the movie's TMDb YouTube trailers; no case if the movie has none |
| Multi-turn | `followUpDirector` | turn 1 `Tell me about {title}`, turn 2 `Who directed it?` (same chat) | turn 2 answer contains the TMDb `Director` |
| Other | `funnyFactNotEmpty` | `Tell me a fun fact about {title}.` | `funnyFact` is not null or empty |

- "The movie is in the answer": first by TMDb id in `turn.movieList`, then by title text (case-insensitive) as fallback.
- Names and ratings are checked in the answer text (case-insensitive).
- A missing truth value: FAIL with reason `no truth value`, no chat call.
- Remove every other check and template (`leadActor`, `releaseYear`, `trailerFormat`, `asksClarification`, `plotSearch`).
- Templates live in `eval-movie-templates.json` (multi-turn templates hold a list of turns). The 2 anchors stay in `eval-movie-cases.json`.

### 2. Run size

- Each check runs once per run: 8 template cases + 2 anchors = about 10 questions.
- 1 repetition: each case runs once, in a new chat session. Remove the 2-repetition logic.

### 3. MAF (Microsoft Agent Framework) wiring

- One check per file: an `IEvalCheck` with a name and `EvalCheckResult Evaluate(EvalItem item)`. Adding a check = one new file plus its template.
- Group the run's items by check. For each check, one `LocalEvaluator` with that check's `FunctionEvaluator`, evaluating all its items in one call.
- Read pass/fail and reason from the results MAF returns. No captured variables.
- Carry each case's expected data as typed JSON in `EvalItem.ExpectedOutput`; `funnyFact` stays in `EvalItem.Context`. No `|`-separated strings.
- Remove `EvalCheckRegistry` and `CheckEvaluator` in their current form.
- Verify every MAF API used against `Microsoft.Agents.AI` 1.17.0. If something does not exist as described, stop and report.

### 4. Output and comparison

- Keep the table, the totals and the per-check comparison with the previous run (existing).

### 5. Documents

- Update `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md` and `tasks.md` to match this prompt.

## Out of scope

- LLM grader, CI, dashboards, new Azure resources, command-line options, retries, parallel runs, a random seed.
- Any change to the API or to how the API answers.
- Fixing failing checks.

---

## Steps

1. Update `spec.md`. Codex review.
2. Update `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`. Codex review.
3. Regenerate `tasks.md` with `/speckit-tasks`. Codex review.
4. **Stop. Show the owner a short summary of the tasks and wait for approval before implementing.**
5. After approval: `/speckit-implement`. Codex review.
6. Verify (see "Done when"), then stop. Do not run the harness with real keys; the owner does the live runs.

## Codex cross-review

After steps 1, 2, 3 and 5:

- Run `codex exec` with stdin closed (append `< /dev/null`).
- Guardrails for Codex: stay focused on this prompt, cut inflated or overkill work, keep it simple and easy to explain in a demo, without losing any check in Scope.
- Fix the findings and review again. Repeat until Codex approves, up to 6 rounds.
- If round 6 is not approved, stop and report the open findings to the owner.

## Done when

- `dotnet build movie-tracker-api.slnx` succeeds with no new warnings.
- The 8 checks in section 1 (plus `listIncludes` for the anchors) each exist as their own `IEvalCheck` file; no other checks remain.
- One repetition per case.
- MAF results are read from `LocalEvaluator`; no captured variables, no `|`-separated expected data.
- Only `MovieTracker.Evals/` and `specs/008-eval-harness/` differ (plus the existing `.slnx` line).
- No `var`, no `_` field prefix, no code comments in `MovieTracker.Evals/`.
