# Phase 0 Research: Local Azure Config + Manual Eval Pipeline

**Branch**: `011-local-azure-config` | **Date**: 2026-11-12

No `NEEDS CLARIFICATION` markers were left in `spec.md`. The research below captures the decisions that the spec locks in (and their alternatives), so the plan and tasks can reference them.

---

## Decision 1 — Signal for "running in Azure Container Apps"

- **Decision**: use the presence of the environment variable `CONTAINER_APP_NAME`. When set, the app is in Azure; when unset, it is local.
- **Rationale**: the Container Apps runtime always injects this variable; it is stable, platform-managed, and independent of the ASP.NET Core environment name. The Container App runs as `ASPNETCORE_ENVIRONMENT=Development`, so `IHostEnvironment.IsDevelopment()` would mis-classify Azure as "local". The owner's prompt locks this choice.
- **Alternatives considered**:
  - `AZURE_CLIENT_ID` — rejected: a developer could have it set locally from a stale experiment and it would mis-classify local as Azure.
  - `ASPNETCORE_ENVIRONMENT` — rejected: the deployed Container App runs `Development` today; see above.
  - A custom explicit variable we set ourselves — rejected: duplicates a variable the platform already provides.

## Decision 2 — Credential types

- **Decision**: in Azure use `new ManagedIdentityCredential(managedIdentityClientId: Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"))`; locally use `new AzureCliCredential(new AzureCliCredentialOptions { TenantId = configuration["Azure:TenantId"] })`.
- **Rationale**: today the Cosmos client is constructed with `new DefaultAzureCredential()`, which in Azure resolves to the user-assigned managed identity selected by `AZURE_CLIENT_ID`. Switching to an explicit `ManagedIdentityCredential(managedIdentityClientId: AZURE_CLIENT_ID)` preserves that intended Azure identity without the probe chain. `AzureCliCredential` with a pinned tenant guarantees the developer picks the right directory when they are signed in to several. The owner's prompt names these two credentials.
- **Alternatives considered**:
  - `DefaultAzureCredential` (status quo) — rejected: hides environment-specific behavior behind probe logic, which is exactly what has caused confusion today; also walks past the stale `AZURE_CLIENT_ID` trap described in Decision 1.
  - `ChainedTokenCredential` — rejected: adds complexity without benefit once the environment switch is explicit.

## Decision 3 — Key Vault configuration provider

- **Decision**: register `builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential, new AzureKeyVaultConfigurationOptions { Manager = new MovieTrackerKeyVaultSecretManager() })` only when not running in Azure. The `MovieTrackerKeyVaultSecretManager` only loads the three named secrets and maps them per the spec.
- **Rationale**: Azure already carries secrets into the Container App as environment variables sourced from Key Vault at deployment time (see `container-app.bicep`); re-fetching them at startup would duplicate work. Locally, the developer has no such env vars, so the provider is the single source of truth. Narrowing the set of loaded secrets to the three the app needs avoids leaking unrelated secret names into `IConfiguration`.
- **Alternatives considered**:
  - Loading the KV provider always — rejected: adds per-startup latency in Azure and risks a second route to the same secret.
  - Prefix-based filtering — rejected: current secret names (`azure-openai-key`, `themoviedb-api-key`, `omdb-api-key`) do not share a usable prefix, and the owner forbids renaming them.
  - Dedicated `IConfigurationBuilder` extension in a new NuGet package — rejected: over-engineered for three secrets.

## Decision 4 — NuGet package

- **Decision**: add `Azure.Extensions.AspNetCore.Configuration.Secrets` to `MovieTracker.Api.csproj` if it is not already referenced. Use the current GA major version.
- **Rationale**: this is the Microsoft-maintained Key Vault configuration provider for ASP.NET Core. The package is already a transitive of other Azure SDK packages in the project, but we take an explicit top-level reference so the build does not silently drop it in a future dependency bump.
- **Alternatives considered**:
  - Hand-rolled Key Vault client with a custom `IConfigurationSource` — rejected: duplicates a well-maintained Microsoft package.

## Decision 5 — Optional Application Insights

- **Decision**: in `TelemetryRegistration.cs`, check whether `APPLICATIONINSIGHTS_CONNECTION_STRING` is a non-empty value; if it is empty, register the OpenTelemetry tracer/meter/logger providers WITHOUT the three Azure Monitor exporters; otherwise behavior is unchanged.
- **Rationale**: locally the developer typically has no Application Insights connection string; today that throws at the first `Add*AzureMonitor*Exporter` call. In Azure the connection string is always set by the Container App, so behavior is preserved. Keeping the OpenTelemetry pipelines registered (without exporters) means tracing/metrics still flow through the app's own code paths, so log scopes and `ActivitySource` usage still work.
- **Alternatives considered**:
  - Skipping telemetry registration altogether locally — rejected: changes application behavior in subtle ways (loss of log scopes).
  - Using a null exporter — rejected: adds a dependency with no payoff.

