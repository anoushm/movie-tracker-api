# Contract: `infrastructure/budget.bicep`

**Branch**: `009-budget-alert`

Behavioral contract for the new Bicep module. The module takes one parameter, declares one resource, and emits no output.

---

## Module signature

```text
module budget 'budget.bicep' = {
  name: 'budget-deployment'
  params: {
    budgetAmount: budgetAmount
  }
}
```

(invoked from `main.bicep`, unconditional, no `if` guard)

## Parameters

| Name | Type | Required | Notes |
|---|---|---|---|
| `budgetAmount` | `int` | yes | Monthly cost budget amount in the subscription's default currency. No Bicep-level validation. |

## Declared resource

```text
resource movieTrackerBudget 'Microsoft.Consumption/budgets@2026-06-01' = {
  name: 'movie-tracker-budget'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-11-01'
    }
    notifications: {
      alertActual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactRoles: [
          'Owner'
        ]
      }
      alertForecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactRoles: [
          'Owner'
        ]
      }
    }
  }
}
```

The module declaration uses the `@description` decorator on its single parameter and NO other comments (per owner rule).

## Outputs

None. Nothing in `main.bicep` consumes the budget's id or name.

## Observable outcomes

| Pre-condition | Expected outcome |
|---|---|
| Deployed for the first time with `budgetAmount: 50` | Resource `movie-tracker-budget` exists on `RG-MovieTracker-Demo` with both notifications. |
| Redeployed in the same month with the same `budgetAmount` | `NoChange` in What-If. |
| Redeployed in the same month with a changed `budgetAmount` | `Modify` in place is expected; actual observation deferred to the first deployment that bumps the value. |
| Redeployed in a later month with the fixed past `startDate` | Not conclusively verified by this slice (see `research.md` Decision 4). Hard-stop wired. |
| `budgetAmount <= 0` | Not investigated by this slice; the demo value is `50` and a zero or negative value can only be introduced by editing the parameters file. |

## Forbidden

- No `contactEmails` anywhere in the module.
- No `contactGroups` (action groups).
- No `filter` (no resource/meter/tag scoping; the budget covers the whole RG).
- No `endDate` by default (defaults to 10 years from `startDate`, per research.md Decision 5). The owner-approved exception applies only if the API refuses to accept the resource without one; in that case the implementation uses Microsoft's recommended default and reports the value to the owner in the PR body.
- No output.
