# Phase 0 Research: Agent Turn With TMDb Tools And Hydration

The spec is a port, so the research list is short: it exists to pin down the
few things the port cannot copy verbatim (route plumbing, DI shape, package
set, error-handling boundary, probe implementations). Every decision below is
justified against the Function App source at
`C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend` and the
existing `MovieTracker.Api` project. No "NEEDS CLARIFICATION" remain.

---

## Decision 1 — Package additions

**Decision**: Add exactly one PackageReference:
`Microsoft.Extensions.Http.Resilience` (version aligned with the existing
`Microsoft.Extensions.AI` 10.7.0 minor line — pick the current 10.x release
when implementing).

**Rationale**: FR-009 requires `AddStandardResilienceHandler(...)` on a named
`HttpClient`. That extension lives in
`Microsoft.Extensions.Http.Resilience`, which is not yet referenced. Every
other dependency the ported code touches is already in the API's csproj:

- `Microsoft.Agents.AI` 1.17.0 supplies both the agent surface
  (`AIAgent`, `ChatClientAgent`, `AIAgentBuilder`,
  `ChatClientAgentRunOptions`) and the preview compaction types
  (`PipelineCompactionStrategy`, `ToolResultCompactionStrategy`,
  `SlidingWindowCompactionStrategy`, `CompactionTriggers`,
  `CompactionMessageGroup`, `AsChatReducer`) under
  `Microsoft.Agents.AI.Compaction`. The Function App uses them from
  `Microsoft.Agents.AI` 1.15.0; the 1.17.0 line is a superset.
- `Microsoft.Agents.AI.OpenAI` 1.17.0 wires the Azure OpenAI chat client
  into `AIAgentBuilder`.
- `Microsoft.Extensions.AI` 10.7.0 supplies `IChatClient`,
  `ChatOptions`, `ChatMessage`, `ChatRole`,
  `ChatResponseFormat.ForJsonSchema`, `AIJsonUtilities`,
  `AIJsonSchemaCreateOptions`, `FunctionCallContent`,
  `FunctionResultContent`, and `AIFunctionFactory`.
- `Azure.AI.OpenAI` 2.8.0-beta.1 supplies `AzureOpenAIClient` and
  `AzureOpenAIClientOptions` for the `HttpClientPipelineTransport` wiring.
- `TMDbLib` 3.0.0 supplies `TMDbClient`, `MovieMethods.Videos`,
  `DiscoverMovie`, and all tool-side types.
- `Microsoft.Azure.Cosmos` 3.62.0 supplies `CosmosClient`, `Container`,
  `PartitionKey`, `CosmosException`.
- `IDistributedCache` and `AddDistributedMemoryCache` are in the
  `Microsoft.AspNetCore.App` shared framework (the project is
  `Microsoft.NET.Sdk.Web`), so no package reference is needed.
- `System.Collections.Concurrent.ConcurrentDictionary`, `JsonDocument`,
  `JsonSerializer` are in the BCL.

**Alternatives considered**:
- Reproducing `AddStandardResilienceHandler` by hand with a custom
  `DelegatingHandler` and Polly directly. Rejected: it is strictly more code
  for the same behaviour, and the Function App's measured timeouts assume the
  `AddStandardResilienceHandler` surface (`TotalRequestTimeout`,
  `AttemptTimeout`, `CircuitBreaker.SamplingDuration`, `Retry.MaxRetryAttempts`).
  Spec guardrails forbid introducing new abstractions.
- Hand-rolling the compaction pipeline. Rejected: FR-012 says the reducer is
  ported as-is, including the preview namespace and the `MAAI001` suppression.

---

## Decision 2 — Routes: minimal API or controllers

**Decision**: Minimal API endpoints for `POST /api/chat/{chatId}/ask`,
`POST /api/chat/{chatId}/ask/v2`, `GET /health/tmdb`, and `GET /health/openai`.
`POST /ask` stays on the existing `AskController` unchanged.

**Rationale**: The three existing new endpoints added in this project
(`/health`, `/health/ready`, `/health/cosmos`, `GET /api/chat/start`,
`/version`) are all minimal API. Keeping the new routes on the same style
matches the project convention and avoids introducing a controller purely for
these two chat routes. `AskController` is kept because changing it is out of
scope — FR-015 only requires it to resolve to the single new `AIAgent`
registration and continue returning `{"answer":string}`. Since the controller
takes `AIAgent` by constructor and calls `RunAsync(string)` with no run
options, the structured-output response format from FR-013 is not applied —
exactly what FR-015 requires.

**Alternatives considered**:
- Convert `/ask` to a minimal API endpoint. Rejected: it is a behaviour-neutral
  refactor the spec does not require, and it widens the diff.
- Add a `ChatController` for the two new routes. Rejected: inconsistent with
  the rest of the project; the two routes are small enough that a static
  class under `Chat/` with endpoint-method handlers is the smallest working
  shape.

---

## Decision 3 — DI lifetimes

