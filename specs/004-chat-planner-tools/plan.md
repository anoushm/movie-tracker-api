# Implementation Plan: ChatPlanner Tools (Ratings, Trailers, Wikipedia-Grounded Context And Funny Fact)

**Branch**: `004-chat-planner-tools` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

## Summary

Attach the Function App's ChatPlanner tools to the single `AIAgent` slice 003
registered. Port `Prompts/ChatPlanner.cs`, `Agents/OpenMovieDbAgent.cs`,
`Agents/TrailerAgent.cs`, and `Agents/WikipediaSearchAgent.cs` as-is from
`movie-tracker-backend`, add their six tool entries to the agent's tool list,
and port the concurrent-with-the-model-call funny-fact flow from
`Functions/Function.cs`'s `SafeGenerateFunnyFactAsync` into slice 003's
`RunTurnAsync` so the response envelope's `funnyFact` field can carry a
Wikipedia-grounded string instead of always-null. The task is kicked off
before the user message is appended, awaited after the model reply is
appended, and its result is written to `session.FunnyFact` only when
non-null (null MUST NOT overwrite). The flow is concurrent, not
fire-and-forget: the turn waits for the task to complete. The
`WikipediaSearchAgent` HttpClient's 15-second timeout is a per-HTTP-request
bound only — it caps each Wikipedia or Wikidata call individually, not the
funny-fact pipeline as a whole (see Performance Goals for the full
sequential shape).

