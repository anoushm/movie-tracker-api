---
description: "Task list for slice 003 — Agent Turn With TMDb Tools And Hydration"
---

# Tasks: Agent Turn With TMDb Tools And Hydration

**Input**: Design documents from `/specs/003-agent-turn/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Organization**: One phase per user story (US1 = P1, US2 = P2, US3 = P3), preceded by Setup and Foundational phases that are shared across all stories. Verification tasks map to specific `quickstart.md` checks.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- All file paths are relative to the repository root (e.g. `MovieTracker.Api/Program.cs`). Reference files outside the repo use absolute paths (e.g. the Function App source under `C:/projects/dev/movie-tracker-backend/`).

## Path Conventions

Single ASP.NET Core project rooted at `MovieTracker.Api/`. Ported Function App source lives at `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/` and is the reference for every port task below.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the single new NuGet package the ported code needs.

- [X] T001 Add `<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.*" />` to `MovieTracker.Api/MovieTracker.Api.csproj` (pick a current 10.x release that aligns with the existing `Microsoft.Extensions.AI` 10.7.0 line). Run `dotnet restore MovieTracker.Api/MovieTracker.Api.csproj` to confirm the package resolves.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Every ported type, the enlarged repository, the enlarged session document, the ported kernel functions, the ported turn runner, and the rewritten DI block. US1/US2/US3 all depend on this phase being complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Remove dead code (FR-014, FR-015)

- [X] T002 Delete `MovieTracker.Api/Tools/TheMovieDBTool.cs` (FR-014).
- [X] T003 Delete `MovieTracker.Api/Tools/DateTimeTool.cs` and the now-empty `MovieTracker.Api/Tools/` folder (`DateTimeTool.cs` becomes unreferenced after FR-015 removes the "DateTime Agent" registration; research.md Decision 8). This task depends on T002.

### Session document and repository (FR-006, FR-007)

- [X] T005 [P] Add a nullable `Dictionary<string, MovieViewModel>? HydratedMovies { get; set; }` property to `MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs` (default null, preserves slice-002 documents on deserialize). Reference: data-model.md "MoviceTrackerChatSession".
- [X] T006 [P] Add two methods to `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`: (a) `public async Task<MoviceTrackerChatSession> GetChatSession(string id)` using `ReadItemAsync` with `new PartitionKey(id)` and (b) `public async Task<MoviceTrackerChatSession> SaveChatSession(MoviceTrackerChatSession session)` using `ReplaceItemAsync`. Mirror the backend's `ChatSessionRepository.GetChatSession` and `SaveChatSession` (file `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/ChatSessionRepository.cs`) with explicit types, braces on every `if`, and no inline comments.

### Ported Chat DTOs — one type per file (data-model.md)