**Decision**:
- `TMDbClient` — Singleton (one per process; FR-014's "one process-wide TMDbClient singleton").
- `TheMovieDBKernelFunctions` — Singleton. The ported class is constructed
  with the TMDbClient singleton; it holds no per-request state.
- `IChatClient` — Singleton. Built from `AzureOpenAIClient`,
  `AsIChatClient()`, `AsBuilder().UseFunctionInvocation(configure cap = 12).Build()`.
  No per-request state; `IChatClient` is thread-safe by contract.
- `AIAgent` — Singleton. Built from the `IChatClient` singleton plus the
  two tool sets (TMDb + DateTime). The ported `RunTurnAsync` reads only the
  `AIAgent` and run-options, never state on the agent itself.
- `ChatSessionRepository` — Singleton (slice 002 pattern). The repository is
  stateless aside from the `Container` reference, which is already singleton.
- `IDistributedCache` — Singleton via `AddDistributedMemoryCache()`.

**Rationale**: The Function App registers these as `AddScoped` because it is a
Functions worker where every invocation gets a fresh DI scope. In an
ASP.NET Core process on Container Apps, singletons avoid the per-request
allocation cost of scoped and match the pattern slices 001 and 002 established
for `CosmosClient` and `ChatSessionRepository`. Nothing in the ported source
depends on scoped lifetime (the per-session cache is a `ConcurrentDictionary`
built per request from the session document — not from DI).

**Alternatives considered**:
- Scoped lifetimes to mirror the backend verbatim. Rejected: more allocations
  for no behavioural gain; inconsistent with the API's existing singletons.
- Hybrid (singleton `IChatClient`, scoped `AIAgent`). Rejected: no reason to
  split; `AIAgent` depends only on `IChatClient` and the tool list, both
  singleton.

---

## Decision 4 — Chat-route error handling boundary

**Decision**: The two chat routes return 400 for a missing/unparseable JSON
body, 404 for a point-read miss on `chatId`, and 503 (generic body, no
exception message) for any other failure that reaches the top-level handler,
including Cosmos faults other than NotFound and model-call failures that
survive the FR-009 retry budget. All exceptions are logged with
`ILogger.LogError` (not `LogCritical` — matches slice 002's generic-error
pattern and the project's current log-level conventions).

**Rationale**:
- The Function App returns 400 with the exception message in every failure
  mode (`return new BadRequestObjectResult(ex.Message)`). Slice 002 already
  established that this project returns 503 with a generic body for
  infrastructure failures and never surfaces the exception message. The spec
  Assumptions call this deliberate departure out, and FR-016 pins the
  three codes (400 / 404 / 503) in the API.
- 404-on-not-found is an owner decision captured in the spec (US1 scenario 3,
  FR-016). It differs from the Function App's 400 and from an "all failures
  are 503" default. Implementation: catch `CosmosException` on
  `GetChatSession` and map `StatusCode == HttpStatusCode.NotFound` to
  `Results.NotFound(new { error = "Chat not found" })` (generic body).
- Isolated TMDb hydration failures for individual movie ids remain
  warnings and do not escalate to 503 (FR-010 and Edge Cases). The ported
  `SafeProcessMovieAsync` wrapper already enforces this.
- The ported `RunTurnAsync` throws if the agent call ultimately fails after
  the FR-009 retry budget. That exception bubbles into the top-level
  handler and becomes a 503. The spec permits this (Edge Cases).
- The ported `ToClientMessageAsync` catches `JsonException` on fence-stripped
  text and emits `AssistantChatMessage("No movies were found", [])`. This
  fallback stays as the spec Edge Case requires; the malformed message is
  still appended to stored history.

**Alternatives considered**:
- Match the Function App byte-for-byte (400 with `ex.Message` body).
  Rejected: Assumption explicitly departs from this.
- Return 500 for infrastructure faults. Rejected: slice 002 already uses 503
  for the same class of failures; consistency with slice 002 is the point of
  the slice.

---

## Decision 5 — Probe implementations

**Decision**:
- `GET /health/tmdb`: call `TMDbClient.GetConfigAsync()` on the DI-registered
  singleton (no client-side cancellation token — the method's TMDbLib 3.0.0
  signature does not accept one; the library's own HTTP defaults apply) and
  return `Results.Ok(new { status = "ok", baseUrl = "https://api.themoviedb.org/3" })`.
  On any exception, log and return 503 with
  `{ status = "fail", reason = "unavailable" }`. No key material is ever
  emitted.
- `GET /health/openai`: resolve the DI-registered `IChatClient`, construct a
  `CancellationTokenSource` with a 10 s probe timeout (this call DOES accept
  a `CancellationToken`), call `GetResponseAsync` with a single short user
  message and `new ChatOptions { MaxOutputTokens = 1 }`, and pass the token.
  Return `Results.Ok(new { status = "ok", endpoint = <AzureOpenAI:Endpoint host>, deployment = <AzureOpenAI:DeploymentName> })`.
  On failure, 503 with
  `{ status = "fail", reason = "unavailable" | "timeout", endpoint, deployment }`,
  where `"timeout"` is used specifically for `OperationCanceledException`.
  No key material is ever emitted.

**Rationale**:
- Both probes exercise a real round trip against the real credential, which
  is what Principle IV requires. `GetConfigAsync` is the cheapest TMDb call
  and does not consume quota; it has no cancellation-token overload in
  TMDbLib 3.0.0, so the probe inherits the library's own HTTP defaults
  rather than promising a timeout the code cannot enforce. A
  `MaxOutputTokens = 1` completion is the cheapest OpenAI call for this
  deployment, is bounded by the FR-009 resilience pipeline the chat client
  already sits behind, and does accept a `CancellationToken` so the probe
  can impose a 10 s ceiling on top of the FR-009 budget.
- The identity bits returned (`baseUrl`, `endpoint` host, `deployment`) are
  non-secret and match the pattern slice 001's `/health/cosmos` established.
- The probes live at their own paths (`/health/tmdb`, `/health/openai`); the
  constitution's health-contract bullet forbids making `/health` and
  `/health/ready` dependency-touching, so these are additive.

**Alternatives considered**:
- Share one `/health/dependencies` endpoint that fans out. Rejected: slice
  001 and the constitution's own text put each dependency on its own path,
  so a single revision can degrade one dependency's probe without touching
  the others.
- Skip real round trips and just construct the clients to prove configuration
  is present. Rejected: Principle IV specifically forbids this ("Clients that
  connect lazily MUST NOT be allowed to defer the discovery of broken
  configuration").

---

## Decision 6 — Hydration cache (slice 003's `IDistributedCache`)

**Decision**: Register `AddDistributedMemoryCache()`. The per-session cache is
a `ConcurrentDictionary<string, MovieViewModel>` built from the session's
`HydratedMovies` map at request start (`LoadHydrationCache`) and written back
with `.Take(150)` at request end (`SaveSessionAsync`). The process-wide
`IDistributedCache` is cold per instance (not multi-replica coherent), which
the spec Assumptions accept.

**Rationale**: Verbatim port of the Function App's hydration cache behaviour
(FR-011). `AddDistributedMemoryCache` is the Function App's choice, confirmed
in `movie-tracker-backend/Program.cs`. Multi-replica cache coherency is
explicitly out of scope; the authoritative copy is the per-session
`HydratedMovies` on the Cosmos document.

**Alternatives considered**:
- `AddStackExchangeRedisCache` for cross-replica coherency. Rejected: out of
  scope (spec Assumptions), and no Redis is provisioned (Bicep changes are
  forbidden).
- No `IDistributedCache` layer at all, falling straight through to TMDb on
  per-session miss. Rejected: FR-011 names three tiers; dropping tier two
  departs from the port.

---

## Decision 7 — Agent instructions string

**Decision**: Don't set `instructions` on `AIAgent`. The system prompt lives
in the Cosmos session's `ChatHistory` as the first `ChatRole.System` message
(slice 002 already seeds it from `ChatStartSystemPrompt.Text`), and
`RunTurnAsync` passes the whole stored history into the agent after context
reduction. Setting a second `instructions` on the agent would duplicate the
prompt and cost tokens per turn.

**Rationale**: The Function App does not set agent-level instructions — the
system prompt is purely in the session's `ChatHistory` because that is what
the stored transcript must replay. `AIAgent.RunAsync(modelMessages, null, …)`
with `session: null` is the stateless pattern the Function App uses; the
conversation flows through `modelMessages`, not through any session the agent
would own. The current `MovieTracker.Api/Program.cs` sets `instructions:` on
each of its three agents, but all three registrations are removed by this
slice (FR-015), so there is no regression — the system prompt reaches the
model through the ChatHistory path, as the ported turn runner expects.

**Alternatives considered**:
- Also set `instructions` on the agent for defence in depth. Rejected: a
  second copy of a prompt diverges silently when `ChatStartSystemPrompt.Text`
  is edited; FR-015 says the agent is unified around the ported turn runner.

---

## Decision 8 — Deleting `Tools/DateTimeTool.cs`

**Decision**: Delete `MovieTracker.Api/Tools/DateTimeTool.cs` alongside
`Tools/TheMovieDBTool.cs`.

**Rationale**: `DateTimeTool.cs` is only referenced by the "DateTime Agent"
registration in `Program.cs`. FR-015 removes that registration and preserves
the DateTime surface through the ported `DateTimeKernelFunctions`.
`DateTimeTool.cs` therefore becomes unreferenced; keeping an unused file is
dead code. If the Codex cross-review rejects this cut as "not in the spec",
the fallback is to leave the file in place and flag it as follow-up cleanup —
the removal of the agent registration still goes through.

**Alternatives considered**:
- Keep `DateTimeTool.cs` for safety. Rejected: dead-code preservation is
  cleanup debt; the spec's explicit removal of the DateTime Agent
  registration is what makes it dead.
