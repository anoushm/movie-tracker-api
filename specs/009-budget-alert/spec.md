# Feature Specification: Budget Alert on RG-MovieTracker-Demo

**Feature Branch**: `009-budget-alert`

**Created**: 2026-11-12

**Status**: Draft

**Input**: Owner prompt at `specs/009-budget-alert/prompt.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Owner is alerted when actual month-to-date spend hits 80% of the limit (Priority: P1)

The owner of resource group `RG-MovieTracker-Demo` receives an email automatically when actual accrued cost in the current month reaches 80% of a preset budget amount.

**Why this priority**: this is the slice's primary value — it stops cost surprises without the owner remembering to check the portal.

**Independent Test**: the PR's `What-If` lists `movie-tracker-budget` as a `Create`; reviewing `infrastructure/budget.bicep` shows the 80%-threshold, `Actual`-type notification with `contactRoles: ['Owner']` and `enabled: true`; after merge, `az consumption budget show` (or an equivalent read-only Azure CLI query) against the deployed resource confirms the notification is live with those exact properties.

**Acceptance Scenarios**:

1. **Given** the resource group has a budget configured with `Owner`-role contact and 80% actual-cost notification, **When** actual spend in the current month crosses 80% of the budget amount, **Then** Azure Cost Management sends an email to the email-capable `Owner`-role recipients on the resource group — with no email address in the repository.

---

### User Story 2 - Owner is warned when end-of-month forecast will exceed the limit (Priority: P1)

The owner receives a second email when the forecasted total monthly spend is expected to exceed 100% of the budget amount, before the overrun actually happens.

**Why this priority**: forecast-based alerts are the earliest reliable warning — they let the owner act before the month closes.

**Independent Test**: reviewing `infrastructure/budget.bicep` shows a second notification at 100% threshold, type `Forecasted`, `contactRoles: ['Owner']`, and `enabled: true`; after merge, `az consumption budget show` confirms both notifications are present on the deployed resource.

**Acceptance Scenarios**:

1. **Given** the deployed budget has a 100% forecasted-cost notification, **When** the month-end forecast computed by Azure Cost Management exceeds the budget amount, **Then** an email is sent to the email-capable `Owner`-role recipients on the resource group.

---

### User Story 3 - Fixed past start date must not break redeployments (Priority: P1)

The plan phase MUST verify that a fixed `startDate` of `2026-11-01` does not make redeployments of `main.bicep` fail. The outcome of that verification MUST be recorded in `research.md`. If the verification shows the fixed date would make redeployments fail, the slice stops and asks the owner.

**Why this priority**: a budget that only deploys cleanly on day one but breaks every subsequent run blocks every future Bicep change.

**Independent Test**: `research.md` records the verification method and outcome specifically for redeployment of an existing budget (separate from the initial-deployment evidence captured by `Validate Bicep` and `What-If` on this PR).

**Acceptance Scenarios**:

1. **Given** the recorded redeployment verification, **When** the owner (or any later slice) redeploys `main.bicep` after the merge, **Then** the Consumption API does not reject the unchanged `startDate`.

---

### Edge Cases

- `budgetAmount = 0` — the parameter is defined as `int` and the default in `demo.parameters.json` is `50`, so a zero value can only be reached by editing the parameters file; the API's handling of a zero amount is not investigated by this slice.
- An `Owner` role assignment on the resource group that refers to a service principal (no email) — Azure Cost Management's exact handling of service-principal contacts is not investigated by this slice; the slice's recipient mechanism is `contactRoles: ['Owner']`, and any email-capable user holding that role receives the alert.
- Historic cost data differs across months — the budget uses current-month cost only (time grain `Monthly`); comparisons to prior months are not part of this slice.
- The start date `2026-11-01` is the first of a month, as required by the Consumption-budgets API for a Monthly budget. The plan phase MUST verify in Microsoft docs that a fixed past start date is accepted on redeployment; the PR's `What-If` and `Validate Bicep` jobs are the gating evidence. If either rejects it, the slice stops and reports to the owner (hard stop).
- The plan phase MUST verify in Microsoft docs that the role currently held by the deploy service principal is sufficient to create budgets at resource-group scope. If verification shows otherwise, the slice stops and reports the exact required role to the owner; no new role assignment is added by this slice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new Bicep module MUST live at `infrastructure/budget.bicep` and MUST declare exactly one `Microsoft.Consumption/budgets` resource at resource-group scope using the latest stable API version supported by Azure. The slice MUST verify that API version and the resource schema against Microsoft's documentation before writing the module.
- **FR-002**: The budget resource name MUST be `movie-tracker-budget`.
- **FR-003**: `category` MUST be `Cost`; `timeGrain` MUST be `Monthly`.
- **FR-004**: `amount` MUST be sourced from a parameter `budgetAmount` (integer) on the module; the module MUST accept no other parameter for the amount.
- **FR-005**: `startDate` MUST be `2026-11-01` (ISO date, first-of-month, required by the Consumption API for a Monthly budget).
- **FR-006**: The budget MUST declare two notifications:
  - Notification 1: threshold `80`, threshold type `Actual`, `contactRoles: ['Owner']`, `enabled: true`.
  - Notification 2: threshold `100`, threshold type `Forecasted`, `contactRoles: ['Owner']`, `enabled: true`.
- **FR-007**: No email address MUST appear anywhere in the repository (not in the Bicep, not in the parameters file, not in docs). `contactRoles` is the only recipient mechanism.
- **FR-008**: The budget MUST NOT contain an `endDate` unless the API refuses to accept the resource without one; if the API requires one, the implementation MUST use the value Microsoft's documentation recommends and MUST report that value to the owner.
- **FR-009**: `infrastructure/main.bicep` MUST expose a new `budgetAmount` (integer) parameter and MUST invoke the new module exactly once at resource-group scope.
- **FR-010**: `infrastructure/demo.parameters.json` MUST set `budgetAmount` to `50`.
- **FR-011**: `infrastructure/README.md` MUST describe the budget in one short entry (purpose, amount, notifications, recipients) and MUST NOT include any email address.
- **FR-012**: The slice MUST confirm, **before deployment**, that the identity used by `.github/workflows/infra-deploy.yml` can create budgets at resource-group scope with its current role. If it cannot, the slice MUST stop and report the exact role the identity needs — and MUST NOT add a role assignment to compensate.
- **FR-013**: No new role assignment, action group, Logic App, runbook, or subscription-level resource MUST be introduced by this slice.
- **FR-014**: No file outside the following set MUST be touched: `infrastructure/budget.bicep` (new), `infrastructure/main.bicep`, `infrastructure/demo.parameters.json`, `infrastructure/README.md`, and `specs/009-budget-alert/`.
- **FR-015**: The budget resource MUST be alert-only — it MUST NOT trigger any automated action to stop, scale, pause, or delete any resource.

### Out of Scope

Carried verbatim from `prompt.md`:

- Stopping or scaling resources at a threshold (action groups, Logic Apps, runbooks).
- Subscription-level budgets, spending limits, Azure OpenAI TPM limits, AI gateway.
- Any change to `MovieTracker.Api/`, other Bicep resources, or workflow files.
- Edits to older specs (001 to 008).

### Key Entities

- **Budget**: `movie-tracker-budget` — a `Microsoft.Consumption/budgets` resource at resource-group scope on `RG-MovieTracker-Demo`. Attributes: `amount` (integer, subscription currency), `category`, `timeGrain`, `startDate`, two notifications.
- **Notification**: an object inside the budget resource with `threshold`, `thresholdType` (`Actual` or `Forecasted`), `contactRoles`, and `enabled`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `az bicep build --file infrastructure/main.bicep` succeeds with no new warnings on this slice.
- **SC-002**: The `Validate Bicep` job on this slice's pull request passes.
- **SC-003**: The `What-If` job on this slice's pull request shows `movie-tracker-budget` as the only `Create` and no unexpected delete or replace for any existing resource (per the owner prompt's "no unexpected delete or replace" rule, incidental in-place modifies on pre-existing drift are acceptable, matching the pattern seen on recent merged PRs).
- **SC-004**: After the merge workflow runs, the budget resource `movie-tracker-budget` exists on `RG-MovieTracker-Demo` with `amount = 50`, two notifications (80% Actual, 100% Forecasted), `contactRoles = ['Owner']`, and no `contactEmails`, verified with `az consumption budget show`.
- **SC-005**: `specs/009-budget-alert/research.md` records the verification method and outcome for a fixed past `startDate` on a Monthly Consumption budget.
- **SC-006**: The diff on the pull request touches only `infrastructure/budget.bicep`, `infrastructure/main.bicep`, `infrastructure/demo.parameters.json`, `infrastructure/README.md`, and `specs/009-budget-alert/`.

## Assumptions

- At least one email-capable user holds the `Owner` role on the resource group, so the alert reaches a human recipient.
- The Microsoft.Consumption/budgets API's current stable version is verified against Microsoft documentation during planning; if it requires an `endDate`, the implementation uses the recommended value and reports it in the PR.
- The budget amount is denominated in the subscription's default currency; this slice does not add a currency parameter.
- The role currently held by the deploy service principal on the resource group is verified during planning (per the edge case above) to be sufficient to manage budgets. The pre-deployment check is the source of truth; no claim is made here about which specific built-in role covers the permission. If the check fails, the slice stops and reports the exact role required.
