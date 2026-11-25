# 013 Chat Pipeline: Commit, Push, PR, Codex Review

Run in Claude CLI from `C:\projects\dev\movie-tracker-api`, on branch `013-chat-pipeline`.
Implementation is done and reviewed by the owner. Do not ask the owner anything except for the hard stops below.

## Rules

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly. Git and `gh` run from Windows only.
- Commit messages and PR text: no Claude attribution (no `Co-Authored-By`, no `Claude-Session`, no "Generated with Claude Code", no mention of Claude).
- The `.gitignore` change in the working tree is owner-approved. Commit it as it is. Do not edit it.
- Do not commit this file (`specs/013-chat-pipeline/pr-prompt.md`), `specs/012-eval-cleanup/pr-prompt.md`, or `movie-tracker notes.txt`.
- Fixes stay inside the 013 scope (`specs/013-chat-pipeline/prompt.md`). Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces that match the folder.
- Keep LF line endings on every file you touch.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Do not delete branches, workflow runs or anything else. Do not merge the PR.

## Steps

1. Commit: stage every change in `git status` except the three files excluded above. Commit message:
   `013: move chat answer-building into ChatPipeline; grade eval rating cases from TMDb top_rated`
2. Push `013-chat-pipeline` to `origin`.
3. PR: open a PR from `013-chat-pipeline` to `main`.
   Body: what changed (items 1 to 5 of `prompt.md`), why (groundwork for Option B), the 6 new files and 7 edited files, the log-category change (owner-accepted), and the eval totals: Nov 18 `14 / 16 (2 skipped, both rating)` -> final run `18 / 18 (0 skipped)`, with a note that rating passed 2/2 on all 4 runs and other drops varied by run (title randomness).
4. GitHub Codex review: comment `@codex review` and wait for the review.
   For every comment, either fix it in code or reply on the comment with a short reason when no change is needed. Answer every comment.
   After fixes: `dotnet build movie-tracker-api.slnx` (no new warnings compared with `main`), then commit and push.
   If a fix changes `MovieTracker.Evals` behavior or the chat answer path, rerun `dotnet run --project MovieTracker.Evals` locally and put the totals in the PR reply.
   Request the review again (`@codex review`). Repeat until Codex approves with no open comments, up to 5 rounds.
5. All PR checks must pass.
6. Report to the owner: the PR link, the number of Codex rounds, each comment with its fix or reply, the final commit, and the PR check status.

## Hard stops (report to the owner, then stop)

- Codex has not approved after 5 rounds.
- You disagree with a Codex comment and the reply does not settle it after one more round. Do not force a change to get approval.
- A Codex comment asks for a change outside the 013 scope.
- A PR check fails and a fix inside the 013 scope does not solve it.
