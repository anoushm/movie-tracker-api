---

description: "Task list for slice 009: budget alert on RG-MovieTracker-Demo"
---

# Tasks: Budget Alert on RG-MovieTracker-Demo

**Input**: Design documents from `specs/009-budget-alert/`

**Prerequisites**: plan.md (substantively approved; see plan.md for Codex review history), spec.md (substantively approved), research.md, data-model.md, contracts/, quickstart.md

**Tests**: no new automated tests. Verification is `az bicep build` + the existing `Validate Bicep` and `What-If` jobs on the pull request + `az consumption budget show` after merge.

**Organization**: tasks are grouped by user story. US1 (80% actual), US2 (100% forecasted), and US3 (redeployment safety) are all P1 and all land through the same Bicep resource.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: Which user story this task maps to (US1, US2, US3)
- File paths are absolute (relative to repo root)

## Path Conventions

- Bicep: `infrastructure/`
- Docs: `infrastructure/README.md` + `specs/009-budget-alert/`

---

## Phase 1: Setup

- [ ] T001 Confirm the working tree on branch `009-budget-alert` only carries this slice's own files, the pending `specs/009-budget-alert/prompt.md` edit (stashed and popped earlier in the run), and the untracked `specs/run-011-010-009.md`. No change to `.gitignore`.

---

## Phase 2: Foundational (Bicep module)

**Purpose**: declare the budget resource in its own Bicep module. Every user story relies on this.

**⚠️ CRITICAL**: no user-story task can begin until this phase is complete.

- [ ] T002 Create `infrastructure/budget.bicep` per `specs/009-budget-alert/contracts/budget.bicep.md`: single `Microsoft.Consumption/budgets@2026-06-01` resource named `movie-tracker-budget`, `category: 'Cost'`, `timeGrain: 'Monthly'`, `amount: budgetAmount` (from a single `@description`-annotated parameter), `timePeriod.startDate: '2026-11-01'`, two notifications (`alertActual80` and `alertForecast100`) with `operator: 'GreaterThan'`, matching thresholds and threshold types, `contactRoles: ['Owner']`, `enabled: true`. No `endDate`, no `contactEmails`, no `contactGroups`, no `filter`, no output. No comments beyond `@description`.

---

## Phase 3: User Story 1 — 80% actual-cost alert (Priority: P1) 🎯

**Goal**: the 80%-threshold Actual notification exists on the deployed budget and targets `contactRoles: ['Owner']`.

**Independent Test**: reviewing `infrastructure/budget.bicep` shows the `alertActual80` block with `threshold: 80`, `thresholdType: 'Actual'`, `operator: 'GreaterThan'`, `contactRoles: ['Owner']`, `enabled: true`; the PR's `What-If` lists `movie-tracker-budget` as a `Create`; after merge, `az consumption budget show` confirms the notification is live with those properties.

### Implementation for User Story 1

- [ ] T003 [US1] (covered by T002) — verify the `alertActual80` block in `infrastructure/budget.bicep` matches the spec exactly. No additional file edit.

---

## Phase 4: User Story 2 — 100% forecasted-cost alert (Priority: P1)

**Goal**: the 100%-threshold Forecasted notification exists on the deployed budget and targets `contactRoles: ['Owner']`.

**Independent Test**: reviewing `infrastructure/budget.bicep` shows the `alertForecast100` block with `threshold: 100`, `thresholdType: 'Forecasted'`, `operator: 'GreaterThan'`, `contactRoles: ['Owner']`, `enabled: true`; after merge, `az consumption budget show` confirms both notifications are present.

### Implementation for User Story 2

- [ ] T004 [US2] (covered by T002) — verify the `alertForecast100` block in `infrastructure/budget.bicep` matches the spec exactly. No additional file edit.

---

## Phase 5: User Story 3 — Fixed past start date must not break redeployments (Priority: P1)

**Goal**: ship the slice with the known cross-month-redeployment risk explicitly recorded and the hard-stop wired. `Validate Bicep` and `What-If` on this PR are the concrete evidence for first-deployment behavior.

**Independent Test**: `specs/009-budget-alert/research.md` Decision 4 records the verification method and the honest outcome ("inconclusive for cross-month; wired hard-stop"); the PR's `Validate Bicep` passes and `What-If` shows the budget as `Create` with no delete or replace on any existing resource.

### Implementation for User Story 3

- [ ] T005 [US3] (covered by T002 and research.md Decision 4) — no additional edit; the slice's response to a cross-month redeployment failure is the hard-stop documented in research.md and the pull-request body.

---

## Phase 6: Wiring (`main.bicep`, parameters, README)