- [X] T007 [P] Create `MovieTracker.Api/Chat/Ask.cs` containing the `Ask` request class with a single `string Input { get; set; } = string.Empty;` property. Namespace `MovieTracker.Api.Chat`. Reference: data-model.md "Ask".
- [X] T008 [P] Create `MovieTracker.Api/Chat/MovieTrailerInfo.cs` containing the `MovieTrailerInfo` record (`Key, Name, Site, Type, Official, YouTubeUrl, EmbedUrl, ThumbnailUrl`, all `string`/`bool` per FR-010) plus the `CreateTrailerInfo(TMDbLib.Objects.General.Video?)` helper ported from `Functions/Function.cs`. Explicit types; braces on every `if`; no inline comments.
- [X] T009 [P] Create `MovieTracker.Api/Chat/MovieViewModel.cs` containing the `MovieViewModel` record with the exact field list from FR-010 (data-model.md "MovieViewModel"). The `Trailer` property is `MovieTrailerInfo?`.
- [X] T010 [P] Create `MovieTracker.Api/Chat/ChatMessage.cs` containing the abstract `ChatMessage` class decorated with `[JsonPolymorphic(TypeDiscriminatorPropertyName = nameof(role))]`, `[JsonDerivedType(typeof(UserChatMessage), "user")]`, and `[JsonDerivedType(typeof(AssistantChatMessage), "assistant")]`. Abstract `string role { get; }` and `string Text { get; }` members. Reference: data-model.md "Chat message polymorphic hierarchy".
- [X] T011 [P] Create `MovieTracker.Api/Chat/UserChatMessage.cs` containing the `UserChatMessage` class derived from `ChatMessage` with `[JsonIgnore] public override string role => "user";`, the overridden `Text` property, and a constructor that takes `string text`. `[JsonIgnore]` is REQUIRED per data-model.md.
- [X] T012 [P] Create `MovieTracker.Api/Chat/AssistantChatMessage.cs` containing the `AssistantChatMessage` class derived from `ChatMessage` with `[JsonIgnore] public override string role => "assistant";`, the overridden `Text` property, a `List<MovieViewModel> MovieList { get; set; } = new();` property, and a constructor that takes `(string text, List<MovieViewModel> movieList)`. `[JsonIgnore]` is REQUIRED per data-model.md.
- [X] T013 [P] Create `MovieTracker.Api/Chat/ChatMessageResponse.cs` containing `public record ChatMessageResponse(string? FunnyFact, List<ChatMessage> Messages);`. Reference: contracts/chat-ask.md.
- [X] T014 [P] Create `MovieTracker.Api/Chat/ChatTurnResponse.cs` containing `public record ChatTurnResponse(string? FunnyFact, ChatMessage Turn);`. Reference: contracts/chat-ask-v2.md.
- [X] T015 [P] Create `MovieTracker.Api/Chat/MovieListItem.cs` containing `public class MovieListItem { public string MovieId { get; set; } public string MovieName { get; set; } }` (the model-facing structured-output item). Reference: data-model.md "MovieListResponse / MovieListItem".
- [X] T016 [P] Create `MovieTracker.Api/Chat/MovieListResponse.cs` containing `public class MovieListResponse { public string SystemMessage { get; set; } public List<MovieListItem> MovieList { get; set; } }`.

### Ported kernel functions (FR-014)

- [X] T017 [P] Create `MovieTracker.Api/Prompts/TheMovieDBKernelFunctions.cs` by copying `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Prompts/TheMovieDBKernelFunctions.cs` with these adaptations only: (a) change `namespace MovieTracker.Backend.Prompts` to `namespace MovieTracker.Api.Prompts`; (b) replace every `var` with the explicit type; (c) add braces around every single-line `if`/`else`/`for`/`while` body; (d) remove every inline comment inside method bodies (keep `[Description]` attributes verbatim and keep `<summary>` XML doc comments on types and public methods); (e) keep the primary constructor `TheMovieDBKernelFunctions(TMDbClient client)` unchanged and the public `CreateTools()` method unchanged (same 8 tools registered). No behaviour change.
- [X] T018 [P] Create `MovieTracker.Api/Prompts/DateTimeKernelFunctions.cs` by copying `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Prompts/DateTimeKernelFunctions.cs` with the same adaptations as T017: namespace change to `MovieTracker.Api.Prompts`, explicit types, braces on every `if`, no inline comments. Keep `[Description]` attributes verbatim and the static `CreateTools()` method unchanged.

### Ported turn runner (FR-005, FR-009, FR-010, FR-011, FR-012, FR-013, FR-016)

