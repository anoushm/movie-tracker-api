# Implementation Plan: Local Azure Config + Manual Eval Pipeline

**Branch**: `011-local-azure-config` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: [`specs/011-local-azure-config/spec.md`](spec.md)

## Summary

Replace the direct `DefaultAzureCredential` usage with a single `TokenCredential` factory keyed off `CONTAINER_APP_NAME`: managed identity (via `AZURE_CLIENT_ID`) in Azure, Azure CLI (via `Azure:TenantId`) locally. Wire that credential into `CosmosClient`, and, only when running locally, into an Azure Key Vault configuration provider that exposes three named secrets as `AzureOpenAI:ApiKey`, `TheMovieDb:Api-Key`, and `OpenMovieDb:Api-Key`. When `APPLICATIONINSIGHTS_CONNECTION_STRING` is empty, keep the OpenTelemetry tracer/meter/logger providers registered but skip the three `AzureMonitor*Exporter` calls instead of throwing. Add a manual GitHub Actions workflow `eval.yml` that signs in via the existing OIDC federated identity, runs `dotnet run --project MovieTracker.Evals`, writes the console output to `$GITHUB_STEP_SUMMARY`, and uploads `last-run.json`. Give that GitHub OIDC service principal `Key Vault Secrets User` on the vault and the Cosmos built-in data-contributor role on the account via Bicep (new principal-id parameter). Amend Principle V (MINOR), and update README + CLAUDE.md to describe the new local flow.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.x), ASP.NET Core 10, C# 13 nullable-enabled.

**Primary Dependencies**:
- `Azure.Identity` (for `ManagedIdentityCredential`, `AzureCliCredential`)
- `Azure.Extensions.AspNetCore.Configuration.Secrets` (Key Vault configuration provider)
- `Microsoft.Azure.Cosmos` (existing)
- `Azure.Monitor.OpenTelemetry.Exporter` (existing)

**Storage**: Azure Cosmos DB (NoSQL, serverless, managed-identity authorized). No schema change in this slice.

**Testing**: existing `MovieTracker.Evals` harness end-to-end run + `dotnet build movie-tracker-api.slnx`.

**Target Platform**: Linux container (production), Windows/macOS/Linux developer workstation with Azure CLI (local).

**Project Type**: ASP.NET Core Web API + .NET console test harness + Azure Bicep infrastructure.

**Performance Goals**: no change to request latency or eval-run wall-clock; the slice is a configuration refactor.

**Constraints**:
- `.gitignore`, Key Vault secret names, and every other Bicep resource outside FR-008 MUST NOT change.
- No new NuGet package other than the Key Vault configuration provider if not already referenced.
- The eval total committed in `results/last-run.json` on this branch MUST meet or exceed the pre-branch baseline total; at most three runs allowed.
- The Bicep What-If on this slice MUST show only additive role-assignment changes; no deletes, no replacements.

**Scale/Scope**:
- API: ~15 files touched (new credential factory, `ChatSessionRegistration`, `TelemetryRegistration`, `Program.cs`, `appsettings.Development.json`).
- Infrastructure: `main.bicep` (new parameter + wiring) and `rbac.bicep` (two role assignments) + `demo.parameters.json` (new parameter value).
- CI: one new workflow file `.github/workflows/eval.yml`.
- Docs: `.specify/memory/constitution.md` (Principle V amendment + Sync Impact Report), `README.md`, `CLAUDE.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Verdict |
|---|---|---|
| I. Managed Identity Over Secrets | Vault secrets are still read with an identity (managed in Azure, Azure CLI locally). No secret material enters source. Cosmos keeps managed-identity auth. | PASS |
| II. Infrastructure Is Bicep, Not Clicks | The two new role assignments are declared in Bicep; principal id becomes a parameter and lands in `demo.parameters.json`; no portal step. | PASS |
| III. Incremental Migration With Declared Boundaries | Spec declares both what the slice imports and what it explicitly does not (Out of Scope). Deployed Container App behavior is preserved. | PASS |
| IV. Dependencies Prove Themselves | The existing `/health/cosmos` probe continues to prove Cosmos reachability through the new credential factory. No new runtime dependency ships without an existing probe. | PASS |
| V. No Secret Material In Source | Passes under the MINOR amendment this slice introduces: local secrets reach the harness via Key Vault + `az login`, user-secrets are no longer used. `appsettings.Development.json` remains gitignored and keeps only non-secret keys. | PASS (with MINOR amendment) |

**Development Workflow — Codex cross-review loop**: every stage of this plan (specify, plan, tasks, implement) is reviewed by `codex exec`, max 6 rounds each, before the pull request opens.

## Project Structure

### Documentation (this feature)

```text
specs/011-local-azure-config/
├── prompt.md         # Owner prompt (already present)
├── spec.md           # Feature spec (approved)
├── plan.md           # This file
├── research.md       # Phase 0 output
├── data-model.md     # Phase 1 output
├── quickstart.md     # Phase 1 output
├── contracts/        # Phase 1 output
│   ├── appsettings.schema.json
│   ├── eval-workflow.schema.md
│   └── credential-factory.md
├── checklists/
│   └── requirements.md
└── tasks.md          # Phase 2 output (speckit-tasks)
```

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                                # Updated to call the credential factory and register the Key Vault provider when not in Azure
├── appsettings.Development.json              # Updated with Azure:TenantId, KeyVault:Uri, Cosmos:Endpoint (gitignored, per-developer)
├── Azure/
│   ├── AzureCredentialFactory.cs             # New: single TokenCredential factory keyed off CONTAINER_APP_NAME
│   └── KeyVaultConfigurationRegistration.cs  # New: WebApplicationBuilder extension to register the KV provider locally
├── ChatSessions/
│   └── ChatSessionRegistration.cs            # Updated: consume the credential factory instead of new DefaultAzureCredential()
└── Telemetry/
    └── TelemetryRegistration.cs              # Updated: skip Azure Monitor exporters when connection string is empty

MovieTracker.Evals/                           # No code change (uses WebApplicationFactory<Program>)

infrastructure/
├── main.bicep                                # Updated: new githubOidcPrincipalId parameter + Cosmos role assignment
├── rbac.bicep                                # Updated: Key Vault Secrets User role assignment on the GitHub OIDC principal
└── demo.parameters.json                      # Updated: githubOidcPrincipalId value

.github/workflows/
├── eval.yml                                  # New: manual-dispatch-only eval workflow
└── infra-deploy.yml                          # Unchanged (pattern reference)

.specify/memory/
└── constitution.md                           # Updated: Principle V MINOR amendment + Sync Impact Report + 1.2.0

README.md                                     # Updated: local "az login + dotnet run" flow
CLAUDE.md                                     # Updated: Development Notes match the new local flow
```

**Structure Decision**: single-project ASP.NET Core API + console harness. Adds a small `Azure/` folder inside `MovieTracker.Api/` for the credential factory and the Key Vault registration — one type per file, file-scoped namespaces, matching the rest of the codebase.

## Complexity Tracking

> No constitution violations to justify.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | (none) | (none) |
