# Feature Specification: Agent Turn With TMDb Tools And Hydration

**Feature Branch**: `003-agent-turn`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "Import the agent turn from movie-tracker-backend into movie-tracker-api (slice 003). Add POST /api/chat/{chatId}/ask and POST /api/chat/{chatId}/ask/v2 against the single agent configured in Program.cs, extend ChatSessionRepository with read and save, add HydratedMovies to MoviceTrackerChatSession, replace Tools/TheMovieDBTool.cs with the backend's TMDb tool set wired through one TMDbClient singleton, keep POST /ask stateless on the same single agent, and remove the extra agent registrations. Use the existing AzureOpenAI config (Endpoint/ApiKey/DeploymentName) only. Out of scope: ChatPlanner tools (ratings, trailers, Wikipedia, funny fact), OMDb, custom telemetry, the reasoning-effort setting, frontend changes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Caller takes a turn in an existing chat and gets back the whole hydrated transcript (Priority: P1)

A client that already holds a `chatId` from slice 002 sends a user message to
`POST /api/chat/{chatId}/ask`. The API retrieves the session, runs one turn against
the single agent configured in `Program.cs`, appends the new messages to the stored
history, hydrates every movie id the transcript references, and returns the full
replayed conversation in the exact shape the Function App returns today.

**Why this priority**: This is the slice. Slice 002 made the Cosmos session useful
only once (at creation); until a turn can be taken against it, the deployed API has
no conversational behaviour and no caller can use it in place of the Function App.
P1 because the whole-transcript shape is what the current frontend consumes - the
v2 single-turn shape (US2) is additive on top.

**Independent Test**: Manual, like slice 002. Call `GET /api/chat/start` to
obtain a `chatId`, then `POST /api/chat/{chatId}/ask` with a real movie
question. The response body parses as `{ funnyFact, messages }` matching the
Function App's wire JSON shape (camelCase outer, lowercase `role`
discriminator); every assistant message carries a hydrated `movieList` whose
items contain the fields enumerated in FR-010; a Cosmos point-read on `chatId`
shows the user message and the model's response appended to the session's
chat history; a second turn against the same session returns the first turn's
user+assistant pair in `messages` unchanged. The same request against the
Function App's `Chat-Ask` endpoint produces a response with the same
top-level and per-message JSON shape. Run the check locally and against the
deployed revision.

**Acceptance Scenarios**:

