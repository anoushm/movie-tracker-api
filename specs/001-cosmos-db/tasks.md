---
description: "Task list for feature 001-cosmos-db (implementation)"
---

# Tasks: Cosmos DB Provisioning and Container App Connectivity

**Input**: Design documents from `/specs/001-cosmos-db/`

**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories)

**Tests**: None. Per Constitution Principle IV ("Dependencies Prove Themselves") and
[plan.md](./plan.md) §Testing, the `GET /health/cosmos` probe is itself the
verification surface. No test project is introduced. Manual verification is enumerated
in [spec.md](./spec.md) SC-001..SC-007.

**Organization**: Tasks follow the file-order dictated by the plan and the user's rule
(**Bicep → csproj + serializer → Program.cs config/DI → `/health/cosmos` endpoint →
docs**), which supersedes the template's per-user-story slicing because every
foundational task in this slice is shared across all three stories. The endpoint
mapping (Phase 5) is where US1 becomes observable; US2 additionally requires the
local-dev doc in Phase 6. US3 is satisfied entirely by the Bicep shape in T001.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: can run in parallel with other [P] tasks in the same phase (different files, no dependency on an incomplete task in the same phase)
- **[Story]**: on user-story-phase tasks only (US1, US2, US3). Foundational and polish tasks carry no story label.

## Path Conventions

Single ASP.NET Core project at repository root:

- Source: `MovieTracker.Api/`
- Infrastructure: `infrastructure/`
- Docs: `README.md`, `CLAUDE.md` at repo root

All paths below are repo-relative to `C:\projects\dev\movie-tracker-api\`.

---

## Phase 1: Setup

**Purpose**: project initialization.

*None required.* The repo is already a .NET 10 ASP.NET Core project with an existing
`MovieTracker.Api.csproj`, `Program.cs`, `infrastructure/main.bicep`, and
`infrastructure/demo.parameters.json`. No new project, tooling, or lint config.

---

## Phase 2: Foundational — Infrastructure (Bicep)

**Purpose**: provision the Cosmos account + database + container and grant the
Container App's existing user-assigned managed identity data-plane access. Everything
in this phase blocks the endpoint work in Phase 4.

**⚠️ CRITICAL**: no user-story implementation begins until Phases 2, 3, and the
Program.cs foundational wiring in the first half of Phase 4-precursor (T008) are
complete.

- [ ] T001 Create `infrastructure/cosmos.bicep` with all of the following, verbatim values:
  - Params: `location string = resourceGroup().location`, `accountName string`, `principalId string`.
  - Resource `Microsoft.DocumentDB/databaseAccounts@2024-05-15` named `accountName`, `kind: 'GlobalDocumentDB'`, no `identity` block, `properties`: `databaseAccountOfferType: 'Standard'`, `minimalTlsVersion: 'Tls12'`, `enableFreeTier: false`, **`disableLocalAuth: true`** (Principle I: managed identity is the sole auth mechanism; keys still exist in ARM but are non-functional for data-plane authentication), `capabilities: [ { name: 'EnableServerless' } { name: 'EnableNoSQLVectorSearch' } ]`, `capacity: { totalThroughputLimit: 4000 }`, single-region `locations: [ { locationName: location, failoverPriority: 0, isZoneRedundant: false } ]`, `isVirtualNetworkFilterEnabled: false`, `virtualNetworkRules: []`, `ipRules: []`.
  - Child resource `Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15` named `database`, no `throughput` (serverless), `properties.resource.id: 'database'` (required by ARM schema; matches backend reference).
  - Child resource `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15` named `chat-sessions` with `properties.resource.id: 'chat-sessions'`, `properties.resource.partitionKey: { kind: 'Hash', paths: [ '/PartitionKey' ] }`, and `properties.resource.conflictResolutionPolicy: { mode: 'LastWriterWins', conflictResolutionPath: '/_ts' }`.
  - Child resource `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15` named `guid(cosmosAccount.id, principalId, '00000000-0000-0000-0000-000000000002')`, `properties.roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'` (built-in **Cosmos DB Data Contributor**), `properties.principalId: principalId`, `properties.scope: cosmosAccount.id`.
  - Outputs: `accountName string = accountName`, `endpoint string = cosmosAccount.properties.documentEndpoint`, `databaseName string = 'database'`, `containerName string = 'chat-sessions'`.

