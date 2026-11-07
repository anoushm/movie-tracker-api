# Research: Cleanup and Naming

**Slice**: 007-cleanup-naming | **Date**: 2026-11-06

## Overview

This is a pure refactor. The spec carries no `NEEDS CLARIFICATION` markers and no technology choices. Research consists of verifying that the mechanical renames and moves do not accidentally change anything the LLM, Cosmos, or telemetry observes.

## Decisions

### D1. Tool method names are the model's tool surface and MUST NOT change

- **Decision**: Rename only the enclosing classes (`OpenMovieDbAgent` → `OmdbTools`, `TrailerAgent` → `TrailerTools`, `WikipediaSearchAgent` → `WikipediaTools`, `TheMovieDBKernelFunctions` → `TmdbTools`, `DateTimeKernelFunctions` → `DateTimeTools`). Leave every method name registered through `AIFunctionFactory.Create` unchanged.
- **Rationale**: `AIFunctionFactory.Create(Delegate)` derives the tool's name from the method name. The LLM has been prompted and tuned against those names; changing them changes what the model emits and which calls dispatch.
- **Alternatives considered**: Renaming methods to drop any lingering "Agent" nouns (rejected — behavior change and out of scope).

### D2. `ChatClientAgentOptions.Name = "MovieTracker.Api"` is telemetry, not identity

- **Decision**: Keep the agent `Name` property as `"MovieTracker.Api"` even after the inline agent moves to `Agents/MovieAgent.cs`.
- **Rationale**: This value is emitted on OpenTelemetry spans and logs as the agent's display name. Changing it breaks continuity of dashboards and queries on Application Insights.
- **Alternatives considered**: Renaming to `"MovieAgent"` for internal consistency (rejected — telemetry name change is out of scope).

### D3. Cosmos document shape and the misspelled `MoviceTrackerChatSession` type

- **Decision**: Rename the C# type to `MovieTrackerChatSession`. Keep the `id` property lowercase, keep every record/property name in the session's serialised form byte-identical. Verify that no `[JsonPropertyName("Movice...")]` or similar attribute pins the misspelled name to the wire format; if one exists, keep the attribute and only change the C# type name.
- **Rationale**: Existing Cosmos documents are keyed on `id` and serialise with the current property names. A migration of stored documents is out of scope.
- **Alternatives considered**: A dual-shape fallback that reads both the typo'd and corrected names (rejected — no stored document uses the C# type name as its JSON key; there is nothing to fall back on).

### D4. Registration extension placement follows "folder of what it registers"

- **Decision**: Place each `Add*`/`Map*` extension in a static class under the folder of the artefacts it registers (`Telemetry/`, `Tools/`, `Agents/`, `ChatSessions/`, `Chat/`). The one exception is `HealthEndpoints.cs`, which lives at the project root because the health endpoints do not belong to any single feature folder.
- **Rationale**: Co-locates registration with the thing registered, which is the one invariant a refactor-only reviewer can audit without running the app.
- **Alternatives considered**: A single `Startup/` folder with all extensions (rejected — reintroduces the "central hub" that this slice is dismantling).

### D5. Registration order within `AddMovieAgent`

- **Decision**: Preserve the pre-slice registration order inside the composition root: named `HttpClient("HttpClient")` → `TMDbClient` + tool classes (`AddMovieTools`) → `IChatClient` + `AIAgent` (`AddMovieAgent`) → Cosmos + repository (`AddChatSessions`).
- **Rationale**: `IChatClient` consumes the named `HttpClient` via `IHttpClientFactory`. The named `HttpClient` must be registered before `IChatClient` to satisfy DI graph construction.
- **Alternatives considered**: Co-locating the typed `HttpClient<WikipediaTools>` registration with `AddMovieAgent` (rejected — the Wikipedia typed client is a tool concern, not an agent concern).

### D6. No contracts/ folder for this slice

- **Decision**: Do not emit `contracts/`.
- **Rationale**: The slice's correctness is defined by "every retained endpoint's contract is byte-identical to pre-slice." A `contracts/` artefact would restate the current OpenAPI document and open a seam for drift. The HTTP contract is already captured by the running service; adding a parallel spec is noise.
- **Alternatives considered**: A `contracts/no-change.md` placeholder (rejected — a placeholder with no content is clutter).

### D7. No test project is created

- **Decision**: This slice does not add a `MovieTracker.Api.Tests` project.
- **Rationale**: The spec's success criteria rely on `dotnet build` + manual smoke tests + ripgrep checks. Adding a test project would be new work outside the declared scope.
- **Alternatives considered**: Adding integration tests for the retained chat endpoints (rejected — explicitly out of scope per spec FR-006 / "Out of Scope").

### D8. Verifying "no code comments" and related style rules in changed files

- **Decision**: After the refactor, run the checks specified in `quickstart.md` section S4 over the set of files changed by the slice:
  - `rg -n "///"` (documentation comments).
  - `rg -nP "(^|\s)//(?!/)"` (line comments, inline or own-line, excluding `///`).
  - `rg -nU "/\*[\s\S]*?\*/"` (block comments, multi-line aware).
  - `rg -n "\bvar\s"` (explicit types, no `var`).
  - `rg -n "\bprivate\s+(readonly\s+|static\s+|static\s+readonly\s+)?[A-Za-z0-9_<>?.\[\]]+\s+_[a-zA-Z0-9]"` (no underscore prefix on private fields).
  - Manual `rg` for async method names: every `async` method whose method name is **not** registered as a tool must end in `Async`. Tool methods (those wrapped by `AIFunctionFactory.Create`) keep their pre-slice names (FR-002).
- **Rationale**: Mechanical renames tend to leave legacy comments in place; a single anchored line-comment pattern misses trailing `// ...` on code lines and block comments entirely. FR-008 is explicit that **all** code comments are removed from changed files, not just doc comments.
- **Wire-name exemption**: `id` (on `MovieTrackerChatSession`) and `role` (JSON discriminator on `ChatMessage`), plus every record property name whose casing is consumed by the frontend or Cosmos, are exempt from PascalCase enforcement. FR-005 freezes the wire shape.
- **Alternatives considered**: Running the check repo-wide (rejected — unchanged files are not in scope and may legitimately carry comments outside this slice's purview).

### D9. The `.http` file edit is a single-request deletion

- **Decision**: Remove only the `weatherforecast` request block from `MovieTracker.Api.http`. Keep every other request as it is.
- **Rationale**: The `.http` file is a developer scratchpad, not the HTTP contract. Dropping the request that calls a deleted endpoint keeps it honest without reformatting the rest.
- **Alternatives considered**: Rebuilding the `.http` file from the current endpoint list (rejected — makes the diff noisy and invites unintended changes).

## Open questions

None. The slice is bounded by the spec and prompt, and every uncertainty surfaced during planning resolved to a documented decision above.
