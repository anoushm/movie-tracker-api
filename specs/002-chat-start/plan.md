# Implementation Plan: Chat-Start Session Persistence

**Branch**: `002-chat-start` | **Date**: 2026-10-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-chat-start/spec.md`

## Summary

Add `GET /api/chat/start` to the Container App. The endpoint generates a chat
identifier, creates one Cosmos document carrying the movie-expert system prompt
as its first chat-history entry, and returns `{ chatId }`. All Cosmos access
reuses the `CosmosClient` registered by slice 001 (managed identity, system-text
JSON serializer, `Cosmos:Database` / `Cosmos:Container` configuration keys).
No infrastructure changes.

The code ported from `movie-tracker-backend` is `MoviceTrackerChatSession`,
`ChatSessionRepository` (create path only), `GenId`, and the Chat-Start system
prompt. The OpenTelemetry `Tracer` dependency and the `GetChatSession` /
`SaveChatSession` methods are dropped (telemetry is slice 005, reads are
future-slice). The `HydratedMovies` property is dropped from the ported class
entirely so no TMDb model types are referenced in this slice; a later slice
re-adds it with its real type.

## Technical Context

**Language/Version**: C# / .NET 10 (matches existing API)

**Primary Dependencies**: ASP.NET Core minimal API, `Microsoft.Azure.Cosmos`
(already referenced by slice 001), `Microsoft.Extensions.AI` (`ChatMessage`,
already referenced in `Program.cs`), `Azure.Identity` (already referenced).
No new NuGet packages.

**Storage**: Cosmos DB account `movie-tracker-cosmos`, database and container
from `Cosmos:Database` / `Cosmos:Container` (both resolved by slice 001).

**Testing**: Manual quickstart against local and deployed instances per spec
SC-001, SC-002, SC-005. No test project is introduced by this slice.

**Target Platform**: Linux container running on Azure Container Apps; same
revision pipeline as slice 001.

**Project Type**: Single ASP.NET Core web service (`MovieTracker.Api`).

**Performance Goals**: None imposed by spec. One Cosmos `CreateItemAsync` per
request. No batching, no cache.

**Constraints**:
- No new config keys, no new environment variables, no new role assignments.
- No persistence abstraction, repository pattern, or interface indirection
  broader than the single endpoint (spec Out of Scope).
- No telemetry, custom metrics, or additional structured logging (spec Out of
  Scope; slice 005 handles telemetry).
- No read/update methods on the repository (spec declares the surface
  write-only).

**Scale/Scope**: 1 new endpoint, 1 repository class (one method), 1 model
class, 1 string constant (system prompt). Expected diff: ~150 lines of C#.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Evaluation |
|---|---|
| I. Managed Identity Over Secrets (NON-NEGOTIABLE) | PASS. Reuses slice-001's `CosmosClient` registered with `DefaultAzureCredential`. FR-006 forbids keys / connection strings / SAS / Key Vault secrets. No new credential paths. |
| II. Infrastructure Is Bicep, Not Clicks | PASS. No Bicep changes. FR-012 explicitly forbids new Azure resources, role assignments, or container env vars. |
| III. Incremental Migration With Declared Boundaries | PASS. Spec names imports (repository create path, model, `GenId`, system prompt) and an explicit Out of Scope list (ask endpoint, agent, tools, hydration, telemetry, abstractions, backfill, frontend). |
| IV. Dependencies Prove Themselves | PASS. The Cosmos dependency is already proven by `GET /health/cosmos` (slice 001). This slice adds no new external dependency. |
| V. No Secret Material In Source | PASS. No secrets introduced. SC-003 verifies by repository search. |

No violations. Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/002-chat-start/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── chat-start.md
├── checklists/
│   └── requirements.md
└── spec.md
```

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                   # Add GET /api/chat/start endpoint; register ChatSessionRepository
├── ChatSessions/                # New folder for the three ported files
│   ├── MoviceTrackerChatSession.cs
│   ├── ChatSessionRepository.cs
│   └── ChatStartSystemPrompt.cs # Verbatim prompt string constant
└── MovieTracker.Api.csproj      # Unchanged (dependencies already present)

infrastructure/                   # UNCHANGED (FR-012)
```

**Structure Decision**: Single existing ASP.NET Core project. Ported chat-session
code lives in `MovieTracker.Api/ChatSessions/` as a self-contained folder. No
`Services/`, `Repositories/`, `Models/` scaffolding is introduced — the spec's
Out of Scope list rejects persistence abstractions and interface indirection,
and three files in a feature folder is cheaper to delete later than a layered
tree.

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.
