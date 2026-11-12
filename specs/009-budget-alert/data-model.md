# Phase 1 Data Model: Budget Alert on RG-MovieTracker-Demo

**Branch**: `009-budget-alert` | **Date**: 2026-11-12

No persistent data entity is added by this slice. The "entities" here are a single Azure resource and a single Bicep parameter.

---

## Azure resource

### `movie-tracker-budget` (`Microsoft.Consumption/budgets@2026-06-01`)

| Property | Value | Notes |
|---|---|---|
| `name` | `movie-tracker-budget` | Constant; passed to the resource declaration. |
| `scope` | resource group `RG-MovieTracker-Demo` | Set by invoking the module as a resource-group deployment from `main.bicep`. |
| `properties.category` | `Cost` | Required by prompt. |
| `properties.timeGrain` | `Monthly` | Required by prompt. |
| `properties.amount` | `budgetAmount` (int) | Sourced from the module parameter; `50` in demo. |
| `properties.timePeriod.startDate` | `2026-11-01` | First of month, as required by the API for a Monthly Cost budget. |
| `properties.timePeriod.endDate` | (omitted) | Defaults to 10 years from the start date (see research.md Decision 5). |
| `properties.notifications.alertActual80` | threshold `80`, thresholdType `Actual`, operator `GreaterThan`, contactRoles `['Owner']`, enabled `true` | Notification 1. |
| `properties.notifications.alertForecast100` | threshold `100`, thresholdType `Forecasted`, operator `GreaterThan`, contactRoles `['Owner']`, enabled `true` | Notification 2. |

No `contactEmails`, no `contactGroups`, no `filter`.

---

## Bicep parameter

### `budgetAmount` (declared in `main.bicep` and `budget.bicep`)

| Attribute | Value |
|---|---|
| Type | `int` |
| Required | yes (set in `demo.parameters.json`) |
| Validation | none at the Bicep level (the spec leaves zero-value API behavior uninvestigated) |
| Demo value | `50` (subscription currency) |
| Decorator | `@description('Monthly cost budget amount for RG-MovieTracker-Demo, denominated in the subscription currency')` |

---

## State transitions

- **First deployment**: the budget is created; the first notification evaluation happens on the next Cost Management refresh.
- **Subsequent deployments with unchanged parameters within the same month**: `NoChange` expected; the first observation happens the next time any infra PR touches this RG after merge.
- **Subsequent deployments with a changed `budgetAmount`**: expected to be a `Modify` in place; observable on the next deploy that bumps the value.
- **Subsequent deployments in a later month with the fixed past `startDate`**: cross-month behavior is not conclusively verifiable from within this slice (see research.md Decision 4); the slice's hard-stop catches a failure if it materializes.
- **No automated state machine lives in the repo**: the budget is alert-only.
