# 011 Local Azure Config + Manual Eval Pipeline: Autonomous Prompt

Run in Claude CLI from `C:\projects\dev\movie-tracker-api`.
The owner wants this done end to end WITHOUT asking him anything, until the PR is approved by Codex on GitHub.
Only stop early for the hard stops listed at the end.

Goal: the API and the eval harness get every setting automatically (Key Vault + `az login`) when not running in Azure,
so `dotnet run --project MovieTracker.Evals` works with no `set` commands. Plus a manual GitHub Actions eval workflow.

---

## Rules

- Follow `.specify/memory/constitution.md` and `CLAUDE.md` exactly. Use the Spec Kit flow (specify, plan, tasks, implement) without pausing for the owner.
- Git runs from Windows only (constitution). The owner allows this prompt to create the branch, commit, push and open the PR.
- Commit messages and PR text: no Claude attribution of any kind (no `Co-Authored-By`, no `Claude-Session`, no "Generated with Claude Code", no mention of Claude).
- Do not touch `.gitignore`. Do not merge the PR.
- Temp files only in `C:\projects\temp`. Delete them when done.
- Code style: explicit types (never `var`), braces on every `if`, no code comments, private fields plain camelCase (no `_`), one type per file, file-scoped namespaces.
- Do not add anything not listed in Scope.

## Scope

### 1. One credential factory (API)
- Running in Azure = environment variable `CONTAINER_APP_NAME` is set (the Container Apps platform sets it). Do not use the environment name: the Container App runs as `Development`.
- In Azure: `ManagedIdentityCredential` with `AZURE_CLIENT_ID` (same identity as today).
- Not in Azure: `AzureCliCredential` with `TenantId` from config `Azure:TenantId`.
- Use this credential for Cosmos (replace the current `DefaultAzureCredential`) and for Key Vault (section 2).

### 2. Key Vault configuration when not in Azure (API)
- Add the Azure Key Vault configuration provider only when not in Azure, with vault URI from config `KeyVault:Uri`.
- Map the existing secret names, without renaming anything in Key Vault:
  `azure-openai-key` -> `AzureOpenAI:ApiKey`, `themoviedb-api-key` -> `TheMovieDb:Api-Key`, `omdb-api-key` -> `OpenMovieDb:Api-Key`. Load only these three.

### 3. Non-secret local settings
- `MovieTracker.Api/appsettings.Development.json`: `Azure:TenantId` = `cf732866-97a2-4a44-8301-be829baafe11`, `KeyVault:Uri` = `https://movie-tracker-kv.vault.azure.net/`, `Cosmos:Endpoint` = `https://movie-tracker-cosmos.documents.azure.com:443/`.

### 4. App Insights optional (API)
- If `APPLICATIONINSIGHTS_CONNECTION_STRING` is empty, skip the Azure Monitor exporters instead of throwing. In Azure it is always set, so behavior there is unchanged.

### 5. Manual eval workflow
- New `.github/workflows/eval.yml`, trigger `workflow_dispatch` only.
- Steps: checkout, setup .NET 10, `azure/login@v2` with the same OIDC secrets as `infra-deploy.yml`, `dotnet run --project MovieTracker.Evals`, then write the console table to the job summary and upload `MovieTracker.Evals/results/last-run.json` as an artifact.
- In Bicep (`rbac.bicep` or `main.bicep`, following existing patterns): give the GitHub OIDC service principal `Key Vault Secrets User` on the vault and the Cosmos built-in data contributor role on the Cosmos account. Its principal id is a new parameter set in `demo.parameters.json`; look up the value with `az ad sp show --id <client id used by the workflow>`. If the value cannot be found, stop (hard stop).

### 6. Docs
- Constitution: MINOR amendment of Principle V: local development and the eval harness read secrets from Key Vault with the developer's `az login`; user-secrets are no longer used. Update the Sync Impact Report and version.
- `README.md` (Cosmos/local development section) and `CLAUDE.md` (Development Notes): local run is `az login` + `dotnet run`; `AZURE_CLIENT_ID` and `AZURE_TOKEN_CREDENTIALS` no longer matter locally.

## Out of scope
- Any change to endpoints, prompts, tools, JSON shapes or eval checks.
- Scheduled or PR-triggered eval runs. Renaming Key Vault secrets. Any other Bicep change.

---

## Steps (no pauses for the owner)

1. Create branch `011-local-azure-config` from an up-to-date `main`.
2. Spec Kit: `/speckit-specify` (feature number 11, short name `local-azure-config`), `/speckit-plan`, `/speckit-tasks`, `/speckit-implement`.
   After each stage: Codex review with `codex exec` (stdin closed: append `< /dev/null`). Guardrails: stay focused on this prompt, cut inflated or overkill work, keep it simple without losing anything in Scope. Fix and re-review until approved, max 6 rounds per stage.
3. Verify locally, in a fresh shell with NONE of these set: `AzureOpenAI__ApiKey`, `TheMovieDb__Api-Key`, `OpenMovieDb__Api-Key`, `Cosmos__Endpoint`, `APPLICATIONINSIGHTS_CONNECTION_STRING`:
   - `dotnet build movie-tracker-api.slnx`: no new warnings.
   - `dotnet run --project MovieTracker.Evals` runs end to end with no setup errors (no 503, no missing-key or credential errors).
   - "Score is good" means: no setup errors, and the total is not lower than the total in the committed `results/last-run.json` from before this branch. Record both totals.
   - If the score is not good, find out whether the cause is this change (fix it and re-run) or normal model variance (re-run once). Max 3 runs.
4. Commit (keep `results/last-run.json` from the final good run), push, open a PR to `main`. PR text: what changed, why, before/after eval totals, local run instructions.
5. GitHub Codex review loop: request a Codex review on the PR (comment `@codex review`). Wait for it. Address every comment: fix in code, or reply with a short reason if no change is needed. Push, then request review again.
   Repeat until Codex approves or has no open comments, max 6 rounds. Make sure the `Validate Bicep` and `What-If` checks pass; a What-If showing an unexpected delete or replace must be fixed.
6. Stop and report to the owner: PR link, Codex rounds used, eval totals, anything left open.

## Hard stops (only reasons to contact the owner before step 6)
- A Codex stage or the PR loop is not approved after 6 rounds.
- The GitHub OIDC principal id cannot be found, or a role assignment needs a permission you do not have.
- A credential or Key Vault access error that a code change cannot fix.
- Anything that would need a change outside Scope.
