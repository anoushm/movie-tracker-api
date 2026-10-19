# Implementation Plan: Agent Turn With TMDb Tools And Hydration

**Branch**: `003-agent-turn` | **Date**: 2026-10-19 | **Spec**: [spec.md](./spec.md)

## Summary

Import the Function App's agent turn into the Container App. The slice adds
`POST /api/chat/{chatId}/ask` and `POST /api/chat/{chatId}/ask/v2`, backed by one
DI-registered `AIAgent` that uses the ported `TheMovieDBKernelFunctions` and
`DateTimeKernelFunctions`, extends `ChatSessionRepository` with read and save,
grows `MoviceTrackerChatSession` by a `HydratedMovies` map, replaces
`Tools/TheMovieDBTool.cs` with the backend's tool set wired through one
`TMDbClient` singleton, keeps `POST /ask` stateless on the same agent, and
removes the three existing per-agent registrations. The ported turn runner,
context reducer, hydration cache, and PascalCase structured-output schema are
behaviour-preserving ports from `Functions/Function.cs` — same logic, same
tool signatures, same prompts, same JSON shapes. The source form adapts to
this project's coding standards: explicit types instead of `var`, braces on
every `if`/`else`/`for`/`while` even when single-line, and no inline comments
(doc-comments on public types or XML `<summary>` blocks are allowed; backend
in-method comments are dropped). Two new dependency probes (`/health/tmdb`,
`/health/openai`) satisfy constitution Principle IV. Manual quickstart against
local and deployed revisions is the only verification, per owner decision.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core (matches slices 001 and 002).

**Primary Dependencies**: `Microsoft.Agents.AI` 1.17.0 (already referenced;
supplies `AIAgent`, `ChatClientAgent`, and the preview `Microsoft.Agents.AI.Compaction`
namespace used under `#pragma warning disable MAAI001`), `Microsoft.Agents.AI.OpenAI`
1.17.0 (already referenced), `Microsoft.Extensions.AI` 10.7.0 (already referenced),
`Azure.AI.OpenAI` 2.8.0-beta.1 (already referenced), `Microsoft.Azure.Cosmos`
3.62.0 (already referenced; reused from slice 001), `TMDbLib` 3.0.0 (already
referenced), `Microsoft.Extensions.Http.Resilience` (NEW — supplies
`AddStandardResilienceHandler`, which is what FR-009's named-HttpClient
pipeline is built from in the Function App). `IDistributedCache` and
`AddDistributedMemoryCache` come from the `Microsoft.AspNetCore.App` shared
framework the project already references.

**Storage**: Cosmos DB account `movie-tracker-cosmos`, same container used by
slice 002. Document shape gains a nullable `HydratedMovies` property (FR-006);
existing documents still deserialize.

**Testing**: Manual quickstart against local and deployed revisions, mirroring
slice 002. No test project is introduced (owner decision; FR-016 edge cases
are exercised in quickstart error paths).

**Target Platform**: Linux container on Azure Container Apps, same revision
pipeline as slices 001 and 002.

**Project Type**: Single ASP.NET Core web service (`MovieTracker.Api`).

**Performance Goals**: None added by spec. The FR-009 OpenAI pipeline carries
the Function App's measured budgets verbatim (120s total / 60s per-attempt / 3
retries / 120s sampling); hydration bound is 150 entries per session document
(FR-011); tool-iteration cap is 12 per request (FR-009). No new SLOs.

**Constraints**:
- No new config keys. Reuse `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`,
  `AzureOpenAI:DeploymentName`, `TheMovieDb:Api-Key`, `Cosmos:Endpoint`,
  `Cosmos:Database`, `Cosmos:Container`. No reasoning-effort key, no OMDb key,
  no telemetry keys.
- No Bicep changes (spec Out of Scope and owner directive).
- No test project (owner directive — manual verification only).
- No new abstractions or interfaces beyond what the ported code already uses
  (owner directive). `TheMovieDBKernelFunctions` and `DateTimeKernelFunctions`
  are kept as the backend declares them (concrete classes, not interfaces).