- [X] T019 Create `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` under `namespace MovieTracker.Api.Chat`. Port the following from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Functions/Function.cs`, with explicit types, braces on every `if`, and no inline comments:
    - Private static readonly `StructuredOutputJsonOptions` (based on `AIJsonUtilities.DefaultOptions`, `PropertyNamingPolicy = null`).
    - Private static readonly `MovieListResponseFormat` built from `MovieListResponse` with `DisallowAdditionalProperties = true`, `RequireAllProperties = true` (FR-013).
    - `#pragma warning disable MAAI001` ... `#pragma warning restore MAAI001` block containing the `ContextReducer` field built as `new PipelineCompactionStrategy([new ToolResultCompactionStrategy(trigger: CompactionTriggers.HasToolCalls(), minimumPreservedGroups: 2) { ToolCallFormatter = SummariseToolCalls }, new SlidingWindowCompactionStrategy(trigger: CompactionTriggers.TurnsExceed(12), minimumPreservedTurns: 4)]).AsChatReducer()` (FR-012), and the private static helpers `SummariseToolCalls`, `SummariseArguments`, `SummariseResult`, `NameOf` ported verbatim behaviour.
    - Private static constants `MaxModelTurns = 12`, `CollapsedToolResultChars = 160`, `CollapsedArgumentChars = 60`, `CollapsedResultNames = 6`, `HydratedMovieLimit = 150`.
    - Private static helpers: `LoadHydrationCache`, `SaveSessionAsync`, `ProcessMovieAsync`, `SafeProcessMovieAsync`, `ToClientMessageAsync`, `RunTurnAsync`. These map 1:1 to the Function App methods; omit anything related to funny facts (ChatPlanner is out of scope — leave `chatSession.FunnyFact` untouched so it serializes null per FR-004), telemetry (`LogUsage`, `Telemetry.*`, cost pricing, `reasoningEffort`), and any `UseOpenTelemetry` / `InstrumentedAIFunction` wrapping.
    - Two public static minimal-API handlers: `public static async Task<IResult> Ask(string chatId, HttpRequest request, ChatSessionRepository repo, AIAgent agent, IDistributedCache cache, TMDbClient tmdbClient, ILogger<ChatMessageResponse> logger)` and `public static async Task<IResult> AskV2(string chatId, HttpRequest request, ChatSessionRepository repo, AIAgent agent, IDistributedCache cache, TMDbClient tmdbClient, ILogger<ChatTurnResponse> logger)`.
    - Handler bodies follow the Function App's `Message` and `MessageV2` methods, mapped to this project's error contract (FR-016, contracts/chat-ask.md, contracts/chat-ask-v2.md). The exact ordering is:
        1. **Body parsing (400 boundary)**: wrap `Ask? ask = await request.ReadFromJsonAsync<Ask>();` in a `try/catch (JsonException)` that returns `Results.BadRequest(new { error = "Invalid request, missing ask object" })`. If the deserialized `ask` is null, return the same 400 body. This runs BEFORE any session mutation, agent invocation, or Cosmos call. (FR-016, quickstart Check 5 covers both missing and malformed bodies.)
        2. **Everything after body parsing is wrapped in one outer `try/catch (Exception ex)`** that logs `logger.LogError(ex, "...")` and returns `Results.Json(new { error = "Chat request failed." }, statusCode: 503)`. This outer boundary MUST include the Cosmos read, the agent call, hydration, and the Cosmos save — any non-NotFound Cosmos failure (connectivity, auth, timeout) and any model-call failure that survives the FR-009 retry budget surfaces as 503 here (FR-016, quickstart Check 6).
        3. **404 boundary nested inside the outer try**: wrap just `MoviceTrackerChatSession session = await repo.GetChatSession(chatId);` in an inner `try/catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)` that returns `Results.NotFound(new { error = "Chat not found" })`. Other `CosmosException` values fall through to the outer 503 catch (quickstart Check 4 for the 404 path).
        4. On success return `Results.Ok(new ChatMessageResponse(null, responseMessages))` for `/ask` and `Results.Ok(new ChatTurnResponse(null, turn))` for `/ask/v2`. No response body ever contains an exception message, stack trace, credential, or connection string.

### DI rewiring (FR-008, FR-009, FR-014, FR-015)

