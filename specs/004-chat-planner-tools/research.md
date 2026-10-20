# Phase 0 Research: ChatPlanner Tools

**Branch**: `004-chat-planner-tools` | **Date**: 2026-10-20

The slice is a port, so Phase 0 is a short set of concrete decisions on
whether the Function App's choices still hold in this repo. Everything else
in the spec is behaviour-preserving and does not need to be re-derived.

## Decision 1 — OMDb client library

**Decision**: Reference `OmdbApiNet` 1.3.0 — the exact package id and
version from `MovieTracker.Backend.csproj:44`. The shipped C# namespace is
`OMDbApiNet` (uppercase M); the NuGet package id is `OmdbApiNet` (lowercase
'o'). Both the ported `using OMDbApiNet;` directives in
`Agents/OpenMovieDbAgent.cs:2-3` and this package reference are needed to
make `AsyncOmdbClient` resolve.

**Rationale**:
- `OpenMovieDbAgent.cs` is ported as-is; it names `AsyncOmdbClient` from
  the `OMDbApiNet` namespace with a single API-key constructor. Any
  substitute would require editing the ported code, which the spec forbids
  ("behaviour-preserving", FR-003).
- The package is .NET Standard 2.0 and resolves cleanly on .NET 10.
- The Function App has run this package in production and the port's
  behaviour contract (`GetItemByIdAsync` returning the 38-field OMDb
  response) is exactly what the port's `GetMovieRatings` reads back.

**Alternatives considered**:
- **Rolling an HTTP client against `www.omdbapi.com/?i={id}&apikey={key}`
  directly.** Rejected — ports the behaviour but not the code; invalidates
  Codex's "ported as-is" guardrail; the package is 24 lines of DI value and
  one dependency to track.
- **`OMDbSharp` and other NuGet packages.** Rejected — none are referenced
  by the Function App; using a different package would require editing the
  ported file.

**Mitigation if `OMDbApiNet` becomes unavailable before implementation**:
spec Assumptions already notes "the owner may substitute any equivalent OMDb
client — with the behavioural contract of this spec unchanged." The
substitution is a plan amendment, not a spec change.

## Decision 2 — Wikimedia User-Agent comment URL

**Decision**: `(+https://github.com/anoushm/movie-tracker-api)`.

**Rationale**:
- Wikimedia's API etiquette requires a descriptive User-Agent whose comment
  identifies the operator so Wikimedia can contact them if a client
  misbehaves. The Function App ships
  `(+https://github.com/TonyAbell/movie-tracker-backend)` — the backend
  repo's URL. Copying that into this different codebase would misidentify
  this API's operator to Wikimedia (sending Wikimedia to the wrong contact
  if something misbehaves), so the port keeps the Function App's
  **pattern** (`{serviceName}/{serviceVersion} + (+<comment URL>)`) but
  swaps in this repo's own URL.
- `git remote get-url origin` on this clone resolves to
  `https://github.com/anoushm/movie-tracker-api` (owner's user context,
  2026-10-20).

**Alternatives considered**:
- **Reuse the Function App's URL verbatim.** Rejected on operator-contact
  grounds above. The user explicitly ratified this decision when accepting
  Codex round 6's rejected finding during the spec cross-review.
- **Omit the comment URL entirely.** Rejected — Wikimedia measured 403s
  without a descriptive UA, and the whole backend history of the grounded
  path returning null traced back to a missing UA (file-level comment on
  `AddHttpClient<WikipediaSearchAgent>` in backend `Program.cs:183-190`).

## Decision 3 — Chat client registration reuse

**Decision**: `ChatPlanner` is constructed by DI with the slice 003
`IChatClient` registration unchanged. No second `IChatClient` registration
is added.

**Rationale**:
- Backend `Prompts/ChatPlanner.cs:17-28` takes `IChatClient` directly and
  uses it for three single-shot completions (`DetectEntity`,
  `GenerateEnhancedFunnyFact`'s grounded prompt, `GetMovieContext`'s
  grounded prompt). None of these completions need the agent's tool set and
  none need the structured-output `ResponseFormat` from slice 003's
  `MovieListResponse` schema.