- Session concurrency control, OMDb, ChatPlanner tools, custom telemetry, and
  frontend changes are explicitly out of scope (spec).
- `AzureOpenAI:ApiKey` continues to authenticate the chat client — spec-level
  Principle I exception, re-stated in Complexity Tracking.

**Scale/Scope**: 2 new chat routes + 2 new probe routes; 1 enlarged repository
(adds read + save); 2 ported kernel-function files; 1 new DTO file set; 1
rewritten `Program.cs` block for agent/tool registrations. Expected diff:
roughly 1500 lines of C# (most of it is ported source copied with trivial
namespace and config-key adaptations).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Evaluation |
|---|---|
| I. Managed Identity Over Secrets (NON-NEGOTIABLE) | PARTIAL. Cosmos continues to use managed identity (slice 001). TMDb uses an API key as before (`TheMovieDb:Api-Key`, Key Vault reference, no identity-based path exists). Azure OpenAI continues to use `AzureOpenAI:ApiKey` as a justified Principle I exception recorded in FR-008 and Assumptions, because `movie-tracker-openai` lives in a different subscription and a cross-subscription role assignment is unavailable for this slice. Captured in Complexity Tracking; retirement is a follow-up slice per Governance. |
| II. Infrastructure Is Bicep, Not Clicks | PASS. No Bicep changes. Spec Out of Scope and owner directive. |
| III. Incremental Migration With Declared Boundaries | PASS. Spec names what is imported (chat routes, repository read/save, hydration map, TMDb tool set, DateTime tool set, context reducer, structured-output format, resilient HttpClient) and what is not (ChatPlanner tools, OMDb, custom telemetry, reasoning-effort setting, frontend). |
| IV. Dependencies Prove Themselves | PASS. The two new load-bearing dependencies (TMDb and Azure OpenAI) each get a probe (`/health/tmdb`, `/health/openai`, FR-018). The existing Cosmos probe (`/health/cosmos`, slice 001) is unchanged. |
| V. No Secret Material In Source | PASS. All secrets stay in Key Vault / user-secrets / env vars. No new secret shape introduced. SC-005 verifies by configuration diff. |

**Health contract**: `/health` and `/health/ready` remain cheap and
dependency-free. The new probes live at their own paths (`/health/tmdb`,
`/health/openai`), as the constitution requires.

## Project Structure

### Documentation (this feature)

