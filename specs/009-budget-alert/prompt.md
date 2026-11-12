# 009 Budget Alert: Spec Kit Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api` on branch `009-budget-alert`, created by the owner from an up-to-date `main` after 008 is merged.

Goal: a monthly cost budget on resource group `RG-MovieTracker-Demo` that emails the resource group Owner role (no email address in the repo; the repo is public).
It only alerts. It does not stop any resource or spending.

---

## Rules for every stage

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly (Constitution II: infrastructure is Bicep, deployed by the existing workflow).
- Feature folder is `specs/009-budget-alert/` (this file lives in it). Do not create any other spec folder.
- Do not add any resource, parameter, threshold, option or automation not listed in Scope. If something extra seems needed, stop and ask the owner in one line.
- No portal clicks and no imperative `az` changes to Azure. No deployment from the CLI session; the owner deploys through the workflow.
- No commit, no push, no git commands. The owner commits.
- Do not touch `.gitignore`.
- Temp files only in `C:\projects\temp`. Delete them when done.
- No comments in Bicep beyond the existing `@description` decorator style.

---

## Scope

### 1. Bicep module `infrastructure/budget.bicep`

One `Microsoft.Consumption/budgets` resource at resource group scope.
Verify the latest stable API version and its schema in Microsoft docs before writing it.

| Setting | Value |
|---|---|
| Name | `movie-tracker-budget` |
| Category | `Cost` |
| Time grain | `Monthly` |
| Amount | parameter `budgetAmount` |
| Start date | `2026-11-01` (required by the resource, must be the first day of a month) |
| Notification 1 | 80% of the budget, actual cost |
| Notification 2 | 100% of the budget, forecasted cost |
| Contact | `contactRoles: ['Owner']` on both notifications (no email address anywhere in the repo) |

- No end date unless the API requires one. If it does, use the value the docs recommend and report it.
- Verify that a fixed start date does not make redeployments fail. If it does, stop and ask.

### 2. Wiring

- Add a `budgetAmount` (int) parameter to `infrastructure/main.bicep` and call the module.
- Add the values to `infrastructure/demo.parameters.json`: `budgetAmount` = `50`.
- Update `infrastructure/README.md` with one short entry for the budget.

### 3. Permissions check

- Confirm the identity used by `.github/workflows/infra-deploy.yml` can create budgets at resource group scope with its current role.
  If it cannot, stop and report the exact role it needs. Do not add role assignments.

## Out of scope

- Stopping or scaling resources at a threshold (action groups, Logic Apps, runbooks).
- Subscription-level budgets, spending limits, Azure OpenAI TPM limits, AI gateway.
- Any change to `MovieTracker.Api/`, other Bicep resources, or workflow files.
- Edits to older specs (001 to 008).

---

## Steps

Each Spec Kit stage ends with a Codex cross-review (see the next section).

1. `/speckit-specify` using this file as the feature description.
   Create the feature with `-Number 9 -ShortName budget-alert -AllowExistingBranch` so it reuses branch `009-budget-alert` and `specs/009-budget-alert/`.
   The spec must list what this slice adds and the out-of-scope list above (Constitution III).
2. `/speckit-plan`
3. `/speckit-tasks`
4. `/speckit-implement`
5. Verify (see "Done when").

## Codex cross-review

After each of specify, plan, tasks and implement:

- Run a Codex review of that stage's output. Run `codex exec` with stdin closed (append `< /dev/null`) so it cannot hang waiting for input.
- Guardrails for Codex: stay focused on the budget alert, cut inflated or overkill work, keep it simple without losing any functionality in Scope.
- Fix the findings and run the review again. Repeat until Codex approves, up to 6 rounds.
- If round 6 is not approved, stop and report the open findings to the owner.

## Done when

- `az bicep build --file infrastructure/main.bicep` succeeds with no new warnings.
- The diff touches only `infrastructure/budget.bicep`, `infrastructure/main.bicep`, `infrastructure/demo.parameters.json`, `infrastructure/README.md` and `specs/009-budget-alert/`.
- After the owner pushes, the `Validate Bicep` and `What-If` workflow jobs pass, and What-If shows only the new budget being created (no unexpected delete or replace).
