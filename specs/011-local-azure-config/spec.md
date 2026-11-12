# Feature Specification: Local Azure Config + Manual Eval Pipeline

**Feature Branch**: `011-local-azure-config`

**Created**: 2026-11-12

**Status**: Draft

**Input**: Owner prompt at `specs/011-local-azure-config/prompt.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Local developer runs the eval harness with only `az login` (Priority: P1)

A developer with the Azure CLI already signed in runs `dotnet run --project MovieTracker.Evals` after this slice has populated the local `MovieTracker.Api/appsettings.Development.json`, and the harness completes an evaluation run without them exporting any secret environment variable, placing anything in user-secrets, or editing any other file.

**Why this priority**: this is the whole point of the slice. Every other change exists to make this one flow work. If it does not work, nothing else matters.

**Independent Test**: on a fresh shell with no `AzureOpenAI__ApiKey`, no `TheMovieDb__Api-Key`, no `OpenMovieDb__Api-Key`, no `Cosmos__Endpoint`, and no `APPLICATIONINSIGHTS_CONNECTION_STRING`, run `dotnet run --project MovieTracker.Evals` and the console shows the eval table and totals end to end.

**Acceptance Scenarios**:

1. **Given** the developer is signed in with `az login` to the correct tenant, **When** they run the eval harness from a fresh shell with none of the secret environment variables set, **Then** the harness fetches the OpenAI, TMDb, and OMDb keys from Key Vault, contacts Cosmos with that same identity, and prints the usual table and totals without any "missing configuration" or credential error.
2. **Given** the developer runs the API locally (`dotnet run --project MovieTracker.Api`), **When** they open `/health/cosmos`, **Then** the probe succeeds using the local Azure CLI identity.
3. **Given** the developer has not yet run `az login`, **When** they run either project, **Then** the credential chain fails with the Azure CLI error, which is the expected surface area for this condition.

---

### User Story 2 - Container App in Azure keeps working unchanged (Priority: P1)

The deployed `movie-tracker-api` Container App continues to reach Azure OpenAI, TMDb, OMDb, Cosmos, and Application Insights using its user-assigned managed identity. Nothing in production regresses.

**Why this priority**: production parity is the floor. A local-dev improvement that breaks the deployed app is a net loss.

**Independent Test**: inspect the composition root on this branch and confirm, by reading the code, that when `CONTAINER_APP_NAME` is set the managed-identity path is taken, the Key Vault provider is not registered, and the credential and telemetry call sites are the same ones that run in production today.

**Acceptance Scenarios**:

1. **Given** the Container App revision starts, **When** the runtime reads configuration, **Then** it uses the managed identity (selected via `AZURE_CLIENT_ID`) because `CONTAINER_APP_NAME` is set, it does NOT register the Key Vault configuration provider, and it does NOT need any local `appsettings.Development.json` value.
2. **Given** Application Insights is configured in Azure, **When** the app starts, **Then** telemetry exporters are registered with the connection string from the Container App env var exactly as before.

---

### User Story 3 - Owner triggers the eval from GitHub Actions (Priority: P1)

The owner (or any maintainer with write access) clicks "Run workflow" on a new "Eval" workflow and the harness runs end to end against the same Key Vault and Cosmos account, produces the console table in the job summary, and uploads `last-run.json` as a workflow artifact. The workflow never runs automatically.

**Why this priority**: the owner required the manual eval workflow as part of this slice (not a follow-up); it ships in 011.

**Independent Test**: after merge, go to Actions, pick "Eval", click "Run workflow", and verify the run succeeds, prints the per-check table in the job summary, and attaches `last-run.json` as an artifact.

**Acceptance Scenarios**:

1. **Given** the workflow is dispatched manually, **When** it runs, **Then** it signs in via the existing OIDC federated credentials, runs `dotnet run --project MovieTracker.Evals`, writes the table to `$GITHUB_STEP_SUMMARY`, and uploads `MovieTracker.Evals/results/last-run.json`.
2. **Given** a push or a pull request touches any file, **When** the push or pull request lands, **Then** the Eval workflow does NOT run.
3. **Given** the dispatched workflow signs in as the GitHub OIDC service principal, **When** the harness reads a Key Vault secret and a Cosmos item inside that run, **Then** both succeed because the identity holds `Key Vault Secrets User` on the vault and the built-in Cosmos data-contributor role on the Cosmos account.

---

### Edge Cases

- `CONTAINER_APP_NAME` unset locally but `AZURE_CLIENT_ID` set from a previous experiment: the local path must ignore the stale client id and use `AzureCliCredential` with the configured `Azure:TenantId` — the "am I in Azure?" switch is `CONTAINER_APP_NAME`, nothing else.
- `APPLICATIONINSIGHTS_CONNECTION_STRING` empty: the app registers the OpenTelemetry pipelines without the Azure Monitor exporters instead of throwing at startup.
- `Azure:TenantId` missing from configuration when running outside Azure: startup fails with a clear "missing configuration" message, matching the existing fail-fast pattern for Cosmos keys.
- `KeyVault:Uri` missing from configuration when running outside Azure: startup fails fast with a clear "missing configuration" message. In normal use, the local `appsettings.Development.json` populated by this slice keeps this value present.
- Key Vault access denied for the local principal: the Key Vault provider error is surfaced; the fix is role assignment, not a code change.
- A Key Vault secret name in scope is missing: the `.AddAzureKeyVault` provider handles that; a downstream "missing configuration" check surfaces the specific key.
- A `--what-if` on infrastructure must show only additive role-assignment changes, no deletes or replacements, on this slice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The API MUST select its Azure credential from a single factory: `ManagedIdentityCredential` with client id `AZURE_CLIENT_ID` when the environment variable `CONTAINER_APP_NAME` is set; `AzureCliCredential` with `TenantId` from configuration `Azure:TenantId` otherwise.
- **FR-002**: Both the Cosmos client and the Key Vault configuration provider MUST use the credential from FR-001; the current direct use of `DefaultAzureCredential` MUST be removed.
- **FR-003**: When not in Azure (FR-001), the API MUST register the Azure Key Vault configuration provider against the vault at configuration `KeyVault:Uri` and MUST load exactly these three secrets, mapped to these configuration keys: `azure-openai-key` -> `AzureOpenAI:ApiKey`, `themoviedb-api-key` -> `TheMovieDb:Api-Key`, `omdb-api-key` -> `OpenMovieDb:Api-Key`. No other secrets are loaded by the provider.
- **FR-004**: When running in Azure (FR-001), the API MUST NOT register the Key Vault configuration provider; secrets continue to arrive through the Container App's existing environment variables.
- **FR-005**: `MovieTracker.Api/appsettings.Development.json` MUST contain `Azure:TenantId` = `cf732866-97a2-4a44-8301-be829baafe11`, `KeyVault:Uri` = `https://movie-tracker-kv.vault.azure.net/`, and `Cosmos:Endpoint` = `https://movie-tracker-cosmos.documents.azure.com:443/`. No other new keys and no secret material. `.gitignore` is unchanged, so this file remains per-developer and the eval GitHub Actions workflow supplies the equivalent `Azure__TenantId`, `KeyVault__Uri`, and `Cosmos__Endpoint` values as workflow-level environment variables.
- **FR-006**: When the environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING` is empty, the API MUST register the OpenTelemetry tracer, meter, and logger providers WITHOUT the three Azure Monitor exporters, instead of throwing. When it is non-empty, behavior is unchanged.
- **FR-007**: A new GitHub Actions workflow at `.github/workflows/eval.yml` MUST exist and MUST have exactly one trigger: `workflow_dispatch`. Its steps MUST be: checkout, setup .NET 10, `azure/login@v2` using the same OIDC secrets as `infra-deploy.yml` (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), run `dotnet run --project MovieTracker.Evals`, write the console output into `$GITHUB_STEP_SUMMARY`, and upload `MovieTracker.Evals/results/last-run.json` as a build artifact.
- **FR-008**: The Bicep infrastructure MUST assign the GitHub OIDC service principal the `Key Vault Secrets User` role on the vault and the Cosmos built-in data-contributor role on the Cosmos account. The principal id MUST be a new parameter of `main.bicep`, surfaced in `infrastructure/demo.parameters.json`. Role assignment code MUST follow the pattern already used in the repository (role assignments live in `rbac.bicep` or `main.bicep`; no new module is introduced solely for this).
- **FR-009**: `.specify/memory/constitution.md` Principle V MUST be amended (MINOR bump): local development and the eval harness read secrets from Key Vault via the developer's `az login`; user-secrets are no longer used. The Sync Impact Report and version MUST be updated accordingly.
- **FR-010**: `README.md` (local-development section) and `CLAUDE.md` (Development Notes) MUST state that the local flow is `az login` + `dotnet run`, and that `AZURE_CLIENT_ID` and `AZURE_TOKEN_CREDENTIALS` are no longer meaningful locally.
- **FR-011**: No endpoint, prompt, tool, JSON shape, or eval check logic MUST change in this slice. The final committed `results/last-run.json` eval total MUST meet or exceed the pre-branch baseline total. If the first run is below the baseline, the slice MUST investigate whether the cause is this change (fix it and re-run) or normal model variance (re-run once); at most three total runs are allowed and both the baseline total and the final total MUST be recorded in the pull request.
- **FR-012**: `.gitignore`, Key Vault secret names, and any Bicep resource not listed in FR-008 MUST NOT change in this slice. No scheduled eval runs, no pull-request-triggered eval runs.

### Key Entities

- **Credential factory**: a single function or type that returns a `TokenCredential` — managed identity in Azure, Azure CLI locally. Consumed by the Cosmos client and the Key Vault configuration provider.
- **Key Vault configuration source**: a configuration provider registered conditionally at host build time, mapping three specific vault secrets into `IConfiguration`.
- **Eval workflow**: a `workflow_dispatch`-only GitHub Actions workflow identified by `.github/workflows/eval.yml`, producing a job summary and a `last-run.json` artifact.
- **GitHub OIDC principal**: the service principal used by the existing `infra-deploy.yml` OIDC login; its object id is discoverable with `az ad sp show --id <client id>` and becomes a Bicep parameter in this slice.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh shell with none of `AzureOpenAI__ApiKey`, `TheMovieDb__Api-Key`, `OpenMovieDb__Api-Key`, `Cosmos__Endpoint`, or `APPLICATIONINSIGHTS_CONNECTION_STRING` set, `dotnet run --project MovieTracker.Evals` completes end to end with no setup errors.
- **SC-002**: The eval total committed in `results/last-run.json` on this branch meets or exceeds the total in the pre-branch committed `results/last-run.json`; both numbers are recorded in the pull request.
- **SC-003**: `dotnet build movie-tracker-api.slnx` adds no new warnings.
- **SC-004**: A code read of the composition root on this branch shows that the credential factory, the Key Vault provider, and the Application Insights registration all behave in Azure exactly as they did before this slice, keyed off `CONTAINER_APP_NAME` and `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- **SC-005**: A manually dispatched run of `.github/workflows/eval.yml` completes with the eval table in the job summary and `last-run.json` attached as an artifact.
- **SC-006**: The `What-If` job on this slice's pull request reports only additive role-assignment changes on top of the existing Cosmos and Key Vault resources; no deletes, no replacements.

## Assumptions

- The Container Apps platform reliably sets `CONTAINER_APP_NAME` for every revision; this is the single, stable signal for "running in Azure".
- The developer is expected to be signed in to the right tenant with `az login`; cross-tenant local setups are out of scope.
- The GitHub OIDC service principal's object id is retrievable with `az ad sp show --id <client id>` in the owner's tenant; if it is not, the slice stops and reports to the owner (hard stop).
- `MovieTracker.Api/appsettings.Development.json` remains covered by `.gitignore` (unchanged by this slice). This slice is responsible for populating the three FR-005 keys in that local file; the README documents the file. In the eval GitHub Actions workflow those three values are supplied as workflow-level environment variables, so no equivalent file is created in CI.
- The vault secret names `azure-openai-key`, `themoviedb-api-key`, and `omdb-api-key` already exist with the correct values; this slice does not rename, create, or rotate them.
- Scheduled and pull-request-triggered eval runs are deferred to a later slice.
- The role assignments granted in FR-008 are the minimum required; broader roles (Key Vault Administrator, Cosmos account write) are explicitly not granted here.