- [ ] T006 Update `infrastructure/main.bicep`: add a top-level `budgetAmount` (int) parameter with `@description('Monthly cost budget amount for RG-MovieTracker-Demo, denominated in the subscription currency')`. Immediately after the ACR/KV role assignment block (or at the natural spot matching existing module invocation order), invoke the new module: `module budget 'budget.bicep' = { name: 'budget-deployment' params: { budgetAmount: budgetAmount } }`. No guard. No other change. Depends on T002.
- [ ] T007 Update `infrastructure/demo.parameters.json`: add a new `budgetAmount` entry with `{ "value": 50 }`. Match the existing JSON style (two-space indent, trailing-comma rules). Depends on T006.
- [ ] T008 Update `infrastructure/README.md`: add one short entry under an appropriate heading describing the budget (resource name, amount parameter, two notifications, `Owner` recipient role, no email). Keep it to a single short paragraph plus a one-row table if the README already uses tables. Do NOT include any email address.

---

## Phase 7: Verification

- [ ] T009 Run `az bicep build --file infrastructure/main.bicep`; zero new warnings relative to `origin/main`.
- [ ] T010 Record for the PR body: (a) the exact `budgetAmount` value (`50`), (b) the two notifications shapes, (c) the known cross-month-redeploy risk and the hard-stop wiring, (d) the deploy-identity verification summary.
- [ ] T011 Delete any files created under `C:/projects/temp/` for this slice.

---

## Phase 8: Local Codex review + commit + PR + GitHub Codex loop

- [ ] T012 Run the local Codex cross-review on the implementation (post-code change): `codex exec -s read-only --skip-git-repo-check -C C:/projects/dev/movie-tracker-api ... < /dev/null`, same guardrails (stdin closed). Fix findings up to 6 rounds; re-run T009 after any code fix. Hard stop if round 6 is not approved.
- [ ] T013 Commit (no Claude attribution of any kind — no `Co-Authored-By`, no `Claude-Session`, no "Generated with Claude Code", no mention of Claude). Include only this slice's files (`infrastructure/budget.bicep`, `infrastructure/main.bicep`, `infrastructure/demo.parameters.json`, `infrastructure/README.md`, `specs/009-budget-alert/`). The pending edit to `specs/009-budget-alert/prompt.md` (stashed + popped earlier) is committed with this slice.
- [ ] T014 Push and open a pull request to `main`. PR body: what changed, why, the two eval-safety remarks (none in this slice; call out budget specifics instead), the known cross-month risk, and the deploy-identity note.
- [ ] T015 Verify on the PR: `Validate Bicep` passes; `What-If` shows `movie-tracker-budget` as the one and only `Create` on the slice's diff, with no unexpected delete or replace. If either job fails on the fixed past `startDate`, hard stop and escalate.
- [ ] T016 Run the GitHub Codex review loop: comment `@codex review`, wait, address every comment (fix code or reply with a short reason), push, request again. After any push, re-verify T015. Loop until Codex approves OR has no open comments; up to 6 rounds; hard stop at round 6.
- [ ] T017 Prepare the per-slice handoff line for the runner's final report: PR link, Codex rounds used (local + GitHub, including round counts that went past the cap for the spec and plan stages), items left open (the cross-month-redeploy observation).

---

## Runner-owned post-handoff checklist

These are performed by the runner per `specs/run-011-010-009.md` steps 7-8, NOT by this slice.

- [ ] R1 Merge the PR.
- [ ] R2 Wait until the workflows on `main` triggered by the merge finish green.
- [ ] R3 Post-merge, verify the budget resource with `az consumption budget show --budget-name movie-tracker-budget --resource-group RG-MovieTracker-Demo` and confirm all of: `amount = 50`, `timeGrain = Monthly`, `startDate = 2026-11-01`, `alertActual80` present (threshold 80, Actual, Owner role, enabled), `alertForecast100` present (threshold 100, Forecasted, Owner role, enabled), and no `contactEmails` populated on either notification.

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): no dependencies.
- Phase 2 (Foundational): depends on Phase 1.
- Phases 3-5 (user-story verification): depend on Phase 2; each is a code-read, not an edit.
- Phase 6 (Wiring): depends on Phase 2.
- Phase 7 (Verification): depends on Phase 6.
- Phase 8 (Review + ship): depends on Phase 7.

### Parallel Opportunities

- None — the slice is linear (one module, then wiring, then verify).

---

## Hard stops (escalate to the owner)

- `Validate Bicep` or `What-If` on the PR fails specifically on the fixed past `startDate`.
- The deploy service principal's role turns out to be insufficient (surfaces on merge-triggered `Deploy Infrastructure`).
- A post-merge redeployment in a future month fails with a past-date error.
- The local Codex review (T012) is not approved after round 6.
- The GitHub Codex loop (T016) still has unresolved findings after round 6.
- Any change that would go outside the slice's Scope.

---

## Notes

- `[P]` tasks: none in this slice (linear).
- No comments in Bicep beyond `@description`.
- Do NOT touch `.gitignore`.
- Temp files only under `C:/projects/temp`; T011 cleans them up.
