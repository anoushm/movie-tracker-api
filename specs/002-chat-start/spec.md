# Feature Specification: Chat-Start Session Persistence

**Feature Branch**: `002-chat-start`

**Created**: 2026-10-06

**Status**: Draft

**Input**: User description: "Import chat-session persistence from movie-tracker-backend into movie-tracker-api. Source: C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend\ChatSessionRepository.cs and the Chat-Start function plus its system prompt in Functions\Function.cs. Add GET /api/chat/start. It returns { chatId } and creates the session document in the existing Cosmos container (movie-tracker-cosmos), reached through the user-assigned managed identity with no connection string. Database and container names come from Cosmos:Database and Cosmos:Container config, not hard-coded. Old chat sessions in the Function App's Cosmos account are not migrated."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Caller opens a new chat and receives a session identifier (Priority: P1)

A client application opens a new conversation by calling `GET /api/chat/start` and
receives back an opaque session identifier. The identifier corresponds to a freshly
created session document that already carries the movie-expert system prompt, so the
first subsequent user turn can be answered without any further session setup.

**Why this priority**: This is the entire slice. Without it there is no way to begin a
conversation under the new API, and no later slice (`/ask`, hydration, telemetry) has
anywhere to attach its state. The deployed Cosmos container from slice 001 remains
unused until this endpoint writes to it.

**Independent Test**: Issue a single `GET /api/chat/start` against the deployed API's
public FQDN and against a locally running instance. In both cases the response
contains an identifier, and a point-read of the Cosmos container for that identifier
returns a document whose chat history contains the movie-expert system message.

**Acceptance Scenarios**:

1. **Given** the API is running and the identity holds its Cosmos data-plane role,
   **When** a caller requests `GET /api/chat/start`,
   **Then** the response is 200 and the body is a JSON object with a single `chatId`
   string property.
2. **Given** a successful call to `GET /api/chat/start` returning `chatId` X,
   **When** the Cosmos container is point-read with id X and partition key X,
   **Then** a session document exists, its id and partition key both equal X, and its
   chat history contains exactly one message whose role is "system" and whose text is
   the movie-expert system prompt.
3. **Given** two concurrent callers invoke `GET /api/chat/start`,
   **When** both responses are compared,
   **Then** each receives a distinct `chatId` and both documents are present in
   Cosmos.
4. **Given** the Cosmos endpoint is reachable but the identity lacks write permission,
   **When** a caller requests `GET /api/chat/start`,
   **Then** the response is a 5xx error and the response body does not contain
   credentials, tokens, or connection strings.

---

### User Story 2 - Session identifiers are URL-safe (Priority: P2)

A client embeds the returned `chatId` in a subsequent URL path or query without
needing to encode it.

**Why this priority**: The Function App's existing clients already pass the id through
URL positions where unencoded characters cause breakage. Preserving that id shape is
cheap now and expensive to change later.

**Independent Test**: A unit test on the generator asserts that produced characters
fall in the allowed set across a sample run. A single `GET /api/chat/start` returns
an identifier matching the same rule.

**Acceptance Scenarios**:

1. **Given** a response from `GET /api/chat/start`,
   **When** the `chatId` is inspected,
   **Then** every character is from the URL-unreserved set (letters, digits, or any
   subset of `-._~` that excludes the characters previously rejected by the Function
   App: `-` and `~`).

---

### Edge Cases

- Cosmos configuration is missing when the application is already running (hot-swap of
  settings): behaviour is undefined; this feature inherits the fail-fast-at-startup
  guarantee from slice 001 and does not re-check configuration per request.
- The Cosmos account exists but the configured container does not: the response is a
  5xx error. Exact classification of the underlying Cosmos failure is not part of the
  response contract.
- A random identifier is generated that happens to contain a disallowed character:
  the application retries the generation rather than returning a reserved character to
  the caller, matching the Function App's existing behaviour.
- The Cosmos write succeeds but the response is lost in transit: the caller retries
  and receives a different `chatId` pointing at a different document. Orphaned
  documents from the previous attempt remain in the container; cleaning them is out
  of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose `GET /api/chat/start` and respond with a JSON body
  containing a single `chatId` string property. No other properties, headers, or
  side-effects are part of the contract.
- **FR-002**: The system MUST, before responding, create a session document in Cosmos
  DB whose `id` and partition key both equal the returned `chatId`.
- **FR-003**: The created document MUST contain a chat history populated with exactly
  one system message, whose text is the movie-expert system prompt reproduced
  verbatim from the Function App's `Chat-Start` implementation.
- **FR-004**: The created document MUST preserve the Function App's document shape on
  the properties exercised by this slice: an `id` string, a `PartitionKey` string, a
  `ChatHistory` array of messages, a nullable `FunnyFact`, and a nullable
  `HydratedMovies` map. Properties not exercised in this slice remain null or absent.
- **FR-005**: The chat history message element MUST be serialized in a shape the
  Agent Framework (`Microsoft.Extensions.AI.ChatMessage`) can round-trip. The Function
  App's note that pre-migration documents cannot be loaded is accepted; forward
  compatibility starts at this slice.
