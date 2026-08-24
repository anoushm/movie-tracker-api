# Implementation Plan: Cosmos DB Provisioning and Container App Connectivity

**Branch**: `001-cosmos-db` | **Date**: 2026-08-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-cosmos-db/spec.md`

## Summary

Provision a new Cosmos DB account in `RG-MovieTracker-Demo`, matching the Function App's
shape (serverless + NoSQL vector search, database `database`, container `chat-sessions`,
partition key `/PartitionKey` Hash, LastWriterWins on `/_ts`, minTLS 1.2). Authorize the
Container App via its existing user-assigned managed identity (`movie-tracker-api-identity`)
plus a Cosmos data-plane SQL role assignment scoped to the new account — no keys, no
connection strings, no Key Vault secret. Expose `GET /health/cosmos` performing a real
read round trip within a 10 s bounded timeout, without disturbing `/health` or
`/health/ready`. Port `CosmosSystemTextJsonSerializer` verbatim (namespace only) so the
container's document shape matches the Function App byte-for-byte.

## Technical Context

**Language/Version**: C# / .NET 10 (already the project baseline).

**Primary Dependencies**:
- `Microsoft.Azure.Cosmos` — NEW; pinned to `3.62.0` to match the Function App's
  currently-deployed SDK version, so serialization and client behaviour are identical
  across both services.
- `Azure.Identity` — NEW; provides `DefaultAzureCredential`, which resolves the
  user-assigned MI in ACA (via `AZURE_CLIENT_ID`) and `az login` credentials locally.
- `Microsoft.Extensions.AI` — NEW as an EXPLICIT `PackageReference`. It is present
  transitively today (via `Microsoft.Agents.AI`), but the ported serializer's
  `AIJsonUtilities.DefaultOptions` type resolver is a direct compile-time dependency
  and must not be left to transitive resolution.

**Storage**: Azure Cosmos DB for NoSQL, serverless, single region (`westus3`, from
`resourceGroup().location`).

**Testing**: None added by this slice. Per Constitution Principle IV, the
`/health/cosmos` probe is itself the verification surface; there is no separate test
project to introduce. Local and deployed manual verification is enumerated in
[spec.md](./spec.md) Acceptance Scenarios and Success Criteria SC-001..SC-007.

**Target Platform**: Linux container on Azure Container Apps (Consumption profile,
single active revision, port 8080), plus local dev on Windows via `dotnet run`.

**Project Type**: Single ASP.NET Core web service (existing `MovieTracker.Api/`
project). No new projects.

**Performance Goals**: Probe failure returns within 10 s (SC-006). No steady-state
throughput target — no user traffic touches Cosmos in this slice.

**Constraints**:
- No account keys, connection strings, or KV-stored secrets (Principle I + FR-003).
- Configuration reaches the container via non-secret env vars only (FR-009, Principle V).
- `/health` and `/health/ready` remain Cosmos-independent (FR-008, SC-005).
- Fail fast at startup on missing config (FR-011, edge case #1).
- No document IO from application features in this slice (out-of-scope list is binding).

**Scale/Scope**: One environment (`demo`). One new Bicep file (`cosmos.bicep`), three
modified Bicep files (`main.bicep`, `container-app.bicep`, `demo.parameters.json`),
four new env vars (`Cosmos__Endpoint`, `Cosmos__Database`, `Cosmos__Container`,
`AZURE_CLIENT_ID`), one new module wired into `main.bicep`, one new endpoint mapped in
`Program.cs`, one ported serializer class, one DI singleton, README + CLAUDE.md
updates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates are derived from `.specify/memory/constitution.md` v1.0.0.

| Principle | Compliance |
|-----------|------------|
| **I. Managed Identity Over Secrets (NON-NEGOTIABLE)** | PASS. Cosmos auth is the existing UAMI plus a `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments` for the built-in Cosmos DB Data Contributor role, all declared in Bicep. No account key, connection string, SAS, or KV secret is created, stored, or referenced. |
| **II. Infrastructure Is Bicep, Not Clicks** | PASS. New `infrastructure/cosmos.bicep` module + wiring in `infrastructure/main.bicep`. The role assignment (data plane) and the endpoint env var flow-through in `container-app.bicep` are declared. No portal steps, no imperative `az` commands in the deployment path. Local-dev role grant is documentation only, outside the deployed system. |
| **III. Incremental Migration With Declared Boundaries** | PASS. The Out-of-Scope list in [spec.md](./spec.md) is binding: no repository/model porting, no Function App endpoint migration, no data migration, no application reads/writes, no Key Vault provider adoption. The slice is complete when `GET /health/cosmos` returns 200 in `demo` (SC-001). |
| **IV. Dependencies Prove Themselves** | PASS. `GET /health/cosmos` performs a real Cosmos round trip and names the account and database (FR-005, FR-006). Startup validation prevents lazy discovery of missing config (FR-011). |
| **V. No Secret Material In Source** | PASS. New config keys (`Cosmos__Endpoint`, `Cosmos__Database`, `Cosmos__Container`) are non-secret. `Cosmos__Database` and `Cosmos__Container` are literals in `appsettings.json`; `Cosmos__Endpoint` is injected per-environment via `container-app.bicep`'s `envVars` as a plain value, not `secretRef`, and is intentionally absent from `appsettings.json` so a missing value fails startup. |

**Initial gate**: PASS with no violations. `Complexity Tracking` section is empty.

**Post-design re-check**: PASS. No design decision below introduces a
constitution-violating shortcut. In particular, the probe deliberately does not fall
back to a key on failure (Principle I), and the config validation lives in `Program.cs`
before the host is built so a bad deployment fails to start rather than passing a broken
client to DI (Principle IV).

## Project Structure

### Documentation (this feature)

```text
specs/001-cosmos-db/
├── spec.md              # feature specification (input)
└── plan.md              # this file
```

`research.md`, `data-model.md`, `quickstart.md`, and `contracts/` are intentionally
omitted:

- **research.md**: no unknowns. All decisions are pre-committed (auth model, account
  location, shape, verification approach, serializer source). The shape's source of
  truth is `C:\projects\dev\movie-tracker-backend\infrastructure\cosmos.bicep`.
- **data-model.md**: this slice writes no domain data. The container shape is fully
  described by FR-002 and by the referenced Function App Bicep.
- **contracts/**: the only new interface is `GET /health/cosmos`, whose contract is
  described inline under "Endpoint contract" below.
- **quickstart.md**: local-dev run instructions belong in `README.md` per FR-013 and
  Constitution Principle V (documentation lives with the code, not beside the spec).

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                              # MODIFY: add Cosmos config validation,
│                                           #         DI singleton, /health/cosmos map
├── Core/
│   └── CosmosSystemTextJsonSerializer.cs   # NEW: ported verbatim from backend repo,
│                                           #      namespace changed to MovieTracker.Api.Core,
│                                           #      PropertyNamingPolicy stays null
├── appsettings.json                        # MODIFY: add non-secret Cosmos:Database
│                                           #         and Cosmos:Container literals.
│                                           #         Cosmos:Endpoint is intentionally
│                                           #         OMITTED so a deploy that forgets
│                                           #         to inject it fails startup.
└── MovieTracker.Api.csproj                 # MODIFY: add PackageReferences for
                                            #         Microsoft.Azure.Cosmos (pin 3.62.0),
                                            #         Azure.Identity, and
                                            #         Microsoft.Extensions.AI (explicit —
                                            #         currently only transitive)

infrastructure/
├── cosmos.bicep                            # NEW: account + database + container +
│                                           #      sqlRoleAssignment for the UAMI
├── main.bicep                              # MODIFY: declare cosmosAccountName param,
│                                           #         instantiate cosmos module (passing
│                                           #         the name + managedIdentity.principalId),
│                                           #         wire cosmos endpoint output into
│                                           #         container-app module params
├── container-app.bicep                     # MODIFY: add Cosmos__Endpoint,
│                                           #         Cosmos__Database, Cosmos__Container,
│                                           #         and AZURE_CLIENT_ID to envVars
│                                           #         (all plain, no secretRef)
└── demo.parameters.json                    # MODIFY: add cosmosAccountName parameter
                                            #         with an explicit value (globally
                                            #         unique — override on collision)

README.md                                    # MODIFY: FR-013 — new resource, config keys,
                                             #         local-dev role-grant recipe
CLAUDE.md                                    # MODIFY: FR-013 — runtime guidance for the
                                             #         new endpoint and env vars
```

