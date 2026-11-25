# 012 Eval Cleanup: PR, Codex Review, Merge, Eval on main

Run in Claude CLI from `C:\projects\dev\movie-tracker-api`.
Branch `012-eval-cleanup` is already committed and pushed (`3c72e05`). Do not ask the owner anything except for the hard stops below.

## Rules

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly. Git and `gh` run from Windows only.
- Commit messages and PR text: no Claude attribution (no `Co-Authored-By`, no `Claude-Session`, no "Generated with Claude Code", no mention of Claude).
- Do not touch `.gitignore`. Do not commit this file (`pr-prompt.md`).
- Fixes stay inside the 012 scope (`specs/012-eval-cleanup/prompt.md`). Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces that match the folder.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Do not delete branches, workflow runs or anything else.

## Steps

1. PR: open a PR from `012-eval-cleanup` to `main`.
   Body: what changed (items 1 to 7 of `prompt.md`), why, and the eval totals: Nov 12 `6 / 10` -> Nov 18 `14 / 16 (2 skipped, both rating)`.
2. GitHub Codex review: comment `@codex review` and wait for the review.
   For every comment, either fix it in code or reply with a short reason when no change is needed. After fixes: `dotnet build movie-tracker-api.slnx` (no new warnings), then commit and push.
   If a fix changes `MovieTracker.Evals` behavior, rerun `dotnet run --project MovieTracker.Evals` locally and commit the new `results/last-run.json`.
   Request the review again (`@codex review`). Repeat until Codex approves with no open comments, up to 5 rounds.
3. All PR checks must pass.
4. Merge the PR using the same merge method as the earlier PRs in this repo. Wait until every workflow on `main` that the merge triggered finishes green.
5. Run the eval on `main`: `gh workflow run eval.yml --ref main`, then watch the run until it finishes.
6. Report to the owner: the PR link, the number of Codex rounds, the merge commit, the eval run link, and the totals line plus the per-check comparison from the run's job summary.

## Hard stops (report to the owner, then stop)

- Codex has not approved after 5 rounds.
- A PR check or a post-merge workflow on `main` fails, and a fix inside the 012 scope does not solve it.
- The eval workflow on `main` fails. Report the failing step and its error, and do not try to fix it.