```text
specs/003-agent-turn/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── chat-ask.md
│   ├── chat-ask-v2.md
│   └── health-probes.md
├── checklists/
│   └── requirements.md
└── spec.md
```

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                          # Rewritten DI: one AIAgent, one IChatClient,
│                                       # named "HttpClient" + AddStandardResilienceHandler,
│                                       # AddDistributedMemoryCache, TMDbClient singleton,
│                                       # TheMovieDBKernelFunctions singleton,
│                                       # chat routes, /health/tmdb, /health/openai.
├── Chat/                               # NEW folder for the ported turn surface.
│   │                                   # One type per file (coding standard CS-1).
│   ├── ChatTurnEndpoints.cs            # The two minimal-API handlers for
│   │                                   # POST /api/chat/{chatId}/ask and /ask/v2;
│   │                                   # ported RunTurnAsync and ToClientMessageAsync;
│   │                                   # ported LoadHydrationCache / SaveSessionAsync;
│   │                                   # ported SafeProcessMovieAsync / ProcessMovieAsync;
│   │                                   # ported context reducer and SummariseToolCalls /
│   │                                   # SummariseArguments / SummariseResult helpers.
│   │                                   # (These are helper methods of the endpoint class,
│   │                                   # not top-level types, so CS-1 permits them.)
│   ├── MovieViewModel.cs               # MovieViewModel record.
│   ├── MovieTrailerInfo.cs             # MovieTrailerInfo record + CreateTrailerInfo helper.
│   ├── ChatMessage.cs                  # Polymorphic ChatMessage base. [JsonPolymorphic]
│   │                                   # discriminator stays lowercase "role" per spec FR-002.
│   ├── UserChatMessage.cs              # UserChatMessage (role = "user").
│   ├── AssistantChatMessage.cs         # AssistantChatMessage (role = "assistant", carries
│   │                                   # MovieList).
│   ├── ChatMessageResponse.cs          # ChatMessageResponse record (/ask envelope).
│   ├── ChatTurnResponse.cs             # ChatTurnResponse record (/ask/v2 envelope).
│   ├── MovieListResponse.cs            # MovieListResponse (model-facing structured-output
│   │                                   # DTO; PascalCase schema).
│   ├── MovieListItem.cs                # MovieListItem (nested in MovieListResponse).
│   └── Ask.cs                          # Ask request DTO (one property: Input).
├── ChatSessions/
│   ├── MoviceTrackerChatSession.cs     # Add nullable HydratedMovies map (FR-006).
│   ├── ChatSessionRepository.cs        # Add GetChatSession and SaveChatSession (FR-007).
│   └── ChatStartSystemPrompt.cs        # Unchanged.
├── Prompts/                            # NEW folder for the ported tool sets.
│   ├── TheMovieDBKernelFunctions.cs    # Ported as-is from backend; constructed by DI
│   │                                   # from the single TMDbClient singleton.
│   └── DateTimeKernelFunctions.cs      # Ported as-is from backend (static methods).
├── Controllers/
│   └── AskController.cs                # Unchanged: resolves the single AIAgent, invokes
│                                       # it WITHOUT the structured-output response format,
│                                       # keeping /ask on {"question":string} → {"answer":string}.
├── Models/
│   ├── AskRequestDot.cs                # Unchanged.
│   └── AskResponseDot.cs               # Unchanged.
├── Core/                               # Unchanged.
└── Tools/                              # DELETED folder.
    ├── TheMovieDBTool.cs               # (removed — FR-014)
    └── DateTimeTool.cs                 # (removed — becomes unreferenced after FR-015
                                        # removes the "DateTime Agent" registration;
                                        # its surface is preserved via the ported
                                        # DateTimeKernelFunctions).

MovieTracker.Api/MovieTracker.Api.csproj  # Add ONE PackageReference:
                                          #   Microsoft.Extensions.Http.Resilience (10.x
                                          #   matching the Microsoft.Extensions.AI minor).

infrastructure/                        # UNCHANGED (owner directive).
```

**Structure Decision**: Single existing ASP.NET Core project. The ported chat
code is grouped under `Chat/`, the ported tool sets under `Prompts/` (matching
the backend's folder name). No `Services/` or `Repositories/` scaffolding is
introduced — the owner directive forbids new abstractions and interfaces, and
the ported Function App code is already written to concrete types, so flat
folders are the smallest working shape.

**Delete-not-rewrite note**: `Tools/TheMovieDBTool.cs` is deleted outright
(FR-014). `Tools/DateTimeTool.cs` is also deleted because the agent that
referenced it is removed by FR-015 and the DateTime surface is kept through
the ported `DateTimeKernelFunctions`. The `Tools/` folder is therefore removed.
If the Codex cross-review rejects deleting `DateTimeTool.cs` as outside the
spec's declared boundary, the follow-up is to keep the file (unreferenced) and
flag it as cleanup for the next slice — the agent registration still goes.

## Complexity Tracking

> Deviations from Core Principles that must be justified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle I (Managed Identity Over Secrets) — Azure OpenAI chat client authenticates with `AzureOpenAI:ApiKey` rather than the user-assigned managed identity. | The `movie-tracker-openai` Cognitive Services resource lives in a different subscription (Pay-As-You-Go) from the Container App (Development). A cross-subscription role assignment to the user-assigned managed identity is unavailable in this slice's scope. Spec FR-008 states this explicitly. | Moving the auth path to managed identity requires either (a) co-locating the OpenAI resource and the Container App's identity in one subscription, or (b) establishing cross-subscription role assignments — both are infrastructure changes outside this slice. The owner has committed to retiring the key in a follow-up slice; Governance requires the next feature that touches the OpenAI auth path to do so (spec Assumptions). |
