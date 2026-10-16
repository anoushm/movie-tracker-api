---

description: "Task list for slice 002 — Chat-Start Session Persistence"
---

# Tasks: Chat-Start Session Persistence

**Input**: Design documents from `/specs/002-chat-start/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No test project is introduced in this slice (plan Technical Context → Testing; research Decision 8). Verification is manual against quickstart.md.

**Organization**: Grouped by user story to preserve independent testability. US1 (P1) is the whole slice; US2 (P2) is a property check on the ids US1 already produces.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)

## Path Conventions

Single ASP.NET Core project at repository root. All C# sources below live under
`MovieTracker.Api/`. Infrastructure under `infrastructure/` is NOT touched by
this slice (plan FR-012).

---

## Phase 1: Setup

**Purpose**: Nothing to set up. No new NuGet packages, no new config keys, no
new Bicep, no new folder scaffolding beyond the one folder created implicitly
by Phase 3's file additions. Slice 001 already provides the `CosmosClient`
registration, the `CosmosSystemTextJsonSerializer`, and the
`Cosmos:Endpoint` / `Cosmos:Database` / `Cosmos:Container` configuration.

_No tasks in this phase._

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Nothing to add. The only shared infrastructure the endpoint needs
(`CosmosClient` singleton, system-text JSON serializer, config keys) is already
registered in `Program.cs` by slice 001. Verified by `GET /health/cosmos`.

_No tasks in this phase._

**Checkpoint**: Slice 001 is deployed and `/health/cosmos` returns 200 locally
and in Azure. If not, stop and fix slice 001 first.

---

## Phase 3: User Story 1 — Caller opens a new chat and receives a session identifier (Priority: P1) 🎯 MVP

**Goal**: `GET /api/chat/start` returns `{ chatId }` and writes exactly one Cosmos document whose `id` and `PartitionKey` both equal `chatId` and whose `ChatHistory` holds the movie-expert system prompt as its only message.

**Independent Test**: Run `quickstart.md` Checks 1, 2, 3, 4 locally and against the deployed FQDN. Checks are self-contained (one call → inspect body → point-read Cosmos / Portal query).

### Implementation for User Story 1

- [x] T001 [P] [US1] Create `MovieTracker.Api/ChatSessions/ChatStartSystemPrompt.cs` — `public static class ChatStartSystemPrompt { public const string Text = """<prompt>"""; }`. Text is the system prompt from `movie-tracker-backend` `Functions/Function.cs` lines 390–477, copied verbatim.
- [x] T002 [P] [US1] Create `MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs` — preserve the `Movice...` misspelling. Properties: `string id`, `string PartitionKey`, `List<Microsoft.Extensions.AI.ChatMessage> ChatHistory`, `string? FunnyFact`. Do NOT declare `HydratedMovies` (research Decision 3).
- [x] T003 [US1] Create `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs` — constructor `(CosmosClient cosmosClient, string database, string container)` resolves `Container` via `cosmosClient.GetContainer(database, container)`. One public method `Task<MoviceTrackerChatSession> NewChatSession(List<Microsoft.Extensions.AI.ChatMessage> initialHistory)` → generate `id`, build session, `CreateItemAsync(session, new PartitionKey(id))`, return. One `private static string GenId()` copied verbatim from source: `RandomNumberGenerator.GetBytes(5)` → `Convert.ToBase64String` → `Replace('/', '~').Replace('+', '-').Replace("=", "")` → recurse if result still contains `-` or `~`. No `ILogger`, no `Tracer`, no try/catch, no `GetChatSession`, no `SaveChatSession` (research Decisions 2, 4, 6). If this file ends up importing both `Microsoft.Extensions.AI` and `OpenAI.Chat`, qualify `ChatMessage` fully (both namespaces define that type).
- [x] T004 [US1] In `MovieTracker.Api/Program.cs`, after the existing `CosmosClient` singleton registration, register `ChatSessionRepository` as a singleton: `builder.Services.AddSingleton(sp => new ChatSessionRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabase, cosmosContainer));` (reuses the local `cosmosDatabase` / `cosmosContainer` strings already read at startup).
- [x] T005 [US1] In `MovieTracker.Api/Program.cs`, after the existing minimal-API map blocks, add `app.MapGet("/api/chat/start", async (ChatSessionRepository repo, ILogger<Program> logger) => { try { MoviceTrackerChatSession session = await repo.NewChatSession(new List<Microsoft.Extensions.AI.ChatMessage> { new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, ChatStartSystemPrompt.Text) }); return Results.Ok(new { chatId = session.id }); } catch (Exception ex) { logger.LogError(ex, "Chat-Start session creation failed."); return Results.Json(new { error = "Session creation failed." }, statusCode: 503); } }).WithName("ChatStart");` — `Microsoft.Extensions.AI.ChatMessage` and `ChatRole` are fully qualified because `Program.cs` also imports `OpenAI.Chat`, which defines both type names. Behaviour per research Decision 5 and the chat-start contract.

### Verification for User Story 1 — Local

- [x] T006 [US1] Run `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`, then execute `quickstart.md` Check 1. Expected: `status=200 chatId=<7 chars>`. (Also satisfies US2's regex assertion on a single id.)
- [x] T007 [US1] Execute `quickstart.md` Check 2 against the returned `chatId`. Expected: document exists with `id = PartitionKey = chatId` and `ChatHistory` is one system message carrying the system prompt verbatim.
- [x] T008 [US1] Execute `quickstart.md` Check 3. Expected: 5 distinct ids, each matching `^[A-Za-z0-9]{7}$`, all point-read to documents. (Satisfies US2's Independent Test.)
- [x] T009 [US1] Execute `quickstart.md` Check 4 (regression on `/health`, `/health/ready`, `/health/cosmos`, `/version`, `POST /Ask`). Expected: health/ready/cosmos/version return 200; `/Ask` returns whatever it returned on `main` before this slice (compare against a pre-change baseline — do not require 200 if `main` was not already 200).
- [x] T010 [US1] Execute `quickstart.md` Error path section. Expected: `status=503`, body is `{"error":"Session creation failed."}`, no secret-like substrings in the body, server log contains the `CosmosException`. Restore `Cosmos:Container` afterwards.

### Verification for User Story 1 — Deployed

- [ ] T011 [US1] **OWNER step** — the owner commits the slice and the `acr-build-push` GitHub workflow deploys the new revision. Do NOT run `az acr build` or `az containerapp update` from this agent. Stop after T010 and report; T012 resumes once the owner confirms the deployed revision is active.
- [ ] T012 [US1] Repeat `quickstart.md` Checks 1, 2, 3, 4 against the Container App's ingress FQDN. Expected: identical outcomes to T006–T009.

**Checkpoint**: At the end of Phase 3, `GET /api/chat/start` is live locally and in Azure, writes documents to Cosmos via managed identity, and no other endpoint has regressed.

---

## Phase 4: User Story 2 — Session identifiers are URL-safe (Priority: P2)

**Goal**: Every `chatId` contains only `[A-Za-z0-9]`.

**Independent Test**: `quickstart.md` Check 1 regex (`^[A-Za-z0-9]{7}$`) and Check 3 per-id regex both pass. US2 adds no implementation work — the ported `GenId` already enforces this via retry (FR-008, FR-009). Traceability is recorded on T006 and T008; see the parenthetical notes on those tasks.

_No tasks in this phase._

**Checkpoint**: If either regex check throws under T006 or T008, `GenId` is not retrying correctly — fix in `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs` (research Decision 6; FR-008) before continuing.

---

## Phase 5: Polish

- [x] T013 Run `SC-003` grep from `quickstart.md` No-regression check. Expected: zero new matches for `AccountKey=|AccountEndpoint=|SharedAccessSignature|SAS=` beyond what already exists on `main`.

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 Setup: empty.
- Phase 2 Foundational: empty. Precondition is slice 001 being green (`/health/cosmos` → 200).
- Phase 3 US1: depends on preconditions; T001 + T002 can run in parallel; T003 depends on T002; T004 depends on T003; T005 depends on T001 (uses `ChatStartSystemPrompt.Text`), T003 (uses `ChatSessionRepository`), and T004 (uses the DI registration); T006–T010 depend on T005 (local verification is serial because `dotnet run` is a single process); T011 depends on T005; T012 depends on T011.
- Phase 4 US2: no tasks. Traceability recorded on T006 and T008.
- Phase 5 Polish: depends on the diff being essentially final.

### Parallel Opportunities

- T001 and T002 are different files with no dependencies → `[P]`.
- Everything else is serial by file (`ChatSessionRepository.cs` → `Program.cs`) or by shared process (local verification, deployed verification).

---

## Parallel Example: User Story 1

```bash
# T001 and T002 are the only parallel pair:
Task: "Create MovieTracker.Api/ChatSessions/ChatStartSystemPrompt.cs"
Task: "Create MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 and Phase 2 are empty — skip.
2. Phase 3: implement T001 → T005, run local verification T006 → T010, deploy T011 and re-run T012.
3. Stop and demo. US1 is the entire feature's user-visible behaviour.

### Incremental Delivery

Not applicable at a cross-story level — US2 is a property of US1's output, not a separate increment. The one meaningful split is "local only" (T001–T010) vs "deployed" (T011–T012), which the task list already orders.

---

## Notes

- `[P]` tasks = different files, no dependencies.
- No test project this slice. If a regression surfaces, add it to the next slice that owns the regressed code path rather than scaffolding a test project here.
- Preserve the misspelled type name `MoviceTrackerChatSession` on purpose (data-model.md).
- No inline comments in the ported files — strip any that the source carries.
- Any Bicep edit, new NuGet package, new config key, or new environment variable is out of scope — if a task appears to require one, re-read spec.md Out of Scope before proceeding.