- **FR-006**: The system MUST connect to Cosmos DB using the Container App's
  user-assigned managed identity. No Cosmos account key, connection string, SAS, or
  Key Vault secret may be introduced by this feature.
- **FR-007**: The database name and container name used for the write MUST be read
  from the `Cosmos:Database` and `Cosmos:Container` configuration keys that slice 001
  already requires. Hard-coding either name in source is prohibited.
- **FR-008**: The system MUST generate session identifiers that contain only
  characters from the URL-unreserved set, and MUST exclude the characters already
  rejected by the Function App's identifier generator (`-`, `~`). The generator MUST
  retry when a candidate contains a rejected character.
- **FR-009**: Identifiers MUST be generated from a cryptographically strong random
  source, matching the Function App's generator (five random bytes, URL-safe
  base64, retry on rejected characters). Collision handling beyond that is out of
  scope: the source performs none and this slice does not add it.
- **FR-010**: The endpoint MUST return a 5xx error if the Cosmos write fails, and
  the response body MUST NOT include credentials, tokens, or connection strings.
  No further structure of the error body is required by this slice.
- **FR-011**: No existing endpoint behaviour MUST change as a result of this feature.
  In particular, `/health`, `/health/ready`, `/health/cosmos`, `/version`, and the
  current `/ask` endpoint must behave identically before and after this change.
- **FR-012**: The feature MUST NOT introduce new Azure resources, new role
  assignments, or new container environment variables beyond those already provisioned
  by slice 001.

### Out of Scope *(explicitly excluded from this feature)*

- Any `/ask` endpoint behaviour: no changes to `POST /ask` and no port of
  `Chat-Ask` or `Chat-Ask-V2` from the Function App.
- The movie-expert agent itself, its tools, its tool-calling flow, and any TMDb or
  Azure OpenAI integration beyond what is already wired in the API.
- Reading, mutating, or hydrating chat sessions after creation. The repository
  surface in this slice is write-only create.
- Hydration of `HydratedMovies` or any TMDb round trip triggered by chat flow.
- Telemetry, tracing spans, custom metrics, or structured logging additions beyond
  the ambient logging already present.
- Any frontend change, SDK, OpenAPI client regeneration, or sample client update.
- Migrating, copying, or backfilling existing chat sessions from the Function App's
  Cosmos account. Those documents stay where they are and are not read by this API.
- Deprecating, redirecting, or signalling anything on the Function App's existing
  `Chat-Start` endpoint.
- Introducing a persistence abstraction, repository pattern, or interface indirection
  broader than what this one endpoint requires.

### Key Entities

- **Chat session document**: the Cosmos document created by this endpoint. Owns
  `id`, `PartitionKey`, `ChatHistory` (initially one system message), and reserved
  nullable slots for later slices.
- **Session identifier (`chatId`)**: the single opaque string returned to the caller.
  Equal to the document's `id` and its partition key. URL-safe and non-guessable.
- **Movie-expert system prompt**: the fixed text seeded as the first message of
  every new session. Reproduced from the Function App; not configurable in this
  slice.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller can obtain a new session identifier against the deployed API
  with a single `GET` request and no prior setup, measured by a successful 200
  response at the public FQDN.
- **SC-002**: A successful `GET /api/chat/start` leaves one new document in Cosmos
  whose id equals the returned identifier, verified by a point-read in a small
  integration smoke test.
- **SC-003**: A search of this repository finds zero new Cosmos keys, connection
  strings, or SAS tokens introduced by this feature.
- **SC-004**: `/health`, `/health/ready`, `/health/cosmos`, `/version`, and the
  current `/ask` endpoint continue to return their existing successful responses,
  verified by a focused smoke run after the change.
- **SC-005**: A unit test on the id generator confirms produced characters fall in
  the URL-unreserved set excluding `-` and `~`, matching the Function App's rule.
- **SC-006**: A failing Cosmos write surfaces to the caller as a 5xx response.

## Assumptions

- Slice 001 ("Cosmos DB Provisioning and Container App Connectivity") is deployed and
  verified. This feature consumes the account, database, container, identity, and
  role assignment that slice provisioned, and introduces none of its own.
- The configured container's partition key path is `/PartitionKey`, matching slice
  001's provisioning. The document's `PartitionKey` property is populated with the
  same value as `id`, matching the Function App's convention.
- The movie-expert system prompt is treated as fixed text for this slice. If it must
  vary per environment or per caller later, that is a follow-up and introduces a new
  configuration key, not a code path in this slice.
- The endpoint is anonymous, matching the Function App's `Chat-Start` authorization
  level. End-user authentication for chat sessions is a future concern.
- The Container App already sets `AZURE_CLIENT_ID` to select the user-assigned
  managed identity for `DefaultAzureCredential`, and the local development story
  established by slice 001's README continues to apply unchanged.
- The id generator follows the Function App's scheme (five random bytes, URL-safe
  base64, retry on `-`/`~`) rather than inventing a new encoding. Changing the
  scheme is a separate decision, not part of this slice.
- The existing `CosmosClient` registration from slice 001 is reused as the single
  shared client; no second client is introduced.