- [X] T020 Rewrite the agent/tool DI block in `MovieTracker.Api/Program.cs`:
    - **Remove** the `movieAgent` and `dateTimeAgent` locals and both of their `builder.Services.AddSingleton(...)` calls, the `builder.Services.AddSingleton<TheMovieDBTool>()` call, and the factory `builder.Services.AddSingleton<AIAgent>(sp => { ... })` block (lines ~22–71 of the current file).
    - **Add** the `using` statements the ported code needs: `using Microsoft.Extensions.AI;`, `using Microsoft.Extensions.Caching.Distributed;`, `using Microsoft.Extensions.Http.Resilience;`, `using MovieTracker.Api.Chat;`, `using MovieTracker.Api.Prompts;`, `using OpenAI;`, `using System.ClientModel.Primitives;`, `using TMDbLib.Client;`. Keep any existing usings still referenced; drop `using MovieTracker.Api.Tools;`.
    - **Add** `builder.Services.AddDistributedMemoryCache();` (FR-011).
    - **Add** a named HttpClient with the FR-009 settings:
      ```
      builder.Services.AddHttpClient("HttpClient")
          .AddStandardResilienceHandler(options =>
          {
              options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
              options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
              options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
              options.Retry.MaxRetryAttempts = 3;
          });
      ```
    - **Add** `builder.Services.AddSingleton<TMDbClient>(sp => new TMDbClient(builder.Configuration["TheMovieDb:Api-Key"] ?? throw new InvalidOperationException("Missing configuration value: TheMovieDb:Api-Key")));` (FR-014, "one TMDbClient singleton").
    - **Add** `builder.Services.AddSingleton<TheMovieDBKernelFunctions>();` (DI-constructed from the TMDbClient singleton).
    - **Add** a singleton `IChatClient` factory that: resolves `IHttpClientFactory`, creates the "HttpClient" named client, builds an `AzureOpenAIClient` with `new ApiKeyCredential(apiKey.Key)` (reuse the existing `apiKey` and `endpoint` locals) and `new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) }`, calls `.GetChatClient(deploymentName).AsIChatClient().AsBuilder().UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>(), client => { client.MaximumIterationsPerRequest = 12; }).Build(sp);` (FR-009, cap = 12, research.md Decision 7).
    - **Add** a singleton `AIAgent` factory that resolves `IChatClient`, `TheMovieDBKernelFunctions`, and `ILoggerFactory`, builds `List<AITool> tools = [.. tmdb.CreateTools(), .. DateTimeKernelFunctions.CreateTools()];`, and returns `chatClient.AsAIAgent(new ChatClientAgentOptions { Name = "MovieTracker.Api", ChatOptions = new ChatOptions { Tools = tools } }, sp.GetRequiredService<ILoggerFactory>(), sp);` (FR-008, FR-014). Do NOT set `instructions:` (research.md Decision 7 — the system prompt is in the Cosmos `ChatHistory`).

### Build gate

- [X] T021 Run `dotnet build MovieTracker.Api/MovieTracker.Api.csproj`. The build must succeed with no errors. Fix any compile errors introduced by T002–T020 before proceeding.

**Checkpoint**: Foundation ready. All ported source compiles. No chat routes are wired yet; user story work begins now.

---

## Phase 3: User Story 1 - Whole-transcript turn (Priority: P1) 🎯 MVP

**Goal**: `POST /api/chat/{chatId}/ask` runs one turn against the unified agent, hydrates every movie id the transcript references, persists the session, and replays the full hydrated transcript on the wire (contracts/chat-ask.md).

**Independent Test**: `quickstart.md` Checks 1, 2, 4, 5, 6, and 9 pass locally. US1 is the MVP: with only this story shipped, the frontend's current `/ask` consumer works. Check 10 (Function App shape parity for both routes) runs in Phase 6 after US2 is wired.

### Implementation for User Story 1

- [X] T022 [US1] In `MovieTracker.Api/Program.cs`, after the existing `app.MapGet("/api/chat/start", ...)` block, add both minimal-API routes: `app.MapPost("/api/chat/{chatId}/ask", ChatTurnEndpoints.Ask).WithName("ChatAsk");` and `app.MapPost("/api/chat/{chatId}/ask/v2", ChatTurnEndpoints.AskV2).WithName("ChatAskV2");` (contracts/chat-ask.md, contracts/chat-ask-v2.md). Add `using MovieTracker.Api.Chat;` if not already present from T020. Wiring US2's route here with US1's avoids a second API restart in Phase 4.

### Local verification for User Story 1