**Structure Decision**: single ASP.NET Core project, existing `MovieTracker.Api/`
layout preserved. New serializer lives under `Core/` beside future infrastructure
helpers. No `Services/` or `Repositories/` folder is introduced — this slice has no
application-level Cosmos access; the probe is deliberately a bare-metal
`Container.ReadContainerAsync` call at the endpoint site to keep the surface minimal
and to avoid seeding abstractions that would prejudge later slices (Principle III).

## Design Details

The following subsections capture the design decisions that would otherwise live in
`research.md` / `data-model.md` / `contracts/`. Everything here is derived from the
pre-committed decisions in the feature input and from the two reference files.

### Cosmos account and shape (FR-001, FR-002, SC-007)

The new `infrastructure/cosmos.bicep` module mirrors
`movie-tracker-backend/infrastructure/cosmos.bicep` on the following properties:

- `kind: 'GlobalDocumentDB'`, single region, `failoverPriority: 0`,
  `isZoneRedundant: false`.
- `minimalTlsVersion: 'Tls12'`.
- `disableLocalAuth: true` — Principle I compliance requires more than "we don't
  reference the key"; the account must reject key-based data-plane auth entirely.
  With this flag the account keys still exist in ARM (key-management APIs may still
  list them under sufficient control-plane permissions) but are non-functional for
  data-plane authentication — only AAD-issued tokens succeed. That is the property
  the constitution actually needs.