1. **Given** a session created by slice 002 containing only the seeded system
   message, **When** a caller sends `POST /api/chat/{chatId}/ask` with
   `{ "Input": "best Christopher Nolan films" }`, **Then** the response is
   200 and the body is a JSON object with properties `funnyFact` (null) and
   `messages` (a JSON array). The array contains exactly one user entry
   (`role` = "user", `text` equal to the posted input) followed by exactly
   one assistant entry (`role` = "assistant", `text` equal to the parsed
   `SystemMessage` string the model emitted, `movieList` carrying one
   hydrated movie view model for every `MovieId` the model emitted that
   TMDb resolved, in the model's chosen order).
2. **Given** the same session after a successful first turn whose response
   referenced two movies, **When** the caller sends a second
   `POST /api/chat/{chatId}/ask`, **Then** the response's `messages` array
   contains the first turn's user+assistant pair in their original order,
   followed by the new turn's user+assistant pair. A Cosmos point-read on
   the session after the second turn shows both first-turn movie ids
   present in `HydratedMovies`, so a repeat reference on the second turn is
   served from the per-session hydration cache (FR-011) without a fresh
   TMDb round trip.
3. **Given** a `chatId` that does not exist in the Cosmos container, **When**
   `POST /api/chat/{chatId}/ask` is called, **Then** the response is 404 with
   a generic body, and no new document is created in Cosmos.
4. **Given** the Cosmos endpoint is reachable but the point-read fails for any
   reason other than not-found, **When** `POST /api/chat/{chatId}/ask` is called,
   **Then** the response is 503 with a generic error body, the server logs the
   exception, and the body does not contain credentials, tokens, connection
   strings, or the exception message.
5. **Given** a session on which 14 turns have already been taken,
   **When** the caller sends a 15th `POST /api/chat/{chatId}/ask` and
   that turn produces exactly one non-empty final assistant message,
   **Then** the response is 200, the response's `messages` array still
   contains every pre-existing user+assistant pair in its original
   order plus the new turn's user+assistant pair, and a Cosmos
   point-read on the session shows `ChatHistory` still contains every
   turn's messages (the reducer runs on a copy per FR-012).

---

### User Story 2 - Caller takes a turn and gets back only that turn (Priority: P2)

A client that is already tracking the earlier conversation locally sends a user
message to `POST /api/chat/{chatId}/ask/v2`. The server runs the same turn against
the same session, persists the same way, and returns only the single assistant
turn just produced, hydrated.

**Why this priority**: This is the Function App's v2 route and must be imported to
preserve the Function App's response surface. It is P2 because the slice is viable
without it (US1 handles the frontend's current shape), and the v2 payload is a
subset of what v1 produces.

**Independent Test**: Manual. Obtain a `chatId`, then call
`POST /api/chat/{chatId}/ask/v2` with a real user message. The response body
parses against `ChatTurnResponse` (exactly `funnyFact` and `turn` at the top
level, with `turn.role` = "assistant"), and an immediate Cosmos point-read
on the session document shows the last assistant message in stored
`ChatHistory` equal (after parsing its `SystemMessage`) to `turn.text`. The
same request against the Function App's `Chat-Ask-V2` endpoint produces a
response with the same top-level and `turn` JSON shape. Run the check
locally and against the deployed revision.

**Acceptance Scenarios**:

1. **Given** a session with any prior history, **When** `POST /api/chat/{chatId}/ask/v2`
   is called with a user message, **Then** the response is 200, the body parses
   against the `ChatTurnResponse` shape (`funnyFact` nullable string plus a single
   `turn` object), `turn.role` = "assistant", and `turn.movieList` contains a
   hydrated entry for every id in the model's structured reply that TMDb
   successfully resolved.
2. **Given** `/ask` and `/ask/v2` are both called interleaved on the same session,
   **When** `/ask` is called after `/ask/v2`, **Then** the transcript returned by
   `/ask` contains every turn that ran through either endpoint, in the order they
   ran.
3. **Given** a turn whose model reply carries `MovieList: []`, **When** v2
   responds, **Then** the response is 200 with `turn.movieList: []`.
   (Tool-driven TMDb calls the model made while composing the answer are
   not constrained by this scenario; only the per-turn hydration path is.)

---

### User Story 3 - Stateless /ask continues to answer against the same agent (Priority: P3)


A caller that does not need session persistence sends a one-shot question to the
existing `POST /ask` endpoint and gets back a plain answer, served by the same
single agent that `/api/chat/{chatId}/ask` uses.

**Why this priority**: Preserves the existing endpoint's contract so no caller
breaks, and does so without maintaining a second agent instance or a second tool
registration. P3 because this slice's new value is in `/api/chat/{chatId}/ask`;
`/ask` is kept working, not improved.

**Independent Test**: Manual. Call `POST /ask` with a plain question before
and after this feature deploys. Both calls return 200 with the same wire
response shape (`{ "answer": string }`, ASP.NET Core default camelCase of
`AskResponseDot`). After deploy, a review of `Program.cs` confirms `/ask`,
`/api/chat/{chatId}/ask`, and `/api/chat/{chatId}/ask/v2` are wired to the
single `AIAgent` registration specified in FR-008.

**Acceptance Scenarios**:

1. **Given** the running API, **When** a caller sends `POST /ask` with
   `{ "question": "hi" }`, **Then** the response is 200 and the body is
   `{ "answer": string }`, identical to the wire response shape before this
   feature deployed (ASP.NET Core default camelCase serialization of
   `AskResponseDot`).
2. **Given** the running API, **When** `POST /ask`,
   `POST /api/chat/{chatId}/ask`, and `POST /api/chat/{chatId}/ask/v2` are
   each called in turn, **Then** all three succeed against the same
   deployed revision, and a review of `Program.cs` confirms they resolve
   to the single `AIAgent` registration FR-008 specifies.

---

### Edge Cases

- The model's assistant reply is wrapped in Markdown JSON fences
  (```` ```json ``` ````): fences MUST be stripped before parsing (matching
  the backend's `text.Replace("\`\`\`json", "").Replace("\`\`\`", "")`).
  If, after stripping, parsing as `MovieListResponse` JSON still fails, the
  assistant message is returned with fallback text ("No movies were found")
  and an empty `movieList`. The malformed message is still appended to
  stored history so the model sees its own prior output on the next turn.
- The model emits a `MovieId` as a JSON number instead of a string: it MUST
  be accepted and hydrated the same as a numeric string, matching the
  backend's `movieIdElement.ValueKind == JsonValueKind.Number` branch.
- The model emits a `MovieId` that is non-numeric (after accepting the
  numeric form above) or that TMDb cannot resolve: the hydration task for
  that single entry logs a warning and leaves its slot null; the compacted
  `movieList` keeps the model-chosen order of the entries that did resolve;
  the turn still returns 200. An isolated TMDb hydration failure MUST NOT
  escalate to a 503 response for the whole turn.
- The tool-iteration cap is reached before the model emits a final answer,
  or the agent produces no final assistant message with non-empty text:
  `POST /api/chat/{chatId}/ask/v2` MUST return 200 with
  `turn = AssistantChatMessage("No movies were found", [])`, matching the
  Function App's v2 fallback. `POST /api/chat/{chatId}/ask` returns the
  whole stored transcript as usual; the empty-text final message is simply
  skipped by its `ToClientMessageAsync` null-filter.
- A caller sends a missing or unparseable JSON body (the Function App's
  `ask == null` check): the response is 400 with a generic error body
  ("Invalid request, missing ask object"), the session is not mutated,
  and no agent call is made. A body that deserializes but whose `Input`
  property is null, missing, empty, or whitespace-only is NOT validated
  by this slice; it is passed to the agent exactly as the Function App
  does (its `Ask.Input` defaults to `string.Empty`).
- Two concurrent turns on the same `chatId`: the slice does not add session
  locking. Whichever write reaches Cosmos last wins; the earlier turn's appended
  messages may be overwritten. Preserving the Function App's existing behaviour
  (no locking) is explicit and documented, not a defect.
- Azure OpenAI returns an error, is rate-limited, or exceeds the configured
  per-attempt timeout: the OpenAI-only named-HttpClient resilience pipeline
  (FR-009) retries up to its configured limit. If the model call still fails
  after retries, the endpoint returns 503 with a generic body and the exception
  is logged. TMDb calls go through `TMDbClient`'s own HTTP pipeline and inherit
  its library defaults (not the OpenAI resilience); a persistent TMDb failure
  behind the agent's tool-call path surfaces as a 503 by the same rule.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose `POST /api/chat/{chatId}/ask` and
  `POST /api/chat/{chatId}/ask/v2`. Both routes accept a JSON body of shape
  `{ "Input": string }`, use the same path parameter name `chatId`, and are
  anonymous (matching the Function App's authorization level).
- **FR-002**: The response body of `POST /api/chat/{chatId}/ask` MUST satisfy
  the same JSON contract the Function App's `ChatMessageResponse` emits on
  the wire: exactly two top-level properties `funnyFact` (nullable string)
  and `messages` (array). Each `messages` element MUST carry a `role`
  discriminator property whose value is `"user"` or `"assistant"`, a `text`
  property, and - for assistant elements - a `movieList` array. The outer
  object and all nested objects use the default web-JSON policy (camelCase
  for CLR PascalCase properties, including every `MovieViewModel` field in
  FR-010); the `role` discriminator stays lowercase because the Function
  App's `ChatMessage` type declares it that way via
  `[JsonPolymorphic(TypeDiscriminatorPropertyName = nameof(role))]`. No other
  top-level properties are part of the contract. Compatibility is
  shape-based (same property names, same types, same nesting; property
  ordering and insignificant whitespace are not part of the contract) and
  is verified against a real Function App response captured during the
  manual check in SC-001. The `funnyFact` field is permitted to be `null`
  where the Function App's response carries a generated string (see FR-004).
- **FR-003**: The response body of `POST /api/chat/{chatId}/ask/v2` MUST have
  exactly `funnyFact` (nullable string) and `turn` (one chat-message object
  with the same per-element shape as `messages` in FR-002), under the same
  web-JSON policy and the same `role` discriminator rule.
- **FR-004**: Because ChatPlanner tools are out of scope for this slice,
  `funnyFact` MUST be serialized as `null` in every response for both routes.
  The property is still present on the response object so the client contract
  is shape-compatible with the Function App.
- **FR-005**: The system MUST load the session document from Cosmos by point-read
  on `chatId` before running the turn, append the new user message and every
  message the model emits (including intermediate tool-call and tool-result
  messages that carry no text) to the session's `ChatHistory`, and write the
  session back to Cosmos before responding.
- **FR-006**: The `MoviceTrackerChatSession` type MUST gain a
  `HydratedMovies` property whose Cosmos serialization is
  `Dictionary<string, MovieViewModel>` (keyed by TMDb movie id string)
  and whose default is null, matching the Function App's session shape.
  Documents written before this feature deploys MUST still deserialize
  with `HydratedMovies` null, and MUST NOT be rewritten by this feature
  except as part of a turn that touches them.
- **FR-007**: `ChatSessionRepository` MUST expose a session read and a session
  save alongside its existing create (slice 002). Read MUST use a Cosmos
  point-read on `chatId` with partition key equal to `chatId`. Save MUST
  replace the full session document via `ReplaceItemAsync`, matching the
  Function App's `SaveChatSession`. No other repository surface is added.
- **FR-008**: Both routes MUST run their turn through a single DI-registered
  `AIAgent` built on top of an `IChatClient` that targets the existing
  `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, and
  `AzureOpenAI:DeploymentName` configuration. The chat client MUST
  authenticate to the `movie-tracker-openai` resource with
  `AzureOpenAI:ApiKey`, matching the Function App and the current
  `POST /ask`. No new OpenAI configuration keys are introduced. This is a
  justified Principle I exception: the `movie-tracker-openai` resource lives
  in a different (Pay-As-You-Go) subscription than the Container App
  (Development subscription), so a cross-subscription role assignment is not
  available for this slice. Moving the OpenAI auth path to the user-assigned
  managed identity is deferred to a dedicated follow-up slice.
- **FR-009**: The agent's chat-client pipeline MUST include automatic function
  invocation with `MaximumIterationsPerRequest = 12`, and the Azure OpenAI
  chat-client transport MUST route through a named resilient `HttpClient` with a
  120-second total request timeout, a 60-second per-attempt timeout, 3 retry
  attempts, and a 120-second sampling duration. The named HttpClient's
  resilience applies only to the OpenAI call path; the TMDb tool and hydration
  path uses `TMDbClient`'s own HTTP pipeline and its library defaults. These
  OpenAI values are the Function App's `Program.cs` settings reproduced
  verbatim.
- **FR-010**: On every turn, the system MUST hydrate every `MovieId` the model
  emits into a `MovieViewModel` carrying the CLR fields: `PosterPath`, `Adult`,
  `Overview`, `ReleaseDate`, `GenreIds`, `Id`, `OriginalTitle`,
  `OriginalLanguage`, `Title`, `BackdropPath`, `Popularity`, `VoteCount`,
  `Video`, `VoteAverage`, `Favorite` (always false in this slice), `ImdbId`,
  `Trailer`, `GenreNames`, `Runtime`, and `Tagline`. On the wire these
  serialize per FR-002's web-JSON policy (camelCase). On a cache miss (per
  FR-011), hydration MUST fetch the view model via TMDb's `GetMovieAsync`
  with the `Videos` append-to-response (one request per missed movie); a
  cache hit MUST NOT issue the detail request. Hydration MUST run
  concurrently across the movies in a single turn and MUST write each
  resolved view model back by index so the model's ordering is preserved. `Trailer` MUST be a `MovieTrailerInfo` carrying
  `Key`, `Name`, `Site`, `Type`, `Official`, `YouTubeUrl`
  (`https://www.youtube.com/watch?v={Key}`), `EmbedUrl`
  (`https://www.youtube.com/embed/{Key}`), and `ThumbnailUrl`
  (`https://img.youtube.com/vi/{Key}/maxresdefault.jpg`), selected from the
  movie's videos as the first entry with `Type == "Trailer"` and
  `Site == "YouTube"`, ordered by `Official` descending then `Size`
  descending; it MUST be null if no such video exists or if the only
  candidate's `Site` is not `"YouTube"`. A failure to hydrate one entry MUST
  NOT fail the turn - that single entry is dropped from `movieList` and the
  turn still returns 200.
- **FR-011**: Hydration MUST follow the Function App's cache behaviour,
  ported as-is from `Functions/Function.cs`:
  1. On load, `LoadHydrationCache(session)` builds a per-request
     `ConcurrentDictionary<string, MovieViewModel>` from
     `session.HydratedMovies`, treating null as empty.
  2. Hydration reads through this per-session cache first, then a
     process-wide `IDistributedCache` (in-memory for this slice;
     multi-replica coherency is not in scope). On a miss, both caches
     MUST be populated; on a process-cache hit, the per-session cache
     MUST also be populated.
  3. On save, `SaveSessionAsync` writes
     `session.HydratedMovies = sessionCache.Take(150).ToDictionary(...)`
     and emits an informational log line if `sessionCache.Count` exceeded
     150 before the trim, matching the Function App's `LogInformation`.
     No ordering, recency, or eviction policy beyond `.Take(150)` is
     part of this slice; whichever 150 entries the dictionary enumerator
     yields are the ones persisted. Entries dropped by the trim re-enter
     the normal cache lookup on the next reference (per-session cache
     first, then process cache, then TMDb).
  The 150 bound is a hydration-map bound only. Stored `ChatHistory`
  is not bounded by this slice, so this slice does not formally
  guarantee Cosmos' 2 MB item limit.
- **FR-012**: Before the agent call, the stored chat history MUST be
  reduced by the Function App's `ContextReducer` pipeline, ported as-is
  from `Functions/Function.cs`:
  ```
  new PipelineCompactionStrategy([
      new ToolResultCompactionStrategy(
          trigger: CompactionTriggers.HasToolCalls(),
          minimumPreservedGroups: 2)
      { ToolCallFormatter = SummariseToolCalls },
      new SlidingWindowCompactionStrategy(
          trigger: CompactionTriggers.TurnsExceed(12),
          minimumPreservedTurns: 4),
  ]).AsChatReducer()
  ```
  The reducer MUST run over a copy of the full stored `ChatHistory`
  (the Function App's `[.. chatMessages]` pattern) so the stored
  history survives intact and the whole-transcript response in FR-002
  keeps replaying every turn. `SummariseToolCalls`, `SummariseArguments`,
  and `SummariseResult` are ported as-is from the Function App. The
  observable behaviour of these strategies is whatever the ported code
  and the referenced `Microsoft.Agents.AI.Compaction` preview types
  produce; this slice does not re-specify it.
- **FR-013**: The structured-output response format MUST be applied per-turn
  through the chat-route's run options (as the Function App's `RunTurnAsync`
  does), not set as an agent-wide default. The format MUST be a
  `ChatResponseFormat.ForJsonSchema` built from `MovieListResponse`, with
  `DisallowAdditionalProperties = true` and `RequireAllProperties = true`,
  generated against serializer options whose `PropertyNamingPolicy` is null so
  the schema the model sees uses the Function App's PascalCase names
  (`SystemMessage`, `MovieList`, `MovieId`, `MovieName`). This PascalCase
  schema governs only the model-facing structured output; the client-facing
  wire format in FR-002/FR-003 is camelCase and unaffected.
- **FR-014**: The agent MUST be loaded with exactly two tool sets, created
  and registered in this slice:
  1. The backend's TMDb tool set from
     `Prompts/TheMovieDBKernelFunctions.cs`, ported with
     behaviour-preserving fidelity (same tool signatures, parameters,
     return shapes, and prompts the model sees) and wired through one
     process-wide `TMDbClient` singleton constructed from the existing
     `TheMovieDb:Api-Key` configuration.
  2. The backend's `Prompts/DateTimeKernelFunctions.cs` static tool set,
     ported with the same behaviour-preserving fidelity.
  The existing `MovieTracker.Api/Tools/TheMovieDBTool.cs` and the agent's
  current per-tool DI registrations MUST be removed as part of this
  slice; the TMDb surface the model sees comes only from the ported
  backend file.
- **FR-015**: `POST /ask` MUST continue to accept its existing
  `{ "question": string }` request body (deserialized to `AskRequestDot`) and
  return its existing `{ "answer": string }` response body (serialized from
  `AskResponseDot` under ASP.NET Core's default camelCase policy), served by
  the same single `AIAgent` registration used by the chat routes. `/ask`
  MUST invoke the agent WITHOUT passing the structured-output response
  format from FR-013, so its `answer` stays a plain-text model reply rather
  than a `MovieListResponse` JSON blob. It MUST NOT read or write any
  Cosmos session. All three `AIAgent` registrations currently in
  `Program.cs` - the "Movie Assistant" singleton, the "DateTime Agent"
  singleton, and the factory-registered "TheMovieDb Agent" - MUST be
  removed and replaced by the single `AIAgent` registration specified in
  FR-008 whose tool list is the two sets described in FR-014 (ported TMDb
  kernel functions and ported DateTime kernel functions). Date/time
  functionality the removed "DateTime Agent" currently provides is
  preserved through the new unified agent's DateTime tool set, not through
  a second agent.
- **FR-016**: On a missing or unparseable request body (the Function
  App's `ask == null` check), both chat routes MUST return 400 with a
  generic body ("Invalid request, missing ask object" is acceptable). A
  body that deserializes but carries a null/missing/empty/whitespace
  `Input` is NOT validated by this slice and MUST be passed through to
  the agent, matching the Function App. On a point-read miss for
  `chatId`, both chat routes MUST return 404 with a generic body.
  Isolated hydration failures for individual `MovieId` entries MUST NOT
  trigger 503 (see Edge Cases). On any other failure from the Cosmos
  session read/save or from the model call that reaches the top-level
  handler, both chat routes MUST return 503 with a generic body and
  MUST log the exception. No response body MUST contain the exception
  message, stack trace, credentials, tokens, connection strings, or any
  secret material.
- **FR-017**: No existing endpoint path MUST change behaviour beyond what this
  feature specifies. In particular `/health`, `/health/ready`, `/health/cosmos`,
  `/version`, and `GET /api/chat/start` MUST behave identically before and after
  this change.
- **FR-018**: Both TMDb and Azure OpenAI become load-bearing runtime
  dependencies of the API via this slice, so the slice MUST add two
  dependency probes:
  - `/health/tmdb` performs a cheap real round trip against the configured
    `TMDbClient` (e.g. `GetConfigAsync`) and returns the TMDb API base URL
    reached. It MUST NOT return any key material.
  - `/health/openai` performs a cheap real round trip against the configured
    chat client (e.g. a one-token completion against the configured
    deployment) and returns the Azure OpenAI endpoint host and deployment
    name reached. It MUST NOT return any key material.
  Both probes MUST live on their own paths; `/health` and `/health/ready`
  MUST stay cheap and dependency-free per the constitution's health
  contract. On failure each probe MUST return 503 with a reason
  classification and no secret material.

### Out of Scope *(explicitly excluded from this feature)*

- The ChatPlanner tool set from `movie-tracker-backend/src/MovieTracker.Backend/Agents`
  and all tools it owns: `GetMovieRating`, `CompareMovieRatings`,
  `GetMovieTrailers`, `HandleTrailerRequest`, Wikipedia and Wikidata lookups,
  and the funny-fact pipeline. `FunnyFact` is returned as `null`.
- The OMDb integration and its API key.
- Custom telemetry: `Telemetry.cs`, the `OpenTelemetryChatClient` wiring, the
  `InstrumentedAIFunction` wrapper, gen-ai metric and histogram exporters, cost
  pricing emission, and the Application Insights log exporter the backend
  configures.
- The reasoning-effort setting (`AzureOpenAI:Reasoning-Effort`). The agent runs
  with the deployment's default effort.
- Any frontend change, SDK update, or OpenAPI client regeneration.
- Migrating or copying chat sessions from the Function App's Cosmos account.
- Changing the Function App's existing `Chat-Ask`, `Chat-Ask-V2`, `Chat-Start`,
  or related endpoints.
- Session concurrency control (locking, ETag check, optimistic concurrency on
  the Cosmos replace). The Function App performs none and this slice does
  not add any.
- Any per-call "tool middleware" (`AIAgentBuilder.Use(...)`) and any per-tool
  metrics instrumentation.

### Key Entities

- **Chat session document (`MoviceTrackerChatSession`)**: the Cosmos
  document created by slice 002. Owned by this slice: adds the
  `HydratedMovies` map, populated per FR-011 and bounded by the
  Function App's `.Take(150)` trim. The `FunnyFact` property stays as
  slice 002 defined it; this slice does not write to it.
- **Movie view model (`MovieViewModel`)**: the hydrated shape returned inside
  `MovieList`. Ported from `Functions/Function.cs` with the field set in
  FR-010. Nullable additive fields (`GenreNames`, `Runtime`, `Tagline`) stay
  nullable; clients MUST treat null as "unknown".
- **Chat message (response-side `ChatMessage`, `UserChatMessage`, `AssistantChatMessage`)**:
  the polymorphic response shape. Discriminator is the lowercase `role` field,
  reproduced from the Function App; the discriminator MUST NOT be renamed.
- **Structured-output DTO (`MovieListResponse`, `MovieListItem`)**: the shape
  the model is forced to produce via `ChatResponseFormat`. Carries `SystemMessage`
  and `MovieList` of `{ MovieId, MovieName }`. Deliberately does NOT carry
  `FunnyFact`.
- **Context reducer**: the Microsoft.Agents.AI compaction pipeline (preview;
  MAAI001 is suppressed). Owns tool-result collapse and the sliding window.
- **TMDb client**: one `TMDbClient` singleton per process, constructed from
  `TheMovieDb:Api-Key`. The only entry point tools and hydration use.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Manual verification, like slice 002. In each target
  environment (local build and deployed revision), create a `chatId` via
  `GET /api/chat/start`, then run `POST /api/chat/{chatId}/ask` and
  `POST /api/chat/{chatId}/ask/v2` against that session. Each call
  returns 200, and each response body shares the same top-level and
  per-message JSON shape as a real response from the Function App's
  matching endpoint (property names, types, nesting; byte equality is
  not required against live dependencies).
- **SC-002**: Over three sequential `POST /api/chat/{chatId}/ask` turns on
  the same session where each turn produces exactly one non-empty final
  assistant message, the whole-transcript response grows by exactly one
  user+assistant pair per turn, with no reordering of earlier turns and
  no duplication. Verified by inspecting response bodies and by Cosmos
  point-read on `chatId` after each turn.
- **SC-003**: Given a session whose prior turn successfully resolved one
  or more movie ids through TMDb (and the hydration map still holds them
  after FR-011's `.Take(150)` trim), the Cosmos document for that session
  contains an entry for each resolved movie id in `HydratedMovies` with
  the fields listed in FR-010; a subsequent turn that references any of
  the same ids returns a `movieList` entry whose fields equal that
  stored entry.
- **SC-004**: A review of `Program.cs` confirms exactly one `AIAgent`
  registration and one `TMDbClient` registration, that the pre-existing
  per-agent registrations and `MovieTracker.Api/Tools/TheMovieDBTool.cs`
  have been removed, and that the ported `*KernelFunctions` files are
  present.
- **SC-005**: A manual check of `IConfiguration` at startup finds no new
  keys under `AzureOpenAI:` beyond `Endpoint`, `ApiKey`, and
  `DeploymentName`, no new keys referencing reasoning effort, and no new
  Cosmos keys or connection strings. The chat client continues to
  authenticate with `AzureOpenAI:ApiKey` (per FR-008); no new credential
  path is introduced by this slice.
- **SC-006**: A manual check confirms `/health`, `/health/ready`,
  `/health/cosmos`, `/version`, `GET /api/chat/start`, and `POST /ask` all
  respond with the same contract they had before this feature, and that
  the new `/health/tmdb` and `/health/openai` probes (FR-018) each return
  200 with no secret material in a healthy deployment.
- **SC-007**: Given a session on which 14 turns have already been taken,
  a 15th `POST /api/chat/{chatId}/ask` that produces exactly one
  non-empty final assistant message returns 200. The response's
  `messages` array contains every pre-existing user+assistant pair in
  its original order plus the new turn's user+assistant pair, and a
  Cosmos point-read on the session shows `ChatHistory` still contains
  every turn's messages (the reducer runs on a copy per FR-012).

## Assumptions

- Slice 002 ("Chat-Start Session Persistence") is deployed and the Cosmos
  container holds documents whose `id`, `PartitionKey`, and `ChatHistory`
  properties this slice reads and writes. This feature consumes the Cosmos
  account, database, container, identity, and role assignment that slice 001
  provisioned.
- The existing `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, and
  `AzureOpenAI:DeploymentName` configuration (resource `movie-tracker-openai`)
  is already wired and `AzureOpenAI:ApiKey` is sourced from Key Vault in the
  Container App environment. This feature reuses that wiring; it does not
  re-source those secrets.
- **Residual technical debt**: The chat-client auth path continues to use
  `AzureOpenAI:ApiKey` as a justified Principle I exception (FR-008). The
  `movie-tracker-openai` resource lives in a different (Pay-As-You-Go)
  subscription than the Container App (Development subscription), which
  currently blocks a one-shot role assignment on the OpenAI resource.
  Migrating this path to the user-assigned managed identity is deferred to
  a dedicated follow-up slice. Per Governance, the next feature that
  touches the OpenAI auth path MUST retire the key.
- The existing `TheMovieDb:Api-Key` is already present as a container secret
  from a Key Vault reference. This feature consumes it; it does not introduce a
  new secret.
- The chat routes remain anonymous, matching the Function App's authorization
  level and slice 002's `GET /api/chat/start`. End-user authentication is a
  future concern.
- Multi-replica cache coherency for `IDistributedCache` is not a goal of this
  slice. `AddDistributedMemoryCache` is accepted as the Function App's choice;
  the only correctness contract is that the per-session `HydratedMovies` map
  on the Cosmos document is authoritative across replicas.
- The chat-message polymorphic discriminator is the lowercase `role` property as
  the Function App ships it. Changing the case or renaming it is a client-side
  break and out of scope here.
- The Function App's error status codes are not matched byte-for-byte. The
  Function App returns 400 for all failures and includes the exception message
  in the body; this slice returns 503 with a generic body for infrastructure
  failures and never surfaces the exception message. This departs from the
  Function App deliberately, in line with slice 002's generic-error pattern,
  and is captured in FR-016.
- The context reducer uses the preview `Microsoft.Agents.AI.Compaction`
  namespace under `#pragma warning disable MAAI001`, matching the Function App.
- `MaximumIterationsPerRequest = 12`, the three resilience timeouts, and
  the 150-movie hydration bound are copied from the Function App's
  measured settings and are not re-derived here. Changing any of them is
  a follow-up slice, not part of this import.