- Slice 003's `IChatClient` registration does not bake in
  `ResponseFormat` (slice 003 FR-013 applies it per-turn through
  `ChatOptions`, not on the registration). `UseFunctionInvocation` is
  applied on the chat client, but single-shot calls that do not emit
  function calls are unaffected by it — the model simply does not call
  tools, which is already the behaviour on these three prompts.
- Adding a second `IChatClient` registration (one with
  `UseFunctionInvocation` for the agent, one bare for ChatPlanner) would
  double the configuration surface and the DI resolution map for no
  observable behaviour difference.

**Alternatives considered**:
- **Second named `IChatClient` registration specifically for ChatPlanner's
  single-shot prompts.** Rejected — the Function App does not do this
  (backend `Program.cs:212-274` registers one `IChatClient` and ChatPlanner
  consumes it directly) and it would exceed the slice's declared boundary.

## Decision 4 — Config key naming consistency

**Decision**: `OpenMovieDb:Api-Key` (dot-colon .NET path; `Api-Key` with the
hyphen; env var `OpenMovieDb__Api-Key`).

**Rationale**:
- Backend `Program.cs` and `OpenMovieDbAgent.cs:65` both read
  `configuration["OpenMovieDb:Api-Key"]` — this is the Function App's own
  spelling. The port must match (FR-003: "configuration key path
  `OpenMovieDb:Api-Key`").
- The repo's existing related keys follow the same hyphen pattern:
  `TheMovieDb:Api-Key`, `AzureOpenAI:ApiKey` (slice 003). The slice 004
  key sits naturally among them.
- Env-var equivalent follows ASP.NET Core's `__` double-underscore
  substitution for `:`; the hyphen in `Api-Key` stays as a literal hyphen
  (slice 003's `TheMovieDb__Api-Key` env var is already wired this way in
  `container-app.bicep:55-57`).

**Alternatives considered**:
- **Rename to `OpenMovieDb:ApiKey` (camelCase, no hyphen).** Rejected —
  would require editing the ported `OpenMovieDbAgent.cs` constructor,
  violating "port as-is". The small inconsistency with `AzureOpenAI:ApiKey`
  is inherited from the Function App.

## Decision 5 — Where the funny-fact flow lives

**Decision**: `SafeGenerateFunnyFactAsync` and the funny-fact
kickoff/await/write-session code port into
`MovieTracker.Api/Chat/ChatTurnEndpoints.cs` as helper methods of the same
endpoint class slice 003 already uses. The flow is identical in sequencing
to backend `Functions/Function.cs:686-697` and `:774, :839-843`.

**Rationale**:
- Keeps the "one conversational turn" surface in one file, matching the
  Function App's `RunTurnAsync` layout.
- Introduces no new class or interface — the owner directive forbids them.
- The flow's correctness depends on three ordering invariants (kick off
  BEFORE user message append and compaction; await AFTER model reply
  append; non-null-only overwrite) that are easier to review when they
  live beside `RunTurnAsync` than when they are split across a helper
  class.

**Alternatives considered**:
- **A separate `FunnyFactService` class.** Rejected — new abstraction
  without any payoff; the method is 11 lines in the backend.

## Decision 6 — DI lifetimes for the ported classes AND slice 003's `AIAgent` / `IChatClient`

**Decision**: Register `OpenMovieDbAgent`, `TrailerAgent`, `ChatPlanner` as
`AddScoped`. Register `WikipediaSearchAgent` via `AddHttpClient<T>` (its
default is Transient per instance, with the typed `HttpClient` managed by
`IHttpClientFactory`). As part of this slice, change the existing slice
003 `IChatClient` registration from `AddSingleton` to `AddScoped`, and the
existing slice 003 `AIAgent` registration from `AddSingleton` to
`AddScoped` (`MovieTracker.Api/Program.cs:47,74`). Everything else slice
003 declares stays as it is (`TMDbClient`, `TheMovieDBKernelFunctions`,
`ChatSessionRepository`, `CosmosClient` remain Singleton; endpoints are
unchanged).

**Rationale**:
- The Function App's `Program.cs:206-282` registers all four classes
  (`OpenMovieDbAgent`, `TrailerAgent`, `WikipediaSearchAgent`,
  `ChatPlanner`) and the `IChatClient` + `AIAgent` as Scoped. The port is
  behaviour-preserving, and the Function App's runtime has been operating
  this configuration without the Scope-capture pitfalls that would show up
  if any of them had been Singleton.