- `capabilities`: `EnableServerless`, `EnableNoSQLVectorSearch`.
- `enableFreeTier: false`, `databaseAccountOfferType: 'Standard'`.
- `capacity.totalThroughputLimit: 4000`.
- Child `sqlDatabases` named `database` (no `throughput` — serverless), with
  `properties.resource.id: 'database'` (required by the ARM schema).
- Child `sqlDatabases/containers` named `chat-sessions` with
  `properties.resource.id: 'chat-sessions'`:
  - `partitionKey`: `{ kind: 'Hash', paths: ['/PartitionKey'] }`.
  - `conflictResolutionPolicy`: `{ mode: 'LastWriterWins', conflictResolutionPath: '/_ts' }`.

Deviations from the reference file, driven by this repository's constitution:

- **Account name is an explicit parameter, not `uniqueString()`-derived** (Azure
  Platform Constraints line 78: "The `uniqueString()` suffix convention used by
  `movie-tracker-backend` is NOT carried into this repository"). `main.bicep` declares
  a `cosmosAccountName` parameter, threads it into the cosmos module, and
  `demo.parameters.json` carries an explicit value from day one — Cosmos account names
  are globally unique, so a collision must be a one-line param change, not a failed
  deployment discovered mid-rollout.
- **No `SystemAssigned` identity on the account** (the reference file has one but does
  not use it; this repo authorizes the caller, not the callee).
- **`adminPrincipalIds` control-plane role assignment loop is not carried over.** This
  repo grants only the data-plane role needed by the Container App.

### Data-plane authorization (FR-003, FR-004, Principle I)

The Container App's existing UAMI (`movie-tracker-api-identity`, already declared in
`main.bicep` line 61) is granted the built-in **Cosmos DB Built-in Data Contributor**
role at account scope via a `Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments`
resource inside `cosmos.bicep`:

- `roleDefinitionId`: `<account>/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002`
  (Cosmos DB Built-in Data Contributor — read + write documents; no control plane).
- `principalId`: `managedIdentity.properties.principalId` (piped from `main.bicep`).
- `scope`: the account's resource ID (full read/write across the account is acceptable
  because only `chat-sessions` exists).

This is the *sole* authorization mechanism. No `listConnectionStrings` output, no KV
secret provisioning, no Cosmos primary/secondary key reference anywhere.

### Configuration keys (FR-009, FR-011)

Three non-secret keys, using this repository's established double-underscore env-var
convention (see `container-app.bicep` lines 47–54):

