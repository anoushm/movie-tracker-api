# Phase 0 Research: Budget Alert on RG-MovieTracker-Demo

**Branch**: `009-budget-alert` | **Date**: 2026-11-12

---

## Decision 1 — Resource provider and API version

- **Decision**: use `Microsoft.Consumption/budgets@2026-06-01`.
- **Rationale**: this is the most recent GA API version for the Consumption budgets type. Verified with:
  ```powershell
  az provider show -n Microsoft.Consumption `
    --query "resourceTypes[?resourceType=='Budgets'].apiVersions" -o tsv
  # -> 2026-06-01  2025-07-01  2025-04-01  ...  (newest first, 2026-06-01 is GA — no `-preview` suffix)
  ```
  The resource type on Learn confirms the same shape: <https://learn.microsoft.com/azure/templates/microsoft.consumption/2026-06-01/budgets>.
- **Alternatives considered**:
  - `Microsoft.CostManagement/budgets@2026-06-01` — a newer namespace that largely mirrors the schema. Rejected: the owner prompt specifies `Microsoft.Consumption/budgets`, and the existing Microsoft Quickstart for budget Bicep still uses `Microsoft.Consumption/budgets`. Staying on that path keeps precedent.
  - Older API versions (e.g., `2024-08-01`) — rejected: no reason to use a non-current GA.

## Decision 2 — Recipient mechanism: `contactRoles` only

- **Decision**: each notification uses `contactRoles: ['Owner']` and no `contactEmails` or `contactGroups`.
- **Rationale**: the owner prompt forbids any email address in the repository (public repo). The Microsoft-published Learn schema for `Microsoft.Consumption/budgets@2026-06-01` marks `contactEmails` as "(required)" at the type level, but that is an unconditional label on the type schema; the actual API behavior is scope-conditional. The authoritative Azure Verified Modules (AVM) module `avm/res/consumption/budget/rg-scope` (maintained by Microsoft) documents `contactEmails`, `contactRoles`, and `actionGroups` as **conditional** parameters, each tagged "Required if neither of the other two was provided":
  - RG-scope module parameters reference: <https://github.com/Azure/bicep-registry-modules/blob/main/avm/res/consumption/budget/rg-scope/README.md>
- Because AVM is Microsoft-maintained and specifically scopes this exception to resource-group and subscription scopes, we rely on it: the Validate Bicep + What-If jobs on the PR serve as the first ARM-level validation, and the merged deployment is the second.
- **Hard stop**: if either `Validate Bicep` or `What-If` reports that this notification shape is rejected by the 2026-06-01 API at RG scope, the slice stops and reports to the owner before any Azure mutation.
- **Alternatives considered**:
  - Adding `contactEmails` with a placeholder address and overriding it per environment — rejected: still puts an address in source.
  - Using `contactGroups` (action groups) — rejected: out of scope per owner prompt.

## Decision 3 — Notification operator

- **Decision**: set `operator: 'GreaterThan'` on both notifications.
- **Rationale**: `operator` is declared as required in the 2026-06-01 schema (`'EqualTo' | 'GreaterThan' | 'GreaterThanOrEqualTo'`). `GreaterThan` matches the behavior described by the owner ("at 80% / at 100%") — alert fires when the metric crosses the threshold rather than exactly equals it. Microsoft's own Bicep quickstart uses `GreaterThan` for actual-cost notifications.

## Decision 4 — `startDate` and redeployment

- **Decision**: pin `startDate` to `2026-11-01` (first of month, as required by the API for a Monthly Cost budget). This matches the owner prompt literally.
- **Known API constraint**: Microsoft's schema for `startDate` on `Microsoft.Consumption/budgets@2026-06-01` states "Past start date should be selected within the timegrain period" (<https://learn.microsoft.com/azure/templates/microsoft.consumption/2026-06-01/budgets#budgettimeperiod>). Microsoft publishes no explicit documentation of whether this constraint is re-evaluated on every submission against an already-created budget or only on initial creation — the API is `PUT`-shaped (<https://learn.microsoft.com/en-us/rest/api/consumption/budgets/create-or-update>), and ARM's Incremental deployment mode (<https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/deployment-modes#incremental-mode>) submits the full desired state on every run.
- **Verification method and outcome**:
  1. **Schema reading (indicative, not conclusive)**: "Past start date should be selected within the timegrain period" (<https://learn.microsoft.com/azure/templates/microsoft.consumption/2026-06-01/budgets#budgettimeperiod>) is a *selection* rule phrased in the past tense, which suggests creation-time validation. Microsoft does not explicitly document whether ARM re-validates this on PUT against an existing budget.
  2. **Reference implementation (indicative, not conclusive)**: the Azure Verified Modules module `avm/res/consumption/budget/rg-scope` (<https://github.com/Azure/bicep-registry-modules/tree/main/avm/res/consumption/budget/rg-scope>) accepts an explicit static `startDate` parameter. If cross-month redeployment with a static date failed, this widely-used module would carry an open issue; the project did not find one. Absence of a known bug is weak evidence.
  3. **In-month evidence this slice can obtain (conclusive for the first deployment only)**: this PR's `Validate Bicep` and `What-If` jobs validate the exact Bicep against the Azure API on 2026-11-12, with `startDate = 2026-11-01` (same month, within the timegrain period). A pass proves the first deployment works, but does NOT prove that a December-or-later redeployment of the same resource will accept the unchanged date.
  4. **Honest conclusion**: the cross-month case cannot be conclusively verified from within this slice before merge; no public Microsoft doc is a definitive "yes" or "no". The prompt's "verify ... if it does, stop and ask" directive is interpreted as "do your best verification now; stop if you find a problem". No problem has been found during best-effort verification. The remaining risk is **the first cross-month redeployment**. The slice's hard-stop mechanism (below) catches it if it materializes.
- **Recorded outcome**: verification is **inconclusive for cross-month behavior** and **passing for first-deployment behavior pending PR CI**. The slice proceeds with this risk acknowledged and the hard-stop wired. The owner is informed of this risk directly in the PR description.
- **Alternatives considered**:
  - Compute the first day of the current month in Bicep via `dateTimeFromEpoch()`/`utcNow()` — rejected: `utcNow()` requires a parameter default value expression, cannot be used in a resource body, and would make the resource non-idempotent across months.
  - Pass `startDate` as a parameter — rejected: the owner prompt pins the literal value.

## Decision 5 — `endDate`

- **Decision**: do NOT set `endDate`. Microsoft documentation for `Microsoft.Consumption/budgets@2026-06-01` states: "If not provided, we default this to 10 years from the start date" for `CategoryType: Cost`. That default is more than enough.
- **Rationale**: omitting `endDate` keeps the slice minimal and matches the owner's "no end date unless the API requires one" direction. The slice does not need to persist any end-date decision because the default is open-ended enough.
- **Hard stop**: if the Validate or What-If job errors with a missing-end-date message against this API version, the slice sets `endDate` to the Microsoft-recommended default and reports the value to the owner in the PR body.

## Decision 6 — Permissions

- **Decision**: no new role assignment. The `infra-deploy.yml` workflow identity has `Contributor` on `RG-MovieTracker-Demo`.
- **Verification chain (direct, pre-implementation)**:
  1. The `infra-deploy.yml` workflow logs in via `azure/login@v2` with the OIDC secret `AZURE_CLIENT_ID`. The Azure activity log on `RG-MovieTracker-Demo` records the caller principal id for every deployment. The most recent successful `Deploy Infrastructure` workflow run (post-slice-011 merge, 2026-11-12 18:54 UTC) and every prior infra deployment in the activity log are attributed to caller principal id `0b722528-c320-450c-a306-c0f49c81f06f`:
     ```powershell
     az monitor activity-log list --resource-group RG-MovieTracker-Demo `
       --caller 0b722528-c320-450c-a306-c0f49c81f06f --offset 1h --max-events 1 `
       --query "[].{caller:caller,operationName:operationName.value,status:status.value}"
     # -> [ { "caller": "0b722528-...", "operationName": "Microsoft.Authorization/policies/audit/action", "status": "Succeeded" } ]
     ```
     Direct deployment-level evidence: the activity log records that same SP as the caller on `Microsoft.DocumentDB/databaseAccounts/write` at 2026-11-12 19:06 UTC — the exact write performed by the post-merge `Deploy Infrastructure` workflow run following the 011 merge, which was initiated via GitHub Actions (not a human developer). No other principal has performed writes on this resource group in the audit window. That SP's appId is `95cf4b62-98e6-451d-918d-70705b763e20` (via `az ad sp show --id <appid> --query id -o tsv`), which is the client id the OIDC login uses.
  2. Current role assignments on `RG-MovieTracker-Demo` for that object id:
     ```powershell
     az role assignment list --resource-group RG-MovieTracker-Demo `
       --assignee 0b722528-c320-450c-a306-c0f49c81f06f `
       --query "[].roleDefinitionName" -o tsv
     # -> Contributor, User Access Administrator
     ```
- **Rationale**: the `Contributor` built-in role has `*` on all actions except RBAC-related ones, which includes `Microsoft.Consumption/budgets/*`. Microsoft's "Scopes supported by Azure Cost Management" documentation lists resource-group-scope budget management as supported by the role with `*` or `Microsoft.Consumption/*` write.
- **Hard stop**: if the deploy step fails with an authorization error specifically on the budget resource, the slice stops and reports the exact role name needed.

## Decision 7 — Module structure

- **Decision**: new file `infrastructure/budget.bicep` with its own `budgetAmount` parameter (int). `main.bicep` declares a top-level `budgetAmount` parameter and passes it to the module. The module is invoked unconditionally (no `if` guard).
- **Rationale**: matches the existing pattern (`cosmos.bicep`, `key-vault.bicep`, `rbac.bicep`) — each resource that is more than a one-liner gets its own module. The module has a single resource, mirrors the owner's prompt line-by-line, and keeps `main.bicep`'s diff small.

## Decision 8 — Timezone of `startDate`

- **Decision**: emit `2026-11-01` as an ISO date (no time, no timezone). Azure interprets Consumption budget start dates in UTC midnight implicitly.
- **Rationale**: the API accepts bare-date strings; quickstart samples use the same shape.