## Decision 6 — Delivery of non-secret local keys

- **Decision**: write `Azure:TenantId`, `KeyVault:Uri`, and `Cosmos:Endpoint` to `MovieTracker.Api/appsettings.Development.json`. The GitHub Actions eval workflow supplies the same three keys as workflow-level environment variables using the double-underscore (`__`) form, since the file is gitignored and `.gitignore` is not touched by this slice.
- **Rationale**: the owner's prompt locks this file name. The gitignored file is the owner's local source of truth. CI gets the same keys from env vars so the harness runs identically there without needing to materialize the file.
- **Alternatives considered**:
  - Committing the file to the repo — rejected: would need to change `.gitignore`, which the slice forbids.
  - Shipping a template `appsettings.Development.sample.json` — rejected: adds a bootstrap step with no gain over env vars.

## Decision 7 — Role assignments for the GitHub OIDC principal

- **Decision**: in `rbac.bicep`, extend the module (no new module) with a `Key Vault Secrets User` role assignment on the vault for the GitHub OIDC service principal. In `main.bicep` (or equivalent — matching existing patterns), add the Cosmos built-in data-contributor assignment on the Cosmos account for the same principal. The principal id becomes a new `githubOidcPrincipalId` parameter of `main.bicep`, with its value set in `demo.parameters.json`.
- **Important wiring note**: the existing `rbac` module invocation in `main.bicep` is guarded by `if (!empty(userPrincipalId))`. The GitHub-OIDC role assignments MUST NOT inherit that guard — they are gated on `githubOidcPrincipalId` alone. Either (a) add a second unconditional invocation of `rbac.bicep` (only passing `githubOidcPrincipalId`), or (b) declare the new assignments directly in `main.bicep` with their own `if (!empty(githubOidcPrincipalId))` guard. The tasks phase picks one mechanism; both satisfy the contract.
- **Principal lookup and hard stops** (from the owner prompt): the parameter value is the object id of the service principal already used by `infra-deploy.yml`. Retrieve it on a workstation with `az ad sp show --id <AZURE_CLIENT_ID-used-by-the-workflow>` (field: `id`). Hard stop and escalate to the owner if:
  - `az ad sp show` cannot resolve the client id, or
  - the Azure identity running `az bicep` or the GitHub deploy workflow lacks permissions to create the two role assignments.
- **Rationale**: the existing `rbac.bicep` is already the home of user/service-principal role assignments on the Key Vault; the Cosmos data role is already issued from `cosmos.bicep` to the managed identity, so the account-level `sqlRoleAssignments` resource type is the right place for a second principal. The parameter + parameters-file split matches the existing pattern.
- **Alternatives considered**:
  - Giving the principal `Key Vault Administrator` — rejected: too broad; least privilege is the slice's intent.
  - Giving the principal account-write on Cosmos — rejected: the harness only reads and writes data items; data-contributor is sufficient.

## Decision 8 — Eval workflow trigger and steps

- **Decision**: `.github/workflows/eval.yml` has exactly one trigger, `workflow_dispatch`. Steps: `actions/checkout@v4`; `actions/setup-dotnet@v4` with `.NET 10.x`; `azure/login@v2` using the three OIDC secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; `dotnet run --project MovieTracker.Evals --configuration Release` (stdout captured); write the console table to `$GITHUB_STEP_SUMMARY`; upload `MovieTracker.Evals/results/last-run.json` with `actions/upload-artifact@v4`.
- **Rationale**: exactly matches the owner's prompt. Reusing the same OIDC secrets as `infra-deploy.yml` means no new repository secrets. `workflow_dispatch`-only ensures no PR or push ever runs the harness automatically.
- **Alternatives considered**:
  - Running on schedule or PR — rejected: forbidden by scope.
  - Publishing a GitHub check from the result — rejected: out of scope.

## Decision 9 — Constitution amendment

- **Decision**: amend Principle V. Keep the prohibition on secret material in source. Replace the user-secrets sentence with: "Secrets reach the running container as environment variables sourced from Key Vault, and reach local development through the Azure Key Vault configuration provider using the developer's Azure CLI identity." Update the Sync Impact Report, bump the version from 1.1.0 to 1.2.0 (MINOR), and update "Last Amended" to 2026-11-12.
- **Rationale**: owner's prompt locks this behavior; it is a MINOR expansion of guidance, not a MAJOR redefinition.