| Key (config path) | Env var name | Source | Example value |
|---|---|---|---|
| `Cosmos:Endpoint` | `Cosmos__Endpoint` | `cosmos.bicep` output → `main.bicep` → `container-app.bicep` envVars | `https://movie-tracker-cosmos-demo.documents.azure.com:443/` |
| `Cosmos:Database` | `Cosmos__Database` | plain literal in `container-app.bicep` | `database` |
| `Cosmos:Container` | `Cosmos__Container` | plain literal in `container-app.bicep` | `chat-sessions` |

An additional env var is required for the DI wiring below:

| Env var | Value | Purpose |
|---|---|---|
| `AZURE_CLIENT_ID` | `managedIdentityClientId` (already output by `main.bicep`) | Tells `DefaultAzureCredential` which user-assigned MI to select when multiple credentials are available in ACA. |

`Program.cs` reads all three `Cosmos:*` keys before `builder.Build()`; a null on any
throws `InvalidOperationException("Missing configuration value: Cosmos:<key>")`
(matching the pattern already in `Program.cs` line 14) so the container crash-loops
rather than starting broken.

`appsettings.json` carries the two environment-invariant keys (`Cosmos:Database`,
`Cosmos:Container`) as literals but INTENTIONALLY OMITS `Cosmos:Endpoint`. The
endpoint is environment-specific and must be injected at deploy time (`Cosmos__Endpoint`
env var in ACA; user-secrets locally). A `"<set-in-env>"` placeholder would defeat
the null-check because the value would be non-null but wrong, converting the required
fail-fast startup error into a lazy runtime failure on the first probe.

### CosmosClient DI wiring (FR-010, Principle IV)

`Program.cs` registers exactly one `CosmosClient` as a singleton, using the ported
serializer:

- Credential: `new DefaultAzureCredential()` — resolves the ACA user-assigned MI via
  `AZURE_CLIENT_ID` in production and falls back to `AzureCliCredential` for local dev
  after `az login`.
- `CosmosClientOptions.Serializer = new CosmosSystemTextJsonSerializer()` — the ported
  class with `PropertyNamingPolicy = null` and the combined `AIJsonUtilities` +
  `DefaultJsonTypeInfoResolver`, so future documents remain byte-compatible with the
  Function App's PascalCase shape (notably `PartitionKey`).
- No `RequestTimeout` override at client level; the per-request timeout is enforced by
  a `CancellationTokenSource` at the probe call site (see next subsection) so that the
  bounded-timeout requirement (FR-007, SC-006) cannot regress if a later slice
  reconfigures the shared client.

### Serializer port (FR-010)

`Core/CosmosSystemTextJsonSerializer.cs` is a copy of
`C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend\CosmosSystemTextJsonSerializer.cs`,
with these changes:

1. `namespace MovieTracker.Backend` → `namespace MovieTracker.Api.Core`.
2. `using` list pruned to what the file actually references (linq/text/tasks
   usings are unused).
3. Class-level XML doc comment stripped — this repo enforces a no-inline-comments
   standard. The design rationale (`AIJsonUtilities` combination, `null`
   `PropertyNamingPolicy`) lives in this section of `plan.md`, not in the source
   file.

Behavior is preserved: `PropertyNamingPolicy = null` and the combined
`AIJsonUtilities.DefaultOptions.TypeInfoResolver` + `DefaultJsonTypeInfoResolver`
resolver. Do not "modernize" the code (no records, no primary constructors, no
expression-bodied overrides) — an auditable diff against the source file makes
future upstream sync trivial if the backend serializer changes.

### `/health/cosmos` endpoint contract (FR-005, FR-006, FR-007, FR-008)

Wired the same way as `/health` and `/health/ready` — a minimal endpoint in
`Program.cs`, not a controller:

```text
GET /health/cosmos
```

Handler behaviour:

1. Create `CancellationTokenSource cts = new(TimeSpan.FromSeconds(10))`. All Cosmos
   calls pass `cts.Token`.