- [X] T023 [US1] Start the API locally via `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj` (keep it running for all US1 checks). Then run `specs/003-agent-turn/quickstart.md` **Check 1** (first-turn happy path). Expected: `ok: chatId=<7 chars> first-turn-movies=<n>` and a Cosmos document containing the appended user and assistant messages.
- [X] T024 [US1] Run `specs/003-agent-turn/quickstart.md` **Check 2** (second turn on same `chatId`, hydration-cache replay). Expected: `ok: total messages=<>=4>`.
- [X] T025 [US1] Run `specs/003-agent-turn/quickstart.md` **Check 4** (404 for non-existent `chatId`). Expected: `ok: 404 body=<generic body with no secret material>`.
- [X] T026 [US1] Run `specs/003-agent-turn/quickstart.md` **Check 5** (400 for missing/malformed body). Expected: `ok: missing=400 malformed=400`.
- [X] T027 [US1] Run `specs/003-agent-turn/quickstart.md` **Check 6** (503 for a Cosmos infrastructure failure). Expected: `ok: 503 body=<generic body with no secret material>`. Restore `Cosmos:Endpoint` after the check.
- [X] T028 [US1] Run `specs/003-agent-turn/quickstart.md` **Check 9** (`/health`, `/health/ready`, `/health/cosmos`, `/version`, `/api/chat/start` still 200). Expected: `health=200 ready=200 cosmos=200 version=200 start=200`.

**Checkpoint**: US1 is fully functional locally. The owner may now deploy from this state (deploy is an OWNER step) and run the Phase 6 deployed verification tasks against the FQDN for the US1 slice. Function App shape parity across both chat routes is verified in Phase 6 after US2 is wired.

---

## Phase 4: User Story 2 - Single-turn /ask/v2 (Priority: P2)

**Goal**: `POST /api/chat/{chatId}/ask/v2` runs the same turn on the same stored session but returns only the newly produced assistant turn, hydrated (contracts/chat-ask-v2.md).

**Independent Test**: `quickstart.md` Check 3 passes locally. US2 shares the stored session with US1, so after US2 is wired a subsequent `/ask` call still replays every turn in order (US2 acceptance scenario 2).

### Implementation for User Story 2

- [X] T029 [US2] Done in T022 — US1's wiring task already registered the `/api/chat/{chatId}/ask/v2` route alongside `/ask`, so this task is a no-op bookkeeping entry. Marking it complete acknowledges the US2 route exists.

### Local verification for User Story 2

