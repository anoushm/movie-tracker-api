# Feature Specification: Cosmos DB Provisioning and Container App Connectivity

**Feature Branch**: `001-cosmos-db`

**Created**: 2026-08-14

**Status**: Draft

**Input**: User description: "Import the Cosmos DB capability from movie-tracker-backend into movie-tracker-api. Provision the same Cosmos DB in Azure, then connect to it from the Azure Container App. Being able to connect to Cosmos DB from the ACA is sufficient for this task. Cosmos DB only - all other functionality is out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator confirms the deployed API can reach Cosmos DB (Priority: P1)

An operator deploys the infrastructure and container image, then opens a single URL and
learns definitively whether the running Container App can talk to Cosmos DB, and which
account and database it reached.

**Why this priority**: This is the entire point of the slice. Without a deliberate probe
the connection is unverifiable, because the Cosmos client connects lazily and a
misconfigured deployment reports itself as healthy until a real feature fails.

**Independent Test**: Deploy the infrastructure to an empty resource group, push the image,
and issue a single GET against the API's public FQDN. Delivers a trustworthy yes/no on
connectivity with no other feature present.

**Acceptance Scenarios**:

1. **Given** the infrastructure is deployed and the identity holds its Cosmos role,
   **When** the operator requests `GET /health/cosmos`,
   **Then** the response is 200 and names the Cosmos account and database that were reached.
2. **Given** the identity's Cosmos role assignment has been removed,
   **When** the operator requests `GET /health/cosmos`,
   **Then** the response is 503 and identifies authorization as the failure, without leaking credentials.
3. **Given** Cosmos DB is unreachable or the endpoint is wrong,
   **When** the operator requests `GET /health/cosmos`,
   **Then** the response is 503 within a bounded timeout rather than hanging until the client's default retry budget expires.
4. **Given** any state of Cosmos DB, healthy or not,
   **When** the platform probes `/health` and `/health/ready`,
   **Then** both still return success, so a Cosmos outage never causes the revision to be recycled.

---

### User Story 2 - Developer runs the API locally against the same Cosmos DB (Priority: P2)

A developer clones the repository, follows the README, and runs the API on their machine
with the same connectivity probe passing against the real Cosmos account.

**Why this priority**: Identity-based access has a genuine local-development cost that
key-based access did not. If the local story is undocumented, the next developer silently
reintroduces a connection string and violates Principle I.

**Independent Test**: On a machine with `az login` completed and the documented role
assignment granted, run the API and request `GET /health/cosmos`.

**Acceptance Scenarios**:

1. **Given** a developer signed in with the Azure CLI and holding the documented Cosmos data-plane role,
   **When** they run the API locally and request `GET /health/cosmos`,
   **Then** the response is 200 and names the same account and database as the deployed app.
2. **Given** a developer who has not been granted the data-plane role,
   **When** they request `GET /health/cosmos`,
   **Then** the failure message states which role is missing and on which account.

---

### User Story 3 - Data shape matches the Function App so later slices need no schema change (Priority: P3)

The provisioned database and container match the shape the Function App already uses, so a
later slice that moves chat-session storage inherits a compatible target.

**Why this priority**: Cheap to get right now and expensive to change later, but it delivers
no observable behaviour on its own, so it must not delay P1.

**Independent Test**: Compare the deployed database, container, partition key path, and
conflict-resolution policy against `movie-tracker-backend/infrastructure/cosmos.bicep`.

**Acceptance Scenarios**:

1. **Given** the deployed Cosmos account,
   **When** its database and container definitions are inspected,
   **Then** the database is named `database`, the container is named `chat-sessions`,
   the partition key is the hash path `/PartitionKey`, and the conflict-resolution policy is
   last-writer-wins on `/_ts`.
2. **Given** the deployed Cosmos account,
   **When** its capabilities are inspected,
   **Then** serverless capacity and NoSQL vector search are enabled, matching the Function App's account.

---

### Edge Cases

- Configuration is absent entirely (no endpoint, no database name): the API MUST fail at
  startup with a message naming the missing key, rather than starting and failing per-request.
- The managed identity exists but its data-plane role assignment has not finished
  propagating when the first probe arrives: the probe reports 503 with the authorization
  reason and succeeds on retry, rather than caching a failed state for the life of the process.
- The Cosmos account exists but the `chat-sessions` container does not: the probe distinguishes
  "reached the account" from "found the container", so a partial deployment is diagnosable.
- Two revisions run concurrently during a rollout: both resolve the same account, so the probe
  result does not depend on which replica answers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provision a Cosmos DB account in the API's resource group, declared in Bicep, configured for serverless capacity with NoSQL vector search enabled and a minimum TLS version of 1.2.