- `ChatPlanner` holds a typed `WikipediaSearchAgent` whose
  `HttpClient` must rotate handlers to avoid DNS staleness over long-lived
  processes. A Singleton `ChatPlanner` capturing a typed-client
  `WikipediaSearchAgent` would freeze the handler for the process lifetime,
  silently defeating Microsoft's recommended `IHttpClientFactory` pattern.
  Scoped `ChatPlanner` lets each per-request scope activate a fresh typed
  client (via `IHttpClientFactory`) with correctly rotating handlers.
- Slice 003's `AIAgent` is Singleton today (`Program.cs:74`) with Singleton
  tools (`TheMovieDBKernelFunctions`, `DateTimeKernelFunctions`), which
  works because nothing captured so far is Scoped or Transient. Adding
  Scoped `ChatPlanner` into its tool list would trigger ASP.NET Core's
  scope-validation at startup in Development
  (`ValidateScopes = true`) — this is Codex's blocking finding on round 1
  of the plan review. Changing `AIAgent` to Scoped is the smallest change
  that preserves the port's behaviour and the backend's working pattern.
- The `IChatClient` must also move to Scoped: `ChatClientAgent` captures
  its `IChatClient` by reference; a Singleton `IChatClient` consumed by a
  Scoped `AIAgent` is strictly fine (Singleton can be captured by Scoped),
  but the backend has both as Scoped and the alignment makes the two
  lifetimes consistent. The runtime cost of per-request `IChatClient`
  activation is negligible — the Azure OpenAI SDK's `AzureOpenAIClient`
  construction is lightweight when the underlying `HttpClient` comes from
  `IHttpClientFactory`.

**Scope-boundary note**: changing slice 003's registration lifetime does
NOT violate spec FR-018 ("Slice 003's single-agent tool list MUST remain
exactly the ported TMDb kernel functions + ported DateTime kernel
functions + the six tools listed in FR-001"). FR-018 constrains the tool
LIST, not the DI LIFETIME. The adjustment is plan-level, not spec-level.

**Alternatives considered**:
- **Keep `AIAgent` Singleton, register ChatPlanner as Singleton.**
  Rejected — ChatPlanner's Singleton capture of a typed-client
  `WikipediaSearchAgent` defeats handler rotation (production latent
  defect). Also the backend does NOT do this.
- **Register `WikipediaSearchAgent` as Singleton, instantiate its
  HttpClient directly in a factory lambda instead of using
  `AddHttpClient<T>`.** Rejected — spec FR-005 explicitly requires
  `AddHttpClient<WikipediaSearchAgent>`, and the backend uses it
  (`backend/Program.cs:191`). Changing the registration pattern would
  deviate from the port.
- **Keep `AIAgent` Singleton, add a per-request factory for ChatPlanner
  via `IServiceScopeFactory`.** Rejected — complex, and introduces a
  pattern that neither the backend nor slice 003 uses.

## Decision 7 — Where ported record types live

**Decision**: Public records returned to callers (ChatPlanner tools,
assembly consumers) live in `Agents/` matching the backend's folder.
JSON serialization DTOs for Wikipedia and Wikidata responses
(`WikipediaSummary`, `WikidataResponse`, …) also live in `Agents/` and are
public — the backend has them public and the port does not downgrade them.
One public type per file (slice 003's CS-1).

**Rationale**:
- Matches the backend's `Agents/` folder name verbatim.
- Keeps the WikipediaSearchAgent file to just the agent class so a reader
  does not have to scroll past nine DTO classes to see the behaviour.
- One-type-per-file is slice 003's established project convention; using a
  different rule for this slice would be ad-hoc.

**Alternatives considered**:
- **Group the 9 JSON DTOs in a single `WikipediaDtos.cs` for brevity.**
  Rejected — would deviate from slice 003's CS-1 convention. The extra
  files cost nothing at runtime.
- **Make the JSON DTOs internal.** Rejected — the backend has them public
  and nothing in the port requires narrowing their visibility; "port as-is"
  means no gratuitous changes.
