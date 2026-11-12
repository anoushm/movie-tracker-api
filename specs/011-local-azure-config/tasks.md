---

description: "Task list for slice 011: local Azure config + manual eval pipeline"
---

# Tasks: Local Azure Config + Manual Eval Pipeline

**Input**: Design documents from `specs/011-local-azure-config/`

**Prerequisites**: plan.md (approved), spec.md (approved), research.md, data-model.md, contracts/, quickstart.md

**Tests**: no new automated tests are added by this slice. Verification uses `dotnet build`, a local run of the eval harness, and a post-merge manual run of the new workflow.

**Organization**: Tasks are grouped by user story. US1 (local eval runs with `az login`), US2 (Azure Container App unchanged), US3 (manual GitHub Actions eval workflow) are all P1 in this slice.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- API code: `MovieTracker.Api/`
- Evals project: `MovieTracker.Evals/` (no code change expected)
- Infrastructure (Bicep): `infrastructure/`
- CI: `.github/workflows/`
- Docs: repo root + `.specify/memory/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure the local environment and the project file are ready before any code change.

- [ ] T001 Confirm `MovieTracker.Api/MovieTracker.Api.csproj` references the Key Vault configuration provider package `Azure.Extensions.AspNetCore.Configuration.Secrets`. If missing, add it via `dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets` at the current GA version. No other NuGet package additions are permitted.
- [ ] T002 Confirm `git status` on branch `011-local-azure-config` is clean except for the slice's own files and the three pre-existing untracked entries (`specs/011-local-azure-config/`, `specs/run-011-010-009.md`, stashed `specs/009-budget-alert/prompt.md`). No changes to `.gitignore`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the credential factory and its two consumers (Cosmos, Key Vault) are the foundation that every user story relies on.

**⚠️ CRITICAL**: no user-story task can begin until this phase is complete.

- [ ] T003 [P] Create `MovieTracker.Api/Azure/AzureCredentialFactory.cs` per the contract in `specs/011-local-azure-config/contracts/credential-factory.md` (instance class, injected `IConfiguration`, `IsInAzure` property, `Create()` method; one type per file, file-scoped namespace, no comments, braces on every `if`, plain-camelCase private fields).
- [ ] T004 [P] Create `MovieTracker.Api/Azure/MovieTrackerKeyVaultSecretManager.cs`: a `KeyVaultSecretManager` subclass whose `Load(SecretProperties)` returns true only for the three secret names in `data-model.md` and whose `GetKey(KeyVaultSecret)` returns the mapped `IConfiguration` key.
- [ ] T005 Create `MovieTracker.Api/Azure/KeyVaultConfigurationRegistration.cs`: static `WebApplicationBuilder.AddKeyVaultWhenLocal(AzureCredentialFactory factory, TokenCredential credential)` extension matching the credential-factory contract — no-op when `factory.IsInAzure`, fails fast on missing `KeyVault:Uri` when not in Azure, otherwise registers `AddAzureKeyVault` with the supplied credential and the new secret manager. Depends on T003 and T004.
- [ ] T006 Update `MovieTracker.Api/ChatSessions/ChatSessionRegistration.cs`: take a `TokenCredential` parameter (new argument of `AddChatSessions`), pass it to `new CosmosClient(...)`, remove `new DefaultAzureCredential()`. Keep the rest of the method unchanged. Depends on T003.
- [ ] T007 Update `MovieTracker.Api/Telemetry/TelemetryRegistration.cs`: when `APPLICATIONINSIGHTS_CONNECTION_STRING` is null or whitespace, register the OpenTelemetry tracer/meter/logger providers WITHOUT the three `AzureMonitor*Exporter` calls; otherwise keep today's behavior exactly. Scope limited to the three `.Add...AzureMonitor...Exporter(...)` sites.
- [ ] T008 Update `MovieTracker.Api/Program.cs`: at the top of the file (BEFORE the existing `configuration["AzureOpenAI:ApiKey"]` and other secret-key reads), construct `AzureCredentialFactory` once from the builder's `IConfiguration`, call `Create()` once into a local `TokenCredential`, and call `builder.AddKeyVaultWhenLocal(factory, credential)`. Only then read the secret configuration values. Pass the credential to `AddChatSessions(...)`. Remove any `DefaultAzureCredential` reference. Depends on T003, T005, T006.
- [ ] T009 Update `MovieTracker.Api/appsettings.Development.json` (gitignored, local-only) with the three non-secret keys from FR-005 and nothing else new. Do not add any secret.

**Checkpoint**: credential factory + Key Vault provider + Cosmos wiring + optional Application Insights all compile; `dotnet build movie-tracker-api.slnx` is clean.

---

## Phase 3: User Story 1 - Local developer runs the eval harness with only `az login` (Priority: P1) 🎯 MVP

**Goal**: `dotnet run --project MovieTracker.Evals` works end to end with just `az login` and the three non-secret settings already populated by the foundational phase.

**Independent Test**: on a fresh shell with none of `AzureOpenAI__ApiKey`, `TheMovieDb__Api-Key`, `OpenMovieDb__Api-Key`, `Cosmos__Endpoint`, `APPLICATIONINSIGHTS_CONNECTION_STRING` set, run `dotnet run --project MovieTracker.Evals` and see the full case table, totals, and `results/last-run.json` update.

### Implementation for User Story 1

- [ ] T010 [US1] Verify no code change is needed inside `MovieTracker.Evals/`; the harness consumes the API's `WebApplicationFactory<Program>` and therefore picks up the new credential factory and Key Vault provider for free.
- [ ] T011 [US1] Capture the pre-branch baseline eval total from `git show <branch-point>:MovieTracker.Evals/results/last-run.json` per the quickstart; record the `passed / total`.
- [ ] T012 [US1] Run `dotnet run --project MovieTracker.Evals` on a fresh shell per the quickstart's Prerequisites and Local Verification sections. Record the new `passed / total`.
- [ ] T013 [US1] If the new total is below the baseline, investigate whether the cause is this slice (fix and re-run) or model variance (one re-run allowed). Total runs capped at 3. Keep the final `MovieTracker.Evals/results/last-run.json` for the commit.

**Checkpoint**: eval harness runs locally with no `set` commands and the new total meets or exceeds the baseline.

---

## Phase 4: User Story 2 - Container App in Azure keeps working unchanged (Priority: P1)

**Goal**: production parity — the Container App continues to reach every Azure resource using its managed identity, and no code path in Azure reads from Key Vault at runtime.

**Independent Test**: code read on this branch shows the credential factory, the Key Vault provider gate, and the Application Insights registration all branch on `CONTAINER_APP_NAME` and `APPLICATIONINSIGHTS_CONNECTION_STRING` and preserve the pre-slice Azure behavior.

### Implementation for User Story 2

- [ ] T014 [US2] Read `MovieTracker.Api/Program.cs` end to end after Phase 2 lands; confirm `factory.IsInAzure` gates both the Cosmos credential choice and `AddKeyVaultWhenLocal`, and that `AZURE_CLIENT_ID` is still the selector for the managed identity in Azure.
- [ ] T015 [US2] Read `MovieTracker.Api/Telemetry/TelemetryRegistration.cs`; confirm the three `AzureMonitor*Exporter` calls remain exactly as today when `APPLICATIONINSIGHTS_CONNECTION_STRING` is non-empty.
- [ ] T016 [US2] Read `MovieTracker.Api/ChatSessions/ChatSessionRegistration.cs`; confirm the Cosmos client now takes the credential via parameter and nothing else about it changed (same `CosmosClientOptions`, same `CosmosSystemTextJsonSerializer`).

**Checkpoint**: Azure-path behavior is unchanged under the slice.

---

## Phase 5: User Story 3 - Owner triggers the eval from GitHub Actions (Priority: P1)

**Goal**: a manual-dispatch-only workflow that runs the eval harness using the existing OIDC identity, writes the table to the job summary, and uploads `last-run.json`.

**Independent Test**: after merge, open Actions → "Eval" → **Run workflow**. The run is green, the job summary has the eval table, the `eval-last-run` artifact is attached.

### Implementation for User Story 3

- [ ] T017 [P] [US3] Create `.github/workflows/eval.yml` per `specs/011-local-azure-config/contracts/eval-workflow.schema.md`: single `workflow_dispatch` trigger, `permissions: id-token: write, contents: read`, workflow-level `env:` with the three keys from Decision 6, steps checkout/setup-dotnet/azure-login/run-evals/summary/upload-artifact exactly in that order. No PR or push trigger. No deployment step.
- [ ] T018 [US3] Update `infrastructure/main.bicep` to add `githubOidcPrincipalId` (string, required — no default; supplied by `demo.parameters.json` in T019) and to declare, directly in `main.bicep`, (a) a `Microsoft.Authorization/roleAssignments` on the Key Vault for role id `4633458b-17de-408a-b874-0445c86b69e6` (Key Vault Secrets User) and (b) a `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments` on the Cosmos account for role id `00000000-0000-0000-0000-000000000002` (Cosmos DB Built-in Data Contributor). Both assignments are unconditional (no `if`) and independent of `userPrincipalId`. The Cosmos assignment follows the existing pattern in `cosmos.bicep` (no `principalType` field). `rbac.bicep` is NOT modified — Option (b) from research.md Decision 7.
- [ ] T019 [US3] Update `infrastructure/demo.parameters.json`: add `githubOidcPrincipalId` with the value from `az ad sp show --id <workflow client id>` (field `id`). Hard stop if the lookup fails.
- [ ] T020 [US3] Run `az bicep build --file infrastructure/main.bicep`; confirm zero new warnings.

**Checkpoint**: Bicep compiles; the workflow file exists; the parameters file carries the new principal id.

---

## Phase 6: Docs

**Purpose**: keep the constitution, the README, and CLAUDE.md honest about the new flow.

- [ ] T021 [P] Amend `.specify/memory/constitution.md` Principle V (MINOR): replace the user-secrets sentence with "Secrets reach the running container as environment variables sourced from Key Vault, and reach local development through the Azure Key Vault configuration provider using the developer's Azure CLI identity." Update the Sync Impact Report, bump the version from 1.1.0 to 1.2.0, and update "Last Amended" to 2026-11-12.
- [ ] T022 [P] Update `README.md` local-development / Cosmos section: local run is `az login` followed by `dotnet run`; explicitly state that `AZURE_CLIENT_ID` and `AZURE_TOKEN_CREDENTIALS` are no longer meaningful locally and mention the local `appsettings.Development.json` keys from FR-005.
- [ ] T023 [P] Update `CLAUDE.md` Development Notes to match T022 (two-sentence edit); do not touch any other section.

---

## Phase 7: Verification

**Purpose**: prove the slice is done per "Done When" in `prompt.md` and SC-001..SC-006 in `spec.md`.

- [ ] T024 Run `dotnet build movie-tracker-api.slnx`; zero new warnings (SC-003).
- [ ] T025 Smoke-test the API locally (quickstart steps 1-2 only): `/health/cosmos` returns 200 using the new credential factory. Do NOT re-run the eval harness here; reuse the final `last-run.json` captured in T011-T013.
- [ ] T026 Confirm the final `MovieTracker.Evals/results/last-run.json` from Phase 3 is present and record `baseline` and `final` totals for the pull-request body (SC-002).
- [ ] T027 Run the local Codex cross-review on the implementation (post-code change): `codex exec -s read-only --skip-git-repo-check ... < /dev/null` (stdin closed per the prompt so the autonomous review cannot hang) with the same guardrails; fix findings up to 6 rounds. After any code fix in a given round, re-run T024 (build) and T025 (health smoke). If a fix changes anything the eval harness touches, re-run the eval one more time — but the T011-T013 three-run cap is absolute: once three runs have been consumed, no further eval runs are allowed and a failure to establish a passing final total is a hard stop. Hard stop if round 6 is not approved.
- [ ] T028 Commit (no Claude attribution — no `Co-Authored-By`, no "Generated with Claude Code", no mention of Claude), include only the slice's files (slice folder, modified API files, Bicep files, `demo.parameters.json`, workflow file, constitution, README, CLAUDE.md, and the final `last-run.json`). Push the branch. Open a pull request to `main` whose body covers: (a) what changed, (b) why, (c) baseline and final eval totals, (d) local run instructions (the `az login` + `dotnet run` flow from the quickstart).
- [ ] T029 On the PR, verify `Validate Bicep` passes and the `What-If` job reports only ADDITIVE role-assignment changes — no deletes, no replacements (SC-006). If it reports anything else, fix within scope and push.
- [ ] T030 Run the GitHub Codex review loop: comment `@codex review`, wait, address every comment (fix code or reply with a short reason), push, request again. After any code change in a round, re-run the equivalent of T024 (build), T025 (smoke) locally, and the GitHub `Validate Bicep` + `What-If` checks must stay green (T029 re-verified). If a change touches the eval harness behavior, re-run the eval locally one more time within the T011-T013 three-run cap and refresh the committed `last-run.json`; if the cap is exhausted without a passing final total, hard stop. Loop until Codex approves OR has no open comments. Allow up to 6 rounds; if round 6 still has unresolved findings, hard stop and escalate to the owner.
- [ ] T031 Delete any files created under `C:/projects/temp/` for this slice.
- [ ] T032 Prepare the per-slice handoff line for the runner's final report: PR link, Codex rounds used (local + GitHub), eval totals (baseline → final), and any items left open. **T032 is the slice's stopping point.**

---

## Runner-owned post-handoff checklist

Items below are performed by the runner (not this slice). R1 and R2 come straight from `specs/run-011-010-009.md` steps 7-8. R3 is one additional runner obligation beyond those steps, kept here for traceability to SC-005.

- [ ] R1 Merge the PR.
- [ ] R2 Wait until the workflows on `main` triggered by the merge finish green.
- [ ] R3 Manually run `.github/workflows/eval.yml` once (SC-005) and confirm green, job summary has the eval table, `eval-last-run` artifact is attached. (Additional runner obligation beyond the runner's steps 7-8; attached here because the eval workflow is created in this slice.)

**Checkpoint**: the pre-commit artifacts (build clean, smoke-tested API, final `last-run.json` captured) are in place; T027-T030 cover the local Codex review, commit, push, PR, and GitHub Codex loop; the runner then merges and performs the post-merge checks (R1-R3).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1. Required before the API-code user stories (US1 and US2) can start.
- **Phase 3 (US1)**: depends on Phase 2.
- **Phase 4 (US2)**: depends on Phase 2.
- **Phase 5 (US3)**: depends only on Phase 1. US3 is pure infra + workflow work that does not touch API code, so it runs independently of Phase 2.
- **Phase 6 (Docs)**: depends only on Phase 1.
- **Phase 7 (Verification)**: depends on Phases 2–6.

### Within Each User Story

- T010 → T011 → T012 → T013 (US1 is sequential).
- T014/T015/T016 are independent reads (US2).
- T017 is independent (new workflow file); T018 → T019 → T020 is the US3 Bicep chain (`main.bicep` -> `demo.parameters.json` -> build).

### Parallel Opportunities

- `[P]` tasks: T003, T004, T017, T021, T022, T023.
- US2 reads (T014/T015/T016) can also be done in parallel but are quick enough to do sequentially.
- Phase 6 (T021/T022/T023) can run anytime after Phase 2.

---

## Parallel Example: Phase 2 kickoff

```bash
# Launch the two leaf files together before T005/T006 can start:
Task: "Create MovieTracker.Api/Azure/AzureCredentialFactory.cs (T003)"
Task: "Create MovieTracker.Api/Azure/MovieTrackerKeyVaultSecretManager.cs (T004)"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1: Setup → packages present.
2. Phase 2: Foundational → code compiles.
3. Phase 3: US1 → eval runs locally.
4. **STOP and VALIDATE**: capture baseline + final totals.