One new NuGet reference (`OmdbApiNet` 1.3.0, matching the Function App's
`MovieTracker.Backend.csproj:44` — note the package id has a lowercase 'o';
the C# namespace it ships is `OMDbApiNet`), one new config key
(`OpenMovieDb:Api-Key`), and one `container-app.bicep` edit (new
`omdb-api-key` secret entry + env var, matching the existing
`azure-openai-key` / `themoviedb-api-key` pattern). `infrastructure/key-vault.bicep`
is NOT changed — the owner seeds the secret in the vault once before deploy,
same as the two existing secrets (spec FR-012, recorded Development-Workflow
exception).

One slice 003 adjustment is required: the current `AIAgent` registration is
Singleton (`MovieTracker.Api/Program.cs:74`). The three ported classes
(`OpenMovieDbAgent`, `TrailerAgent`, `ChatPlanner`) and the typed
`WikipediaSearchAgent` HttpClient need per-request lifetime semantics
compatible with ChatPlanner's HttpClient usage, so this slice changes the
single `AIAgent` registration from Singleton to Scoped (matching the
Function App's `Program.cs:282`), along with the ported classes. The spec's
"single AIAgent" boundary (FR-018) is lifetime-agnostic and this change
stays inside it. Research Decision 6 explains the trade-off.

The source form adapts to slice 003's coding standards: explicit types instead
of `var`, braces on every `if`/`else`/`for`/`while`, no inline comments
(doc-comments and XML `<summary>` blocks allowed), and one public type per
file. The ported file-level and record-level `<summary>` blocks from the
backend are kept — they are load-bearing documentation of *why* a given weight
or guard exists (confidence weights, UA header, dirty-NONE parsing). Manual
quickstart against local and deployed revisions is the only verification, per
owner decision — same as slice 003.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core (unchanged from slice 003).

**Primary Dependencies**:
- `Microsoft.Extensions.AI` 10.7.0 (slice 003) — supplies `IChatClient`,
  `AIFunctionFactory.Create` used by `ChatPlanner.CreateTools()`.
- `Microsoft.Agents.AI` 1.17.0 (slice 003) — the single `AIAgent` the six
  tools attach to.
- `TMDbLib` 3.0.0 (slice 003) — `TMDbClient` used by `TrailerAgent`.
- `Microsoft.Extensions.Http` (slice 003 transitively) — supplies
  `AddHttpClient<T>()` for the typed `WikipediaSearchAgent` registration.
- `OmdbApiNet` 1.3.0 (**NEW**) — supplies `AsyncOmdbClient` used by
  `OpenMovieDbAgent`. Matches the Function App's `MovieTracker.Backend.csproj:44`
  verbatim (package id lowercase 'o'; shipped namespace is `OMDbApiNet`).
  See Phase 0 research for alternatives considered.

No other NuGet reference changes. Slice 003 already pulls in everything else
the ported code needs.

**Storage**: Cosmos DB container `movie-tracker-cosmos`, same documents slice
003 reads and writes. This slice begins writing non-null values to the
existing nullable `FunnyFact` property on `MoviceTrackerChatSession` (slice
002 declared it; slice 003 did not write). No schema change; existing
documents still deserialize.

**Testing**: Manual quickstart against local and deployed revisions, mirroring
slice 003. No test project (owner directive restated in spec Assumptions and
FR-015 debt). The quickstart exercises each of US1-US4 against a fresh
`chatId`, plus the Independent Tests the spec defines per user story.

**Target Platform**: Linux container on Azure Container Apps, same revision
pipeline as slice 003.

**Project Type**: Single ASP.NET Core web service (`MovieTracker.Api`).

**Performance Goals**: None added. The `WikipediaSearchAgent` typed HttpClient
carries the Function App's 15-second timeout verbatim (FR-005). The timeout
is a **per-HTTP-request** bound, not an overall funny-fact deadline — the
funny-fact pipeline runs sequentially inside `GenerateEnhancedFunnyFact`:
one bare `IChatClient.GetResponseAsync` for `DetectEntity`, then
`SearchWikipedia` (one HTTP GET, 15s cap), then `QueryWikidata` (one HTTP
POST, 15s cap), then a second `IChatClient.GetResponseAsync` for the
grounded completion. The task is kicked off before the user message is
appended and awaited after the model reply is appended, so Wikipedia and
Wikidata latency hide behind the model call to the extent that one is at
least as slow as the other; the final `await` adds whatever remains of the
sequential pipeline. No overall latency bound is claimed; the per-request
15s cap is the only hard limit on each Wikipedia / Wikidata leg. No other
new budgets.

**Constraints**:
- One new config key: `OpenMovieDb:Api-Key` (FR-003). All other config stays
  as slice 003 defines it.
- One Bicep file touched: `infrastructure/container-app.bicep` (FR-012).
  `infrastructure/key-vault.bicep` is NOT changed; the owner seeds the
  secret in the vault once before deploy (recorded Dev-Workflow exception,
  FR-012).
- No new abstractions. The four ported classes are kept as the backend
  declares them (concrete, no interfaces).
- No test project.
- No new health probes — the slice imports three new external dependencies
  (OMDb, Wikipedia, Wikidata) without probes (recorded Principle IV
  exception for port parity, FR-015).
- No telemetry (slice 005).
- No frontend change.

**Scale/Scope**:
- 4 ported source files (plus their inner records split one-per-file,
  matching slice 003's CS-1 — see Project Structure).
- `Program.cs`: change the existing `IChatClient` and `AIAgent`
  registrations from `AddSingleton` to `AddScoped` (Research Decision 6);
  add three new `AddScoped` registrations (`OpenMovieDbAgent`,
  `TrailerAgent`, `ChatPlanner`); add one typed-client registration
  (`AddHttpClient<WikipediaSearchAgent>` with Wikimedia User-Agent + 15s
  timeout); append `.. chatPlanner.CreateTools()` to the agent's tool list.
- `Chat/ChatTurnEndpoints.cs`: `RunTurnAsync` grows two lines to kick off
  and await the funny-fact task; the two response envelopes wire
  `session.FunnyFact` through instead of literal `null`.
- `MovieTracker.Api.csproj`: adds `OmdbApiNet` 1.3.0 (one PackageReference).
- `infrastructure/container-app.bicep`: adds one entry to `secrets` and
  one entry to `envVars`.
- No tests, no probes, no telemetry, no new routes. Expected diff: ~800
  lines of C# (most of it ported source with trivial namespace adaptation
  and the coding-standard adjustments above), plus the small infra and
  endpoint edits.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Evaluation |
|---|---|
| I. Managed Identity Over Secrets (NON-NEGOTIABLE) | PARTIAL. OMDb has no identity-based auth path (`AsyncOmdbClient` takes an API key only, same as TMDb). The key reaches the container as a Key Vault reference surfaced as a container secret env var (`OpenMovieDb__Api-Key` → `omdb-api-key` secret), matching the slice 003 pattern for TMDb. This is the Principle I escape clause ("permitted ONLY where the target service offers no identity-based path, and then only as a Key Vault reference surfaced as a container secret") — a compliant use, not an exception. OpenAI's Principle I exception from slice 003 is unchanged. |
| II. Infrastructure Is Bicep, Not Clicks | PASS. One Bicep file edited (`container-app.bicep`). No portal clicks required for infrastructure wiring. The owner-seeds-secret step on the Key Vault is a recorded Dev-Workflow exception (below). |
| III. Incremental Migration With Declared Boundaries | PASS. Spec names what is imported (six ChatPlanner tools, three agent classes, funny-fact flow) and what is not (telemetry, probes, new tools, new entity types, frontend, `FunnyFact` in the structured-output schema). This plan does not exceed those boundaries. |
| IV. Dependencies Prove Themselves | RECORDED EXCEPTION. OMDb, Wikipedia, and Wikidata are new external dependencies and the Function App ships no probes for them; this slice ports as-is (spec FR-015). The two failure modes — missing key (503 via DI resolution failure) and present-but-rejected key (200 with graceful degraded answer) — are explicitly documented in spec FR-015 and are not laundered as compliance. Recorded in Complexity Tracking. A follow-up slice may add the probes. |
| V. No Secret Material In Source | PASS. The OMDb key never appears in any committed file (FR-013/FR-014). Local development reads it from `dotnet user-secrets` under the same key path. SC-003 verifies by repo-wide grep for the Function App's compromised key value and for the config path. |

**Dev-Workflow exception (recorded, FR-012)**: the owner manually seeds the
`omdb-api-key` Key Vault secret before the first deploy that includes this
slice. The constitution forbids documented manual pre-deployment steps; this
spec inherits the pattern slice 003's two existing secrets already use
(same vault, same `keyVaultUrl` reference form) rather than deviating from
it mid-feature. Removing the manual step for all three secrets at once is
deferred to a dedicated follow-up slice. Recorded in Complexity Tracking.

**Health contract**: `/health`, `/health/ready`, `/health/cosmos`,
`/health/tmdb`, `/health/openai` all stay unchanged. No new probes.

## Project Structure

### Documentation (this feature)

```text
specs/004-chat-planner-tools/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── funny-fact-field.md
├── checklists/
│   └── requirements.md
└── spec.md
```

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                             # EDIT: change the existing AIAgent
│                                          # registration from AddSingleton to
│                                          # AddScoped (Research Decision 6); add
│                                          # three new scoped registrations
│                                          # (OpenMovieDbAgent, TrailerAgent,
│                                          # ChatPlanner); add one typed-client
│                                          # registration (AddHttpClient<WikipediaSearchAgent>
│                                          # with Wikimedia User-Agent + 15s timeout);
│                                          # append `.. chatPlanner.CreateTools()`
│                                          # to the agent's tool list. ChatPlanner's
│                                          # factory resolves the Scoped IChatClient
│                                          # (slice 003's registration ALSO moves
│                                          # from Singleton to Scoped as part of
│                                          # this change — see Research Decision 6).
├── Chat/
│   ├── ChatTurnEndpoints.cs               # EDIT: RunTurnAsync kicks off
│   │                                      # SafeGenerateFunnyFactAsync before
│   │                                      # appending the user message, awaits
│   │                                      # after the model reply is appended,
│   │                                      # and sets session.FunnyFact ONLY when
│   │                                      # the awaited result is non-null
│   │                                      # (FR-007 clause 4). The two response
│   │                                      # envelope constructions pass
│   │                                      # session.FunnyFact instead of
│   │                                      # literal null.
│   └── (slice 003 types unchanged)
├── Agents/                                # NEW folder (ports from backend's
│   │                                      # Agents/, matching the same name
│   │                                      # per spec Key Entities).
│   ├── OpenMovieDbAgent.cs                # The agent class (scoped). Constructor
│   │                                      # reads IConfiguration["OpenMovieDb:Api-Key"].
│   ├── MovieRatingResult.cs               # Record ported from backend
│   │                                      # Agents/OpenMovieDbAgent.cs top
│   │                                      # (CS-1: one record per file).
│   ├── RatingComparisonResult.cs          # Record ported from backend ditto.
│   ├── TrailerAgent.cs                    # Agent (scoped). Constructor takes
│   │                                      # the slice 003 TMDbClient singleton.
│   ├── WikipediaSearchAgent.cs            # The agent class only. Registered
│   │                                      # via AddHttpClient<T>() — NOT
│   │                                      # AddScoped<T>() alongside (backend
│   │                                      # note at Program.cs:202-205).
│   ├── WikipediaResult.cs                 # One record per file. Public shape
│   │                                      # returned by GetEnhancedInfo.
│   ├── WikipediaContent.cs                # ditto
│   ├── WikidataInfo.cs                    # ditto
│   ├── WikipediaSummary.cs                # JSON DTO for Wikipedia REST summary.
│   │                                      # Keep [JsonPropertyName] attributes
│   │                                      # verbatim (FR-005: load-bearing).
│   ├── WikipediaThumbnail.cs              # JSON DTO (nested).
│   ├── WikidataResponse.cs                # SPARQL JSON DTO.
│   ├── WikidataHead.cs                    # ditto
│   ├── WikidataResults.cs                 # ditto
│   └── WikidataValue.cs                   # ditto
├── Prompts/
│   ├── ChatPlanner.cs                     # NEW. Scoped. Ported from backend.
│   │                                      # Holds the bare IChatClient, the
│   │                                      # three agents, and exposes
│   │                                      # CreateTools() with the six FR-001
│   │                                      # entries. Private helpers:
│   │                                      # DetectEntity, ParseDetectedEntity,
│   │                                      # NullIfNone, DetectedEntity record,
│   │                                      # GenerateEnhancedFunnyFact (not a
│   │                                      # tool; called by RunTurnAsync).
│   ├── TheMovieDBKernelFunctions.cs       # Unchanged (slice 003).
│   └── DateTimeKernelFunctions.cs         # Unchanged (slice 003).
├── ChatSessions/                          # Unchanged (slice 003).
├── Controllers/                           # Unchanged (slice 003).
├── Models/                                # Unchanged (slice 003).
├── Core/                                  # Unchanged (slice 003).
└── MovieTracker.Api.csproj                # EDIT: add one PackageReference:
                                           #   OmdbApiNet 1.3.0 (lowercase 'o';
                                           #   shipped namespace is OMDbApiNet).

infrastructure/
├── container-app.bicep                    # EDIT: append to `secrets` and
│                                          # `envVars` as FR-012 specifies.
│                                          # No other change.
├── key-vault.bicep                        # UNCHANGED. Owner-seeded secret
│                                          # (recorded Dev-Workflow exception).
└── (all other Bicep files unchanged)
```

**Structure Decision**: Single existing ASP.NET Core project. New folders
`Agents/` and `Prompts/ChatPlanner.cs` match the backend's layout exactly
(spec FR-003/FR-004/FR-005/FR-001 anchor on the backend's paths). Inner
record splits follow slice 003's one-public-type-per-file convention
without introducing new abstractions. No test project.

**Delete-not-rewrite note**: nothing is deleted in this slice.
`Tools/` was already removed by slice 003.

## Complexity Tracking

> Deviations from Core Principles that must be justified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle IV (Dependencies Prove Themselves) — no health probes added for OMDb, Wikipedia, or Wikidata. | The Function App ships no such probes and this slice is a port, not a design (spec FR-015). Adding probes exceeds the declared boundary (Principle III) and the owner has recorded this as an exception; the two failure modes are explicitly documented in FR-015. | Adding probes would (a) exceed the slice's declared boundary, (b) require re-deriving reasonable probe cadences for three new dependencies, and (c) multiply the deployed revision's cold-start cost — all for behaviour the Function App does not have. A follow-up slice may add them under its own spec. |
| Development Workflow — owner manually seeds the `omdb-api-key` Key Vault secret before deploy (FR-012). | The slice 003 pattern already in `container-app.bicep` references two Key Vault secrets whose per-secret resources are NOT declared in `key-vault.bicep`; both are seeded by the owner. Deviating from this mid-feature by declaring `omdb-api-key` in Bicep would mean one of three secrets follows a different pattern than the other two — strictly worse than fixing all three at once. | Declaring the secret in `key-vault.bicep` only for OMDb would leave the inherited two secrets as the odd-ones-out, with no reduction in net manual steps. The right fix is "declare all three secret resources + their pipeline parameters" in one dedicated slice, which this slice avoids by design. |
