# Contract: `.github/workflows/eval.yml`

**Branch**: `011-local-azure-config`

The new workflow has exactly one trigger and exactly one job. Its invariants:

## Trigger

```yaml
on:
  workflow_dispatch:
```

No `push`, `pull_request`, `schedule`, or any other trigger is allowed on this file.

## Permissions

```yaml
permissions:
  id-token: write
  contents: read
```

## Required steps (in order)

| # | Purpose | Action / command |
|---|---|---|
| 1 | Checkout | `actions/checkout@v4` |
| 2 | Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: 10.x` |
| 3 | Azure login | `azure/login@v2` with `client-id`, `tenant-id`, `subscription-id` from the same secrets as `infra-deploy.yml` (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) |
| 4 | Run evals | `dotnet run --project MovieTracker.Evals --configuration Release`. The workflow declares workflow-level `env:` entries for `Azure__TenantId`, `KeyVault__Uri`, and `Cosmos__Endpoint` (only these three; the remaining non-secret settings already live in committed `appsettings.json`). The harness then has every value the local `appsettings.Development.json` would have supplied. |
| 5 | Job summary | Append the captured stdout (the eval table + totals) to `$GITHUB_STEP_SUMMARY` inside a `## Eval` block |
| 6 | Artifact | `actions/upload-artifact@v4` uploading `MovieTracker.Evals/results/last-run.json` as `eval-last-run` |

## Secrets used

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

No new repository secret is introduced.

## Forbidden

- Any extra trigger
- Any modification to `infra-deploy.yml`
- Any post-run cleanup touching Azure resources
- Any run that happens without `workflow_dispatch`
