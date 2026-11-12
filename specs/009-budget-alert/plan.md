# Implementation Plan: Budget Alert on RG-MovieTracker-Demo

**Branch**: `009-budget-alert` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: [`specs/009-budget-alert/spec.md`](spec.md)

## Summary

Add a single `Microsoft.Consumption/budgets@2026-06-01` resource at the resource-group scope on `RG-MovieTracker-Demo` via a new Bicep module `infrastructure/budget.bicep`, invoked once from `main.bicep` with a new `budgetAmount` (int) parameter whose demo value is `50`. The budget is Monthly, Category `Cost`, starts `2026-11-01`, and declares two notifications — 80% Actual and 100% Forecasted — both with `contactRoles: ['Owner']`, `operator: 'GreaterThan'`, and `enabled: true`. No contact email, no action group, no role assignment, no automated remediation. `infrastructure/README.md` gets one short entry describing the budget. Nothing else changes.

## Technical Context

**Language/Version**: Bicep (ARM Bicep DSL), targeted at Azure Resource Manager with `az bicep build` from the existing CI runner image.

**Primary Dependencies**:
- Resource provider: `Microsoft.Consumption`
- Resource type: `budgets`
- API version: `2026-06-01` (latest GA, verified via `az provider show -n Microsoft.Consumption` — see research.md Decision 1).

**Storage**: none — the budget is an Azure control-plane resource; no project-side data is persisted.

**Testing**:
- `az bicep build --file infrastructure/main.bicep` (locally and in CI)
- The existing `Validate Bicep` and `What-If` GitHub Actions jobs on the PR.
- `az consumption budget show --budget-name movie-tracker-budget --resource-group RG-MovieTracker-Demo` after merge.

**Target Platform**: Azure Resource Manager (resource-group scope, one deployment, idempotent).

**Project Type**: Infrastructure (Bicep IaC) — no application code change in this slice.

**Performance Goals**: not applicable (control-plane resource; no per-request latency).

**Constraints**:
- `.gitignore` MUST NOT change.
- No role assignment, action group, Logic App, runbook, or subscription-level resource introduced.
- No email address anywhere in the repo.
- No change to any file outside `infrastructure/budget.bicep` (new), `infrastructure/main.bicep`, `infrastructure/demo.parameters.json`, `infrastructure/README.md`, `specs/009-budget-alert/`.
- No comments in Bicep beyond the existing `@description(...)` decorator style.
- `What-If` on the PR must NOT show any delete or replace on any existing resource.
- `startDate` is the first of a month (`2026-11-01`) — verified via Microsoft docs to be the required shape for a Monthly Cost budget.

**Scale/Scope**:
- One new Bicep module (≤ 40 lines).
- Two parameter additions in `main.bicep` (one at module-call time, one parameter definition).
- One value added to `demo.parameters.json`.
- One section added to `infrastructure/README.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Verdict |
|---|---|---|
| I. Managed Identity Over Secrets | No new identity, no new secret, no key. | PASS |
| II. Infrastructure Is Bicep, Not Clicks | The budget is declared in Bicep and deployed by the existing workflow. No portal step. No imperative `az`. | PASS |
| III. Incremental Migration With Declared Boundaries | Scope and Out-of-Scope are explicit in `spec.md`. The slice imports one Azure resource type and nothing else. | PASS |
| IV. Dependencies Prove Themselves | The budget has no new runtime dependency; `az consumption budget show` is the probe. No health endpoint is required because the API code is untouched. | PASS |
| V. No Secret Material In Source | No secret introduced. No email address committed. | PASS |

**Development Workflow — Codex cross-review loop**: every stage of this plan is reviewed by `codex exec`, up to 6 rounds each, before the pull request opens.

## Project Structure

### Documentation (this feature)

```text
specs/009-budget-alert/
├── prompt.md         # Owner prompt (already present)
├── spec.md           # Feature spec (approved substantively; final review iteration carried polish-only MUST FIX past round 6)
├── plan.md           # This file
├── research.md       # Phase 0 output
├── data-model.md     # Phase 1 output (configuration-only data)
├── quickstart.md     # Phase 1 output
├── contracts/        # Phase 1 output
│   └── budget.bicep.md
├── checklists/
│   └── requirements.md
└── tasks.md          # Phase 2 output (speckit-tasks)
```

### Source Code (repository root)

```text
infrastructure/
├── budget.bicep                 # New: Microsoft.Consumption/budgets@2026-06-01 (resource-group scope)
├── main.bicep                   # Updated: new budgetAmount param + module call
├── demo.parameters.json         # Updated: budgetAmount = 50
├── README.md                    # Updated: one short entry on the budget
└── (every other file unchanged)
```

**Structure Decision**: a new, standalone Bicep module `infrastructure/budget.bicep` keeps the resource declaration isolated, matches the existing pattern (`cosmos.bicep`, `key-vault.bicep`, `rbac.bicep`), and lets `main.bicep` wire it with a single `module` block.

## Complexity Tracking

> No constitution violations to justify.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | (none) | (none) |
