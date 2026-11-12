# 010 Eval Score Fixes: Fix Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api` on branch `010-eval-score-fixes` (the owner creates it from `main`).
Follow `.specify/memory/constitution.md` and `CLAUDE.md`. Keep it simple. Only the changes below.

Why: the first eval run scored 4 / 10. Analysis of the run log:

| Case | Root cause | Fix |
|---|---|---|
| `director` (The Fix) | Question had no year; the agent picked another movie with the same title | Kept on purpose (see Part A) |
| `followUpDirector` (Toy Story 5) | `DescribeMovie` loads credits but returns only cast, never the director; the agent had no way to find it | Part B |
| `plot-mob-family` | Question too vague ("mobster movies about a family") | Part A |
| `identifyByPlot`, `rating`, `trailer`, `funnyFact` questions | Title only, no year; same-title movies can be mixed up | Part A |

---

## Rules

- Change only the files named below.
- Do not change any other prompt text, tool, endpoint, JSON shape or check logic.
- No commit, no push, no git commands. The owner commits.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file.
- Do not run the eval harness with real keys; the owner does the live run.

## Part A: clearer eval questions (`MovieTracker.Evals/`)

1. Add a `{year}` placeholder: the release year of the run movie (from its TMDb `release_date`). If the movie has no release date, the case is `no truth value` (existing rule).
2. `eval-movie-templates.json`:

| Template | New text |
|---|---|
| `director` | unchanged: `Who directed {title}?` (kept without year on purpose, to keep testing same-title mix-ups) |
| `identifyByPlot` | `Which {year} movie is this: {overview}` |
| `rating` | `What is the IMDb rating of {title} ({year})?` |
| `trailer` | `Show me the trailer for {title} ({year}).` |
| `followUpDirector` turn 1 | `Tell me about {title} ({year})` (turn 2 unchanged) |
| `funnyFactNotEmpty` | `Tell me a fun fact about {title} ({year}).` |
| others | unchanged |

3. `eval-movie-cases.json`: change the `plot-mob-family` question to `Show me classic mobster movies about a crime family`. Expected stays `["The Godfather"]`.
4. Update the matching examples in `specs/008-eval-harness/` docs (`spec.md`, `data-model.md`, `quickstart.md`, `contracts/`) so they show the new question text.

## Part B: director in `DescribeMovie` (`MovieTracker.Api/Tools/TmdbTools.cs`)

1. `DescribeMovie` already loads credits (`MovieMethods.Credits`). Add a `Directors` field to its JSON: the names of crew members with job `Director`.
2. In the `[Description]` of `DescribeMovie`, add "director" to the list of what it returns (for example: "...top-billed cast, director(s), poster art..."). Change nothing else in that text.
3. No other change to the API.

## Codex cross-review

- Run `codex exec` with stdin closed (append `< /dev/null`).
- Guardrails: stay focused on Parts A and B, cut inflated or overkill work, keep it simple without losing any listed change.
- Fix the findings and review again. Repeat until Codex approves, up to 6 rounds. If round 6 is not approved, stop and report.

## Done when

- `dotnet build movie-tracker-api.slnx` succeeds with no new warnings.
- Templates and the anchor match the tables above; `{year}` is filled from the run movie.
- `DescribeMovie` returns `Directors`; its `[Description]` mentions the director.
- Only these change: `MovieTracker.Evals/` (templates, anchor, `{year}` support), `MovieTracker.Api/Tools/TmdbTools.cs`, and the 008 doc examples.