- [X] T030 [US2] With the API running from US1 verification (no restart needed — T022 already wired both routes), reuse the `$chatId` built up during US1 verification (Check 1 / Check 2 produced the stored session state Check 3 reads) and run `specs/003-agent-turn/quickstart.md` **Check 3** (`/ask/v2` returns `{ funnyFact, turn }` with `turn.role = "assistant"` and the hydrated `turn.movieList`; a follow-up `/ask` on the same `chatId` replays the pair, confirming US2's acceptance scenario 2). Expected: `ok: v2 turn text=<prefix>...`. If the chatId from US1 is no longer available, create a fresh one and run Check 1 before Check 3.

**Checkpoint**: Both chat routes are live. Interleaving `/ask` and `/ask/v2` on one session preserves order.

---

## Phase 5: User Story 3 - Stateless /ask (Priority: P3)

**Goal**: `POST /ask` continues to accept `{ "question": string }` and return `{ "answer": string }`, served by the same unified `AIAgent` registration (FR-015). No implementation task — `AskController` already calls `AIAgent.RunAsync(string)` without a response format, and T020 replaced its agent dependency with the unified one.

**Independent Test**: `quickstart.md` Check 8 passes locally.

### Local verification for User Story 3

- [X] T031 [US3] Run `specs/003-agent-turn/quickstart.md` **Check 8** (`POST /ask` returns `{ answer: <plain text> }`, not a `MovieListResponse` JSON blob). Expected: `ok: /ask answer=<prefix>...`.

**Checkpoint**: All three user stories work locally against the single unified agent and the single `TMDbClient` singleton.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Add the two mandatory dependency probes (FR-018, Principle IV) and run the deployed-environment verification after the OWNER deploys.

### Implementation (probes)

- [X] T032 In `MovieTracker.Api/Program.cs`, after the existing `/health/cosmos` block, add `app.MapGet("/health/tmdb", ...)` per `specs/003-agent-turn/contracts/health-probes.md`. Resolve `TMDbClient` from DI, call `await tmdbClient.GetConfigAsync()`, return `Results.Ok(new { status = "ok", baseUrl = "https://api.themoviedb.org/3" })` on success. On exception, log and return `Results.Json(new { status = "fail", reason = "unavailable" }, statusCode: 503)`. Use `.WithName("TmdbHealth")`.
- [X] T033 In `MovieTracker.Api/Program.cs`, immediately after T032, add `app.MapGet("/health/openai", ...)` per `specs/003-agent-turn/contracts/health-probes.md`. Resolve `IChatClient` and `IConfiguration` from DI; build `using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));`; call `await chatClient.GetResponseAsync([new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "ping")], new ChatOptions { MaxOutputTokens = 1 }, cts.Token);`; return `Results.Ok(new { status = "ok", endpoint = new Uri(config["AzureOpenAI:Endpoint"]!).Host, deployment = config["AzureOpenAI:DeploymentName"]! })`. On `OperationCanceledException` return 503 with `reason = "timeout"`, on any other exception return 503 with `reason = "unavailable"`. Use `.WithName("OpenAiHealth")`. T032 and T033 both edit `MovieTracker.Api/Program.cs`, so they run sequentially.
- [X] T034 Run `dotnet build MovieTracker.Api/MovieTracker.Api.csproj`. Fix any errors from T032–T033.

### Local verification (probes and shape parity)

- [X] T035 Stop any running API, then start it with `dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`. Run `specs/003-agent-turn/quickstart.md` **Check 7** (both probes 200, no secret material). Expected: `ok: tmdb=200 openai=200`.
- [X] T036 Run `specs/003-agent-turn/quickstart.md` **Check 10** (Function App shape parity for BOTH `Chat-Ask` and `Chat-Ask-V2`). The Function App has its own Cosmos database; its session ids are not valid against this API's Cosmos. Procedure: (1) call `GET` on the Function App's `Chat-Start` endpoint to obtain a Function-App-side chatId (`$faChatId`) and use that for the Function App POSTs; (2) call `GET /api/chat/start` on this API to obtain a separate `$apiChatId` and use that for this API's POSTs. Send the same `/ask` body to both `POST /api/chat/{apiChatId}/ask` and the Function App's `POST chat/{faChatId}/ask`; send the same `/ask/v2` body to both `POST /api/chat/{apiChatId}/ask/v2` and the Function App's `POST chat/{faChatId}/ask/v2`. Compare SHAPES only (top-level property names, types, nesting; the lowercase `role` discriminator). Byte equality is NOT required — live model, live TMDb, and different sessions make byte parity impossible. (This runs after US2 is wired per T022 so both envelopes exist on this API.)

### Deployed verification (OWNER deploys first)

- [X] T037 Wait for the OWNER to deploy a new revision of the `movie-tracker-api` Container App that includes the Phase 2–6 changes (reference: `specs/003-agent-turn/quickstart.md` "Deployed run" section for the FQDN convention). Do NOT run `az acr build`, `az containerapp update`, or any other deploy command — deploy is an OWNER step. Record the deployed revision's ingress FQDN as `<fqdn>` for T038.
- [X] T038 Against the deployed `<fqdn>`, re-run `specs/003-agent-turn/quickstart.md` "Deployed run" (Checks 1-4 and 7-10; Check 5 optional; Check 6 is local-only). Replace `http://localhost:5065` with `https://<fqdn>` in every curl command. All expected outputs match the local runs. If any check fails against the deployed revision, record the failing check and halt — do not deploy a fix via `az`; hand it back to the OWNER.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1. Blocks all user stories. Within Phase 2: T003 depends on T002 (both deletions must happen before the folder is gone); T005–T018 are independent file-level tasks; T019 depends on T005–T018 (needs the DTOs, kernel functions, and repository changes to reference); T020 depends on T017, T018, T019; T021 depends on T002–T020.
- **Phase 3 (US1)**: depends on Phase 2 complete. T023–T028 depend on T022 and on each other for ordering only insofar as they share the running API; Check 6 (T027) restarts the API after editing `appsettings.Development.json`, and Check 2 (T024) depends on Check 1 (T023) having produced a `$chatId`.
- **Phase 4 (US2)**: depends on Phase 2 complete AND on US1 verification having produced a stored `$chatId` with history (US2 Check 3 reads it; task text covers the fallback if the id is unavailable). No restart is required — T022 wired both routes together.
- **Phase 5 (US3)**: depends on Phase 2 complete (T020 unifies the agent registration that `/ask` resolves). No code changes.
- **Phase 6 (Polish)**: T032 and T033 depend on Phase 2 (`TMDbClient` and `IChatClient` singletons). T033 depends on T032 (same file). T034 depends on T032 and T033. T035 depends on T034 and on an API restart. T036 depends on T029 having wired `/ask/v2` (parity check covers both routes). T037–T038 depend on T022, T029, T032, T033 all being merged and the OWNER deploying.

### Within Each User Story

- Implementation (route wiring) before verification (quickstart checks).
- All verification tasks need the API running locally (`dotnet run`). Any task that edits `Program.cs` or Razor-style route table requires the API to be restarted before the next verification task runs — `dotnet run` does not hot-reload the route table.

### Parallel Opportunities

- Phase 2 Chat DTOs (T007–T016): all 10 can be created in parallel — each is its own new file. Compile dependency is caught by T021.
- Phase 2 ported kernel functions (T017, T018): parallel with each other and with T007–T016.
- Phase 2 session document and repository (T005, T006): parallel with each other and with the DTOs/kernel functions.
- Phase 6 probe routes (T032, T033) are NOT parallel — both edit the same file (`MovieTracker.Api/Program.cs`) and T033 inserts immediately after T032.

---

## Parallel Example: Phase 2 Foundational

```text
# Launch the 10 Chat DTO files together (all [P], all different new files):
Task: "Create MovieTracker.Api/Chat/Ask.cs"
Task: "Create MovieTracker.Api/Chat/MovieTrailerInfo.cs"
Task: "Create MovieTracker.Api/Chat/MovieViewModel.cs"
Task: "Create MovieTracker.Api/Chat/ChatMessage.cs"
Task: "Create MovieTracker.Api/Chat/UserChatMessage.cs"
Task: "Create MovieTracker.Api/Chat/AssistantChatMessage.cs"
Task: "Create MovieTracker.Api/Chat/ChatMessageResponse.cs"
Task: "Create MovieTracker.Api/Chat/ChatTurnResponse.cs"
Task: "Create MovieTracker.Api/Chat/MovieListItem.cs"
Task: "Create MovieTracker.Api/Chat/MovieListResponse.cs"

# At the same time, kick off the kernel function ports and the session/repo changes:
Task: "Port MovieTracker.Api/Prompts/TheMovieDBKernelFunctions.cs"
Task: "Port MovieTracker.Api/Prompts/DateTimeKernelFunctions.cs"
Task: "Add HydratedMovies to MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs"
Task: "Add GetChatSession/SaveChatSession to MovieTracker.Api/ChatSessions/ChatSessionRepository.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (T001) — package add.
2. Phase 2 (T002–T021) — foundational.
3. Phase 3 (T022–T028) — US1 wiring + local verification.
4. **Stop and validate**: US1 is a shippable MVP; frontend consumers of `/ask` work. Full Function App shape parity for both routes is deferred to T036 after US2.
5. Owner may deploy from here. Phase 4–6 can be added before or after that deploy.

### Incremental Delivery

1. Finish Phase 1 + Phase 2 → foundation.
2. Add US1 → test → MVP.
3. Add US2 → test.
4. Verify US3 (no new code) → test.
5. Add probes (T032–T035) and run the dual-route shape parity (T036) → test.
6. OWNER deploys → T037–T038 against FQDN.

### Owner-vs-worker boundary

- **Worker (this task list)**: everything up to and including local verification + the deployed-run instructions in T038.
- **Owner only**: `az acr build`, `az containerapp update`, Bicep edits, Key Vault edits, any Azure portal change, PR merge, release approval. The deployment step itself (between T037 and T038) is OWNER.

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete tasks.
- Every file-creation task names the exact file path. Every verification task names the exact `quickstart.md` check and the expected output.
- Verification runs locally first (Phases 3–5 and T036). Deployed verification (T037–T038) runs only after the OWNER deploys.
- Coding standards for every C# edit in this slice: explicit types (no `var`), braces on every `if`/`else`/`for`/`while` even single-line, no inline comments (XML `<summary>` on public types and public methods is allowed, as is the single pre-existing `#pragma warning disable MAAI001` block ported from the Function App).
- No test project, no Bicep changes, no new configuration keys, no new NuGet packages beyond T001.