- [ ] T002 Modify `infrastructure/main.bicep`:
  - Add `@description('Cosmos DB account name (globally unique)') param cosmosAccountName string` alongside the existing params.
  - After the existing `managedIdentity` resource (line 61), instantiate a new `module cosmos 'cosmos.bicep'` passing `location: location`, `accountName: cosmosAccountName`, `principalId: managedIdentity.properties.principalId`.
  - Extend the existing `containerAppModule` `params` block to pass `cosmosEndpoint: cosmos.outputs.endpoint`, `cosmosDatabase: cosmos.outputs.databaseName`, `cosmosContainer: cosmos.outputs.containerName`. (Passing `cosmos.outputs.*` creates an implicit dependency on the cosmos module — no explicit `dependsOn` needed.)
  - Add three outputs at the end: `output cosmosAccountName string = cosmos.outputs.accountName`, `output cosmosEndpoint string = cosmos.outputs.endpoint`, `output cosmosDatabaseName string = cosmos.outputs.databaseName`.

- [ ] T003 [P] Modify `infrastructure/container-app.bicep`:
  - Add three params: `param cosmosEndpoint string`, `param cosmosDatabase string`, `param cosmosContainer string`.
  - Append four entries to the existing `envVars` array — ALL plain values, NO `secretRef`, NO new entries in `secrets`:
    - `{ name: 'Cosmos__Endpoint', value: cosmosEndpoint }`
    - `{ name: 'Cosmos__Database', value: cosmosDatabase }`
    - `{ name: 'Cosmos__Container', value: cosmosContainer }`
    - `{ name: 'AZURE_CLIENT_ID', value: managedIdentityClientId }`

- [ ] T004 [P] Modify `infrastructure/demo.parameters.json`:
  - Add a new parameter block: `"cosmosAccountName": { "value": "movie-tracker-cosmos-demo" }`. Value is globally unique across Azure; if the demo deployment later collides, the fix is a one-line change to this value.

- [ ] T004a Validate Bicep compiles: `bicep build infrastructure/main.bicep` returns exit 0 with no errors and no warnings other than pre-existing ones. Do this before attempting any Azure deployment — a syntax or reference error caught here is seconds; caught in `az deployment group create` is minutes plus a partial rollout to unwind.

**Checkpoint**: after T001–T004a, `az deployment group create` against `RG-MovieTracker-Demo` provisions the Cosmos account, `database`, `chat-sessions`, and the data-plane role assignment on the existing UAMI. The Container App gets `Cosmos__*` and `AZURE_CLIENT_ID` env vars but no code yet consumes them.

---

## Phase 3: Foundational — .NET project (csproj + serializer + appsettings)

**Purpose**: introduce the SDK, credential, and serializer that the Cosmos client
registration in Phase 4 will depend on, and wire the non-secret config keys.