2. Resolve `Container container = cosmosClient.GetContainer(config["Cosmos:Database"], config["Cosmos:Container"])`. Both names come from configuration — no string literals in the handler.
3. Perform a real round trip: `await container.ReadContainerAsync(cancellationToken: cts.Token)`.
   This exercises endpoint reachability, TCP + TLS, AAD token acquisition, data-plane
   authorization, database presence, and container presence in a single call — and
   distinguishes "reached the account" from "found the container" via the exception
   `SubStatusCode` / `StatusCode` (edge case #3).
4. Return `200 OK` with body:
   ```json
   { "status": "ok",
     "account":   "<host component of Cosmos:Endpoint>",
     "database":  "<Cosmos:Database>",
     "container": "<Cosmos:Container>" }
   ```
5. On `CosmosException`:
   - `StatusCode == 401 or 403` → `503`, `reason: "authorization"`, plus the account
     host, database name, and a `requiredRole: "Cosmos DB Built-in Data Contributor"`
     field (satisfies US2 acceptance scenario #2). The role name is a static
     known-at-implementation-time literal, not exception content, so FR-006 is not
     violated.
   - `StatusCode == 404` → `503`, `reason: "not-found"`. The response body names both
     database and container so the operator knows the search set; a single
     `ReadContainerAsync` does not itself distinguish "database missing" from
     "container missing" — that distinction is left to portal/CLI follow-up.
   - Other → `503`, `reason: "unavailable"`.
6. On `Azure.Identity.AuthenticationFailedException` (credential acquisition
   failed — no `az login`, wrong tenant, MI not attached) → `503`,
   `reason: "authorization"`. Without this catch, the exception is unhandled and
   returns 500, violating FR-005 ("503 on failure").
7. On `TaskCanceledException` / `OperationCanceledException` → `503`,
   `reason: "timeout"`.

`/health` and `/health/ready` are NOT touched. Their handlers remain literal
`Results.Ok("healthy")` / `Results.Ok("ready")` with no Cosmos dependency (FR-008,
SC-005, edge case #4).

### Deployment ordering (FR-012, Constitution "Development Workflow")

Deployment order is expressed purely as Bicep resource references — no manual
pre-deployment step, no documented "run this first" instruction (which the constitution
labels a design defect):

1. `cosmos.bicep` module runs after `managedIdentity` exists in `main.bicep`, because
   the `sqlRoleAssignments` resource inside `cosmos.bicep` references
   `managedIdentity.properties.principalId` passed as a parameter.
2. `container-app.bicep` gains an implicit dependency on the cosmos module via the
   `cosmosEndpoint`, `cosmosDatabase`, and `cosmosContainer` parameters it now receives
   from `cosmos.outputs.*` in `main.bicep`. Bicep infers `dependsOn` from output
   references, so no explicit `dependsOn: [cosmos]` is needed — the whole cosmos module
   (account + database + container + `sqlRoleAssignment`) is serialized before the
   container-app module. This does NOT eliminate AAD propagation of the role assignment
   inside Cosmos, which is why the first probes after a fresh deploy can still return
   503; see [spec.md](./spec.md) Edge Case 2 and SC-001.

Result: `az deployment group create` against an empty RG succeeds in one shot without a
manual pre-step (SC-003), and `GET /health/cosmos` returns 200 on retry (SC-001 permits
initial 503s during Cosmos role-assignment propagation — see spec Edge Case 2).

### Local development story (FR-013, Spec User Story 2)

`README.md` and `CLAUDE.md` are updated to state:

- New env vars used by the API: `Cosmos__Endpoint`, `Cosmos__Database`,
  `Cosmos__Container`, and (locally) `AZURE_CLIENT_ID` unset so `DefaultAzureCredential`
  falls back to `AzureCliCredential`.
- Local role grant (one-shot, per developer):
  ```text
  az cosmosdb sql role assignment create \
    --account-name movie-tracker-cosmos-demo \
    --resource-group RG-MovieTracker-Demo \
    --scope "/" \
    --principal-id <developer object id from `az ad signed-in-user show`> \
    --role-definition-id 00000000-0000-0000-0000-000000000002
  ```
- Prerequisite: `az login` against the tenant that owns the account.
- Verification: `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`, then
  `curl http://localhost:8080/health/cosmos` returns 200 with the deployed account
  name — matching SC-004.

The role grant is intentionally a documented developer action, not a Bicep resource;
adding developer principals to `main.bicep` would violate the constraint that IaC
represents the deployed system, not developer laptops.

## Complexity Tracking

*Fill ONLY if Constitution Check has violations that must be justified.*

No violations. This section is intentionally empty.
