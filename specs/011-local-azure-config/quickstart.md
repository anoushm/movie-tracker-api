# Quickstart: Local Azure Config + Manual Eval Pipeline

**Branch**: `011-local-azure-config`

The two things to verify on this branch:

1. The eval harness runs end to end with just `az login` and no `set` commands.
2. The deployed Azure path is unchanged.

---

## Prerequisites

- .NET 10 SDK installed.
- Azure CLI installed and `az login` completed to the tenant `cf732866-97a2-4a44-8301-be829baafe11`.
- `MovieTracker.Api/appsettings.Development.json` has the three keys set by this slice:
  ```json
  {
    "Azure":    { "TenantId": "cf732866-97a2-4a44-8301-be829baafe11" },
    "KeyVault": { "Uri":      "https://movie-tracker-kv.vault.azure.net/" },
    "Cosmos":   { "Endpoint": "https://movie-tracker-cosmos.documents.azure.com:443/" }
  }
  ```
  See [`contracts/appsettings.schema.json`](contracts/appsettings.schema.json).
- NONE of the following set in the current shell: `AzureOpenAI__ApiKey`, `TheMovieDb__Api-Key`, `OpenMovieDb__Api-Key`, `Cosmos__Endpoint`, `APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Local verification

```powershell
# 1. Build (no new warnings expected)
dotnet build .\movie-tracker-api.slnx

# 2. Run the API and hit /health/cosmos (returns 200)
$api = Start-Job -ScriptBlock { dotnet run --project .\MovieTracker.Api --launch-profile http }
for ($i = 0; $i -lt 30; $i++) {
    try {
        $r = Invoke-WebRequest http://localhost:5065/health/ready -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { break }
    } catch { Start-Sleep -Seconds 1 }
}
Invoke-WebRequest http://localhost:5065/health/cosmos -UseBasicParsing
Stop-Job $api | Out-Null
Remove-Job $api | Out-Null

# 3. Capture the pre-branch baseline total from the committed copy at the branch point
$branchPoint = (git merge-base HEAD origin/main).Trim()
$baselineJson = git show "${branchPoint}:MovieTracker.Evals/results/last-run.json" | Out-String
$baseline = $baselineJson | ConvertFrom-Json
"Baseline at $($branchPoint.Substring(0,7)): $($baseline.passed) / $($baseline.total)"

# 4. Run the eval harness end to end (overwrites local last-run.json)
dotnet run --project .\MovieTracker.Evals

# 5. Capture the new total from the fresh working copy
$run = (Get-Content .\MovieTracker.Evals\results\last-run.json -Raw | ConvertFrom-Json)
"Run: $($run.passed) / $($run.total) — record both totals in the PR body"
```

**Expected**:
- Build: 0 new warnings.
- `/health/cosmos`: HTTP 200.
- Eval: console shows the full case table, totals, and writes `MovieTracker.Evals/results/last-run.json`.
- Eval total MUST meet or exceed the pre-branch baseline total committed in `results/last-run.json`.
  - If lower on first run: investigate whether this slice's changes caused it (fix and re-run) or if it's model variance (one re-run allowed). Max 3 runs.

## Azure-path verification (code read, no redeploy)

- Confirm in `MovieTracker.Api/Program.cs` that `AzureCredentialFactory.IsInAzure` gates:
  - the credential selection, and
  - the Key Vault configuration-provider registration.
- Confirm in `MovieTracker.Api/Telemetry/TelemetryRegistration.cs` that an empty `APPLICATIONINSIGHTS_CONNECTION_STRING` skips the three `AzureMonitor*Exporter` calls but keeps the OpenTelemetry pipelines registered.

## GitHub Actions eval workflow verification (post-merge)

1. In GitHub → Actions → "Eval" → **Run workflow**.
2. The run completes green.
3. The job summary contains the eval table and totals.
4. The run artifact `eval-last-run` contains `last-run.json`.

## Rollback

Revert the branch via GitHub UI. No data migration; no Key Vault secret is renamed; no Cosmos schema change.