- [ ] T005 [P] Modify `MovieTracker.Api/MovieTracker.Api.csproj`:
  - Add three `<PackageReference>` entries to the existing `<ItemGroup>`:
    - `<PackageReference Include="Microsoft.Azure.Cosmos" Version="3.62.0" />` (matches the Function App at `C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend\MovieTracker.Backend.csproj` line 32).
    - `<PackageReference Include="Azure.Identity" Version="1.21.0" />` (matches the Function App line 21).
    - `<PackageReference Include="Microsoft.Extensions.AI" Version="10.6.0" />` (matches the Function App line 39; explicit because the ported serializer's `AIJsonUtilities.DefaultOptions` is a direct compile-time dependency and must not be left to transitive resolution through `Microsoft.Agents.AI 1.17.0`).

- [ ] T006 [P] Create `MovieTracker.Api/Core/CosmosSystemTextJsonSerializer.cs`:
  - Copy from `C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend\CosmosSystemTextJsonSerializer.cs`.
  - Change `namespace MovieTracker.Backend` to `namespace MovieTracker.Api.Core`.
  - Prune the unused `System.Collections.Generic`, `System.Linq`, `System.Text`, `System.Threading.Tasks` usings.
  - Strip the class-level XML doc comment (this repo's no-inline-comments standard). Rationale for the design lives in [plan.md](./plan.md) §Serializer port, not in the source file.
  - Preserve `PropertyNamingPolicy = null` and the combined `AIJsonUtilities.DefaultOptions.TypeInfoResolver` + `DefaultJsonTypeInfoResolver` resolver — those are behavior, not documentation.
  - Do NOT modernize (no primary constructor, no record, no expression-bodied override rewrites) — the diff stays auditable against the source file.

- [ ] T007 [P] Modify `MovieTracker.Api/appsettings.json`:
  - Add a top-level `"Cosmos"` object with the two non-secret literal keys ONLY:
    - `"Database": "database"`
    - `"Container": "chat-sessions"`
  - Do NOT add `"Endpoint"`. The endpoint is environment-specific and must be supplied at deploy time (`Cosmos__Endpoint` env var in the Container App; user-secrets locally). Omitting the key ensures T008's null-check triggers the required fail-fast startup error (FR-011) when the deployment forgets to inject it. A `"<set-in-env>"` placeholder would defeat the null-check because the value would be non-null but wrong.

**Checkpoint**: after T005–T007 the project restores and builds, the serializer type
resolves, and the three `Cosmos:*` config keys are readable from
`builder.Configuration`.

---

## Phase 4: Foundational — Program.cs config validation + CosmosClient DI

**Purpose**: fail fast on missing config and register the single shared `CosmosClient`
that the endpoint in Phase 5 will consume. Kept as its own phase so the endpoint task
lands strictly last as the user required.

- [ ] T008 Modify `MovieTracker.Api/Program.cs` — insert the following BEFORE `WebApplication app = builder.Build();`, using the same "read → `?? throw new InvalidOperationException(...)`" pattern already present at Program.cs line 14 for `AzureOpenAI:Endpoint`:
  - Add usings at the top of the file: `using Azure.Identity;`, `using Microsoft.Azure.Cosmos;`, `using MovieTracker.Api.Core;`.
  - Read three config values:
    - `string cosmosEndpoint = builder.Configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Endpoint");`
    - `string cosmosDatabase = builder.Configuration["Cosmos:Database"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Database");`
    - `string cosmosContainer = builder.Configuration["Cosmos:Container"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Container");`
  - Register a singleton `CosmosClient`:
    - `builder.Services.AddSingleton(sp => new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), new CosmosClientOptions { Serializer = new CosmosSystemTextJsonSerializer() }));`
  - Do NOT set `CosmosClientOptions.RequestTimeout` here; the per-request 10 s bound lives at the probe call site in T009 so a later slice reconfiguring the shared client cannot regress FR-007 / SC-006.

- [ ] T008a Validate the project compiles: `dotnet build MovieTracker.Api/MovieTracker.Api.csproj` returns exit 0. This catches missing `using` directives (e.g. `System.Net` for T009, `Azure.Identity` for T008), missing package references (T005), and namespace mismatches on the ported serializer (T006) before the endpoint task adds more surface area.

**Checkpoint**: after T008–T008a the app compiles and fails to start unless all three `Cosmos:*` keys are present. When they are, a `CosmosClient` singleton resolves from DI, but no endpoint uses it yet — `/health`, `/health/ready`, `/version` all still respond as before.

---

## Phase 5: User Story 1 — Operator confirms the deployed API can reach Cosmos DB (Priority: P1) 🎯 MVP

**Story goal**: an operator hits one URL against the deployed Container App and gets a
definitive yes/no on Cosmos connectivity, with the account, database, and container
named in the response ([spec.md](./spec.md) US1).

**Independent test**: `curl https://<container-app-fqdn>/health/cosmos` returns HTTP 200
with a JSON body containing `status: "ok"`, the deployed Cosmos account host, and the
literals `database` and `chat-sessions`. A 503 with `reason: "authorization"` during
the first minute after a fresh deploy is expected role-assignment propagation (spec
Edge Case 2, SC-001) and is cleared by retry.

- [ ] T009 [US1] Modify `MovieTracker.Api/Program.cs` — map `GET /health/cosmos` AFTER the existing `app.MapGet("/health/ready", …)` (Program.cs line 89) and before `app.MapGet("/version", …)` (line 92). Do NOT modify `/health` or `/health/ready` handlers (FR-008, SC-005).
  - Add usings at the top of `Program.cs`: `using System.Net;` (for `HttpStatusCode` — omitting this is a compile break) and `using Azure.Identity;` (already added in T008 for `DefaultAzureCredential`; `AuthenticationFailedException` lives in `Azure.Identity` too).
  - Handler signature receives `(CosmosClient cosmosClient, IConfiguration configuration)` from DI.
  - Timeout: `using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));`
  - Round trip: `Container container = cosmosClient.GetContainer(configuration["Cosmos:Database"]!, configuration["Cosmos:Container"]!); await container.ReadContainerAsync(cancellationToken: cts.Token);` — both names come from configuration; no string literals in the handler.
  - Success: return `Results.Ok(new { status = "ok", account = new Uri(configuration["Cosmos:Endpoint"]!).Host, database = configuration["Cosmos:Database"], container = configuration["Cosmos:Container"] })`.
  - `catch (CosmosException ex)` — classify by `ex.StatusCode` and return `Results.Json(body, statusCode: 503)`:
    - `HttpStatusCode.Unauthorized` or `HttpStatusCode.Forbidden` → `reason: "authorization"`, and include `requiredRole: "Cosmos DB Built-in Data Contributor"` in the body so a developer hitting spec US2 acceptance #2 sees which role to grant (the role name is a static known-at-implementation-time value, not exception content — safe under FR-006).
    - `HttpStatusCode.NotFound` → `reason: "not-found"` (single `ReadContainerAsync` does not distinguish "database missing" from "container missing"; response body names both, resolving via portal/CLI is expected).
    - anything else → `reason: "unavailable"`
    - body ALWAYS contains `status: "fail"`, `reason`, `account`, `database`, `container`. NEVER include `ex.Message`, `ex.Diagnostics`, `ex.ResponseBody`, tokens, or connection strings (FR-006).
  - `catch (AuthenticationFailedException)` → 503 with `reason: "authorization"` and the same `requiredRole` field. Without this catch, a credential acquisition failure (e.g. no `az login`, wrong tenant, MI not attached) surfaces as an unhandled exception and returns 500, violating FR-005 (probe returns 503 on failure).
  - `catch (OperationCanceledException)` → 503 with `reason: "timeout"`, same body shape.

**Checkpoint**: US1 complete. `GET /health/cosmos` returns 200 against the deployed
`demo` after role-assignment propagation. US3 (shape match) is already satisfied by
T001; the deployed database and container match the Function App's
`C:\projects\dev\movie-tracker-backend\infrastructure\cosmos.bicep` on name, partition
key, and conflict-resolution policy.

---

## Phase 6: User Story 2 — Developer runs the API locally against the same Cosmos DB (Priority: P2)

**Story goal**: a developer follows the README on a fresh machine and gets a passing
local `/health/cosmos` against the deployed Cosmos account, without introducing a
connection string ([spec.md](./spec.md) US2, SC-004).

**Independent test**: with `az login` completed and the documented one-time data-plane
role grant applied, running `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`
and issuing a local `curl` against `/health/cosmos` returns 200 naming the same account
as the deployed app.

- [ ] T010 [US2] Modify `README.md` — add a new "Cosmos DB local development" section satisfying FR-013:
  - Names the deployed resource: Cosmos account `movie-tracker-cosmos-demo` in resource group `RG-MovieTracker-Demo`, region `westus3`.
  - Documents the three configuration keys — `Cosmos:Endpoint`, `Cosmos:Database`, `Cosmos:Container` — with expected literal values (`database`, `chat-sessions`) and notes that `Cosmos:Endpoint` must be set locally via user-secrets or environment variable (`Cosmos__Endpoint`).
  - Documents that `AZURE_CLIENT_ID` must be UNSET locally so `DefaultAzureCredential` falls back to `AzureCliCredential` (otherwise it tries to authenticate as the deployed UAMI and fails).
  - Includes the one-time per-developer role-grant command using the built-in Cosmos DB Data Contributor role (`00000000-0000-0000-0000-000000000002`). Give it as a PowerShell-safe single line (this repo is Windows/PowerShell; bash `\` line-continuations do not run in `pwsh`):
    ```powershell
    az cosmosdb sql role assignment create --account-name movie-tracker-cosmos-demo --resource-group RG-MovieTracker-Demo --scope "/" --principal-id (az ad signed-in-user show --query id -o tsv) --role-definition-id 00000000-0000-0000-0000-000000000002
    ```
  - States the verification recipe: `az login`, then `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`, then `curl` `/health/cosmos` on the port shown in the console — expect 200 with the deployed account host in the body.

**Checkpoint**: US2 complete. A new developer can follow the README alone and reach a
green local probe (SC-004).

---

## Phase 7: Polish

- [ ] T011 Modify `CLAUDE.md`:
  - Under "Key Endpoints" add: `GET /health/cosmos - Cosmos DB connectivity probe (real read round trip via managed identity)`.
  - Under "Development Notes" add a bullet naming the three new configuration keys (`Cosmos:Endpoint`, `Cosmos:Database`, `Cosmos:Container`) and their env-var equivalents (`Cosmos__Endpoint`, `Cosmos__Database`, `Cosmos__Container`), and noting that `AZURE_CLIENT_ID` is set in the Container App to select the user-assigned MI for `DefaultAzureCredential`.
  - Under "Azure Resources" add: `Cosmos DB Account: movie-tracker-cosmos-demo (serverless, NoSQL vector search, westus3)`.
  - If the existing "Region: West US 2" line is present, update it to `westus3` to match the new deployment target.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: skipped.
- **Phase 2 (Bicep)**: no dependencies — can start immediately.
- **Phase 3 (csproj + serializer + appsettings)**: no dependency on Phase 2 at the file level; can start in parallel with Phase 2 if separate operators are working, but the Bicep MUST be applied to Azure before Phase 5 verification is meaningful.
- **Phase 4 (Program.cs config + DI)**: depends on Phase 3 (csproj + serializer + appsettings) — the packages, serializer type, and config keys must all exist.
- **Phase 5 (US1 endpoint)**: depends on Phase 4 (CosmosClient DI singleton) and on Phase 2 being deployed for a live 200.
- **Phase 6 (US2 doc)**: depends on Phase 5 — the endpoint the doc describes must exist before the local recipe is meaningful.
- **Phase 7 (Polish)**: depends on Phases 5 and 6 — the guidance documents what has shipped.

### Task-Level Dependencies

- T001 → T002 (main.bicep instantiates the cosmos module and reads its outputs)
- T002 → T003 (container-app.bicep receives cosmosEndpoint/Database/Container from main.bicep — but the file edit itself can proceed independently of T002 completing; parallel-safe with T004 because they touch different files)
- T002 → T004 (demo.parameters.json adds the `cosmosAccountName` parameter that main.bicep now declares)
- T005, T006, T007 are parallel-safe with each other (three distinct files)
- T005 → T008 (Program.cs uses `Microsoft.Azure.Cosmos`, `Azure.Identity`)
- T006 → T008 (Program.cs constructs `CosmosSystemTextJsonSerializer`)
- T007 → T008 (Program.cs reads the three `Cosmos:*` keys)
- T008 → T009 (endpoint handler resolves `CosmosClient` from DI and reads config)
- T009 → T010 (README describes the endpoint by name and expected shape)
- T010 → T011 (CLAUDE.md polish comes after README + code are stable)

Tasks marked `[P]` in the same phase are file-disjoint and may run in parallel.

MVP (US1 only) is T001 → T004a → T005..T008a → T009 → deploy → verify 200. T010–T011 add US2 and doc polish but are not required for the operator probe to pass.

---

## Notes

- No test-project tasks. Verification is exclusively the deployed and local `/health/cosmos` probe (Constitution Principle IV; plan.md §Testing).
- Every task lists the exact file it touches and every version, GUID, and resource name is stated verbatim — no TBDs, no placeholders in required identifiers.
- Nothing in this task list touches `appsettings.Development.json`, any Function App source, any chat-session model or repository, Key Vault as a configuration provider, or any other item in [spec.md](./spec.md) §Out of Scope.
- The `_ts` conflict-resolution path in T001 refers to the Cosmos-managed `_ts` timestamp property, not an application-defined field.
- The role-assignment `name` in T001 must be deterministic (`guid(...)`) so re-deploys are idempotent — a random name would attempt to create a new assignment on every deploy and fail on the second run.