- **FR-002**: The system MUST provision a database named `database` containing a container named `chat-sessions`, partitioned by the hash path `/PartitionKey`, with a last-writer-wins conflict-resolution policy on `/_ts`.
- **FR-003**: The Container App MUST authenticate to Cosmos DB using its existing user-assigned managed identity. No Cosmos account key, connection string, or Key Vault secret may be created, stored, or referenced.
- **FR-004**: The system MUST grant that identity a least-privilege Cosmos data-plane role sufficient to read and write documents, declared in Bicep, scoped to the account.
- **FR-005**: The system MUST expose `GET /health/cosmos`, which performs a real round trip to Cosmos DB and returns 200 on success or 503 on failure.
- **FR-006**: The probe response MUST identify the Cosmos account and database it reached, and MUST NOT include credentials, tokens, or connection strings in any response or log.
- **FR-007**: The probe MUST complete within a bounded timeout on failure rather than inheriting the Cosmos client's full default retry budget.
- **FR-008**: `/health` and `/health/ready` MUST remain independent of Cosmos DB availability.
- **FR-009**: The Cosmos endpoint, database name, and container name MUST be supplied as non-secret configuration through application settings and container environment variables, consistent with the naming convention already used by this repository's container environment variables.
- **FR-010**: The application MUST register a single, shared Cosmos client for the process lifetime, configured with a serializer that preserves PascalCase property names so that documents written later remain compatible with the Function App's existing document shape.
- **FR-011**: The application MUST fail fast at startup, naming the missing configuration key, when required Cosmos configuration is absent.
- **FR-012**: The infrastructure MUST deploy to an empty resource group through the existing workflow with no manual pre-deployment step.
- **FR-013**: The README and CLAUDE.md MUST document the new resource, its configuration keys, and the local-development role grant.

### Out of Scope *(explicitly excluded from this feature)*

- Porting `ChatSessionRepository`, `MoviceTrackerChatSession`, or any session or movie model.
- Migrating any Function App endpoint (`Chat-Start`, `Chat-Ask`, `Chat-Ask-V2`) or its behaviour.
- Migrating, copying, or backfilling any existing data from the Function App's Cosmos account.
- Reading or writing real chat sessions from the Container App.
- Adopting Key Vault as a configuration provider in the API.
- Any other Function App dependency: Storage, AI Foundry / Azure OpenAI, Cognitive Search, or Application Insights changes.
- Rotating the secrets currently committed in `appsettings.Development.json` (a known, separately tracked defect).

### Key Entities

- **Cosmos DB account**: the top-level Azure resource that owns the endpoint, region, and capacity mode. One per environment, in the API's resource group.
- **Database (`database`)**: the logical container grouping inside the account. Name matches the Function App.
- **Container (`chat-sessions`)**: the document collection. Provisioned to match the Function App's shape; not read or written by application features in this slice.
- **Managed identity role assignment**: the data-plane grant that lets the Container App's identity act on the account. The sole authorization mechanism.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a deployment to an empty resource group and with no manual configuration step performed between deployment and request, `GET /health/cosmos` returns 200 on retry. A 503 with `authorization` as the reason during initial requests is expected data-plane role-assignment propagation (see Edge Case 2), not a failure.
- **SC-002**: A search of the repository, the deployed Key Vault, and the Container App's secret collection finds zero Cosmos keys or connection strings.
- **SC-003**: The full infrastructure deployment succeeds end to end without any documented manual pre-step, unlike the current Key Vault secret prerequisite.
- **SC-004**: A developer with no prior context can get `GET /health/cosmos` passing locally by following the README alone.
- **SC-005**: `/health` and `/health/ready` continue to return success while Cosmos DB is deliberately made unreachable.
- **SC-006**: A failing probe returns within 10 seconds.
- **SC-007**: The deployed database and container match the Function App's definitions on name, partition key path, and conflict-resolution policy.

## Assumptions

- A new, empty Cosmos account is provisioned for this API rather than sharing the Function App's account, so that nothing in this slice can affect the running Function App. Confirmed with the requester.
- Managed identity with data-plane RBAC is the chosen authentication mechanism, in preference to a Key Vault connection string. Confirmed with the requester.
- A dedicated `/health/cosmos` endpoint is the chosen verification mechanism. The Function App has no health endpoint of any kind, so there is no existing pattern to copy. Confirmed with the requester.
- The probe performs a read-only round trip. Write permission is granted by the role assignment but is not exercised by this slice.
- The existing user-assigned identity is reused; no new identity is introduced.
- The `demo` environment is the target for verification; other environment parameter files are not created by this feature.
- The `demo` environment is provisioned in the `westus3` region.
- Serverless capacity means no throughput configuration is required on the database or container.