### Full Slice

5. Phase 4: confirm Azure path by reading code.
6. Phase 5: add workflow and Bicep.
7. Phase 6: docs.
8. Phase 7: verification + temp cleanup.

---

## Notes

- `[P]` tasks touch different files and have no incomplete dependencies.
- Keep the final `MovieTracker.Evals/results/last-run.json` in the commit.
- Do not touch `.gitignore`.
- No new NuGet package outside T001.
- No code comments in any new or modified file (per CLAUDE.md house style).
- Temp files only under `C:\projects\temp`; T031 cleans them up.

---

## Hard stops (escalate to the owner; do not continue)

- The GitHub OIDC service principal's object id cannot be resolved via `az ad sp show --id <client id>` (T019).
- The identity running Bicep (locally or in the deploy workflow) lacks permission to create the two new role assignments (T018/T020/T029).
- A credential or Key Vault access error that no in-scope code change can resolve.
- Any change that would go outside the slice's Scope (as listed in `prompt.md` section Scope / Out of scope).
- The local Codex review (T027) is not approved after round 6.
- The GitHub Codex review loop (T030) still has unresolved findings after round 6.
- The `What-If` job (T029) shows a delete or replace that no in-scope change can prevent.
- Any post-merge workflow on `main` (R2) fails in a way that no in-scope change can fix.
