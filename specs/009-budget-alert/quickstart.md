# Quickstart: Budget Alert on RG-MovieTracker-Demo

**Branch**: `009-budget-alert`

---

## Prerequisites

- Azure CLI installed, `az login` to tenant `cf732866-97a2-4a44-8301-be829baafe11`.
- The branch `009-budget-alert` is checked out.

## Local verification

```powershell
# 1. Build (no new warnings expected)
az bicep build --file .\infrastructure\main.bicep
```

**Expected**: `az bicep build` succeeds with no **new** warnings relative to `origin/main`.

## PR verification (on GitHub, before merge)

- `Validate Bicep` job: green.
- `What-If` job: `movie-tracker-budget` appears as the only `Create` entry; no unexpected delete or replace on any existing resource.

## Post-merge verification (Azure control plane)

```powershell
az consumption budget show `
  --budget-name movie-tracker-budget `
  --resource-group RG-MovieTracker-Demo `
  --query "{amount:amount, timeGrain:timeGrain, startDate:timePeriod.startDate, endDate:timePeriod.endDate, notifications:notifications}" `
  -o jsonc
```

**Expected**:
- `amount = 50` (demo), `timeGrain = Monthly`, `startDate = 2026-11-01T00:00:00+00:00`.
- Two notifications:
  - `alertActual80`: `threshold = 80`, `thresholdType = Actual`, `operator = GreaterThan`, `contactRoles = ['Owner']`, no email addresses (`contactEmails` is null, absent, or empty), `enabled = true`.
  - `alertForecast100`: `threshold = 100`, `thresholdType = Forecasted`, `operator = GreaterThan`, `contactRoles = ['Owner']`, no email addresses (`contactEmails` is null, absent, or empty), `enabled = true`.


## Known limitations (documented, not blocking)

- A fresh budget starts tracking with the current month's cumulative cost; prior-month data is not back-filled.
- If every `Owner`-role assignment on the resource group is a service principal with no email, Cost Management has no human recipient for the notification. This is a role-assignment hygiene issue outside the slice's scope.
