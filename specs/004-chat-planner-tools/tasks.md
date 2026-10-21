---
description: "Task list for slice 004 — ChatPlanner Tools (Ratings, Trailers, Wikipedia-Grounded Context And Funny Fact)"
---

# Tasks: ChatPlanner Tools (Ratings, Trailers, Wikipedia-Grounded Context And Funny Fact)

**Input**: Design documents from `/specs/004-chat-planner-tools/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Organization**: One phase per user story (US1 = P1 ratings, US2 = P1 trailers, US3 = P2 funny fact, US4 = P3 Wikipedia context), preceded by Setup and Foundational phases that are shared across all stories. The user-story phases are verification-only — the port is unified so all tool code lands in Foundational. Verification tasks map to specific `quickstart.md` checks.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: Which user story this task belongs to (e.g. US1, US2, US3, US4).
- **[OWNER]**: Task must be executed by the repository owner (not by an automated agent). Covers manual secret seeding, PR merge, and deployed-revision verification.
- All file paths are relative to the repository root (e.g. `MovieTracker.Api/Program.cs`). Reference files outside the repo use absolute paths (e.g. the Function App source under `C:/projects/dev/movie-tracker-backend/`).

## Path Conventions

Single ASP.NET Core project rooted at `MovieTracker.Api/`. Ported Function App source lives at `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/` and is the reference for every port task below. Coding rules for every C# file: explicit types instead of `var`, braces on every `if`/`else`/`for`/`while` even when single-line, no inline comments inside method bodies. Keep `[Description]` attributes and XML `<summary>` doc-comments verbatim from the backend — they are load-bearing documentation.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the single new NuGet package the ported code needs.

- [X] T001 Add `<PackageReference Include="OmdbApiNet" Version="1.3.0" />` to `MovieTracker.Api/MovieTracker.Api.csproj`. The package id has a lowercase 'o'; the C# namespace it ships is `OMDbApiNet` (uppercase M), which `OpenMovieDbAgent.cs` imports via `using OMDbApiNet;`. Match the Function App's `MovieTracker.Backend.csproj:44` verbatim. Run `dotnet restore MovieTracker.Api/MovieTracker.Api.csproj` to confirm the package resolves.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Every ported class, every DI change, the funny-fact flow in `ChatTurnEndpoints.cs`, and the Bicep env-var wiring. US1-US4 all depend on this phase being complete.

**⚠️ CRITICAL**: No user story verification can begin until this phase is complete and the OWNER has seeded the OMDb key (Phase 2.5, T020).

### Ported Agents/ records and classes (one type per file; CS-1)

- [X] T002 [P] Create `MovieTracker.Api/Agents/MovieRatingResult.cs` by porting the record declaration from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Agents/OpenMovieDbAgent.cs:30-49`. Namespace `MovieTracker.Api.Agents`. The `Trailer` field is `MovieTrailerInfo?` — reference the existing slice 003 `MovieTracker.Api.Chat.MovieTrailerInfo` (via a `using MovieTracker.Api.Chat;` directive); do NOT re-declare the trailer record. Keep every XML `<summary>` block from the backend verbatim.
- [X] T003 [P] Create `MovieTracker.Api/Agents/RatingComparisonResult.cs` by porting `OpenMovieDbAgent.cs:51-57`. Record positional. Namespace `MovieTracker.Api.Agents`.
- [X] T004 [P] Create `MovieTracker.Api/Agents/WikipediaResult.cs` by porting the `WikipediaResult` **class** declaration from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Agents/WikipediaSearchAgent.cs:284-290`. Public mutable class (`{ get; set; }` properties). The nullable Wikidata property is named `StructuredData` (type `WikidataInfo?`), NOT `WikidataInfo`. Namespace `MovieTracker.Api.Agents`.
- [X] T005 [P] Create `MovieTracker.Api/Agents/WikipediaContent.cs` by porting the `WikipediaContent` class from `WikipediaSearchAgent.cs:292-296`. Public mutable class; `Summary?` and `Thumbnail?` are nullable strings.
- [X] T006 [P] Create `MovieTracker.Api/Agents/WikidataInfo.cs` by porting the `WikidataInfo` class from `WikipediaSearchAgent.cs:298-302`. `StructuredFacts?` is `Dictionary<string, object>?`; `RelatedEntities?` is `List<string>?`.
- [X] T007 [P] Create `MovieTracker.Api/Agents/WikipediaSummary.cs` by porting the `WikipediaSummary` class from `WikipediaSearchAgent.cs:320-330`. **Keep the `[JsonPropertyName("title")]`, `[JsonPropertyName("extract")]`, and `[JsonPropertyName("thumbnail")]` attributes verbatim** — the research notes record them as the fix that made the whole grounded path work (data-model.md records this as load-bearing). Keep the XML `<summary>` block that explains why.
- [X] T008 [P] Create `MovieTracker.Api/Agents/WikipediaThumbnail.cs` by porting `WikipediaSearchAgent.cs:332-342`. Keep the three `[JsonPropertyName]` attributes.
- [X] T009 [P] Create `MovieTracker.Api/Agents/WikidataResponse.cs` by porting `WikipediaSearchAgent.cs:344-348`. Public mutable class with `Head?` and `Results?` nullable properties.
- [X] T010 [P] Create `MovieTracker.Api/Agents/WikidataHead.cs` by porting `WikipediaSearchAgent.cs:350-353`. `Vars?` is `List<string>?`.
- [X] T011 [P] Create `MovieTracker.Api/Agents/WikidataResults.cs` by porting `WikipediaSearchAgent.cs:355-358`. `Bindings?` is `List<Dictionary<string, WikidataValue>>?`.
- [X] T012 [P] Create `MovieTracker.Api/Agents/WikidataValue.cs` by porting `WikipediaSearchAgent.cs:360-365`. Three nullable string properties: `Type?`, `Value?`, `DataType?`.

### Ported Agents/ service classes

- [X] T013 [P] Create `MovieTracker.Api/Agents/OpenMovieDbAgent.cs` by porting the `OpenMovieDbAgent` class from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Agents/OpenMovieDbAgent.cs:59-171`. Adaptations only: (a) change `namespace MovieTracker.Backend.Agents` to `namespace MovieTracker.Api.Agents`; (b) replace every `var` with the explicit type; (c) add braces around every single-line `if`/`else`/`for`/`while` body; (d) remove every inline comment inside method bodies (keep XML `<summary>` blocks on types/methods verbatim); (e) do NOT re-declare `MovieRatingResult`/`RatingComparisonResult`/`MovieTrailerInfo` here — their file-per-type ports are T002/T003 and the existing `Chat/MovieTrailerInfo.cs`. Constructor reads `configuration["OpenMovieDb:Api-Key"]` and constructs `new AsyncOmdbClient(apiKey)` — same lookup string as the backend (FR-003). `CompareMovieRatings` MUST return the Function App's two distinguishable error branches: `"No valid movies found for comparison"` when `!movieRatings.Any()`, and `"No movies with valid IMDb ratings found"` when `highest == null` (spec FR-003).
- [X] T014 [P] Create `MovieTracker.Api/Agents/TrailerAgent.cs` by porting `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Agents/TrailerAgent.cs:23-83`. Same adaptations as T013. The primary constructor takes `TMDbClient tmdbClient` (the slice 003 singleton). Trailer selection is `Where(v => v.Type == "Trailer" && v.Site == "YouTube")` then `OrderByDescending(v => v.Official).ThenByDescending(v => v.Size)` — matching slice 003's hydration path exactly. The success payload MUST include `EmbedInSystemMessage = $"[TRAILER]https://www.youtube.com/watch?v={trailer.Key}[/TRAILER]"` verbatim. Empty/`"UNKNOWN"` title returns the "No movie title was supplied" error+hint payload; TMDb no-match returns the "No movie found matching '{title}'" error payload; no YouTube trailer returns the "No YouTube trailer is available for '{title}'" error payload. (FR-004.)
- [X] T015 [P] Create `MovieTracker.Api/Agents/WikipediaSearchAgent.cs` by porting the `WikipediaSearchAgent` class only from `WikipediaSearchAgent.cs:8-282`. Same adaptations as T013. The class body only — the DTO types from T004-T012 live in their own files. Keep every XML `<summary>` block (file-level and on methods) verbatim; they document the Wikidata-timeout history, the single-GET SearchWikipedia, and the confidence weights. `GetEnhancedInfo` MUST keep its outer `try { ... } catch (Exception ex) { logger.LogError(ex, ...); return null; }` shape (FR-017). `SearchWikipedia` MUST hit `https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(entityName)}` with no extra GETs. `QueryWikidata` MUST POST form-encoded `query` + `format=json` to `https://query.wikidata.org/sparql` with the entity-type-dispatched SPARQL (movie / actor / director; everything else falls back to movie). `CalculateConfidence` MUST score `+0.6` for `wikipedia?.Summary?.Length > 100` and `+0.4` for any Wikidata structured facts, capped at 1.0.

### Ported ChatPlanner (`Prompts/ChatPlanner.cs`)

- [X] T016 Create `MovieTracker.Api/Prompts/ChatPlanner.cs` by porting `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Prompts/ChatPlanner.cs:14-329`. Namespace change to `MovieTracker.Api.Prompts`; `using MovieTracker.Api.Agents;` directive. Same coding-rule adaptations as T013. Primary constructor dependencies: `IChatClient chatClient, WikipediaSearchAgent wikipediaAgent, OpenMovieDbAgent openMovieDbAgent, TrailerAgent trailerAgent`. Public API:
    - `IEnumerable<AITool> CreateTools()` returning EXACTLY six `AIFunctionFactory.Create` entries in this order: `HandleTrailerRequest`, `GetMovieRating`, `CompareMovieRatings`, `FilterMoviesByRating`, `GetChatContext`, `GetMovieContext` (FR-001). No other tools; `GenerateEnhancedFunnyFact` MUST NOT appear in this list.
    - Six tool methods with the Function App's `[Description]` attributes verbatim on the method and on each parameter (FR-001).
    - `public async Task<string?> GenerateEnhancedFunnyFact(string userQuery)` — public but NOT a tool. Called by `ChatTurnEndpoints.SafeGenerateFunnyFactAsync` (T018). Gates at `wikipediaInfo?.ConfidenceScore is not > 0.5` (FR-008). The grounded prompt interpolates ONLY `WikipediaContent.Summary` and instructs the model to answer `NONE` rather than inventing. The result passes through `NullIfNone`.
    - `GetMovieContext` MUST gate at `confidence > 0.5` and interpolate `WikipediaContent.Summary` AND `WikidataInfo.StructuredFacts` (FR-009).
    - `GetChatContext` MUST NOT gate on confidence — it returns the JSON object whenever `GetEnhancedInfo` returns a non-null `WikipediaResult`, and returns `null` only when `GetEnhancedInfo` itself returned null (FR-009; data-model entry for `WikipediaResult`).
    - Private helpers: `DetectEntity(string)`, `ParseDetectedEntity(string?)` with the `actor`/`actress`/`person`/`cast` → `"actor"` and `director`/`filmmaker` → `"director"` normalisation (FR-010), `NullIfNone(string?)`, nested `sealed record DetectedEntity(string Name, string Type)` with the `None` sentinel.
    - Blocked by T004-T006, T013, T014, T015 (the agents and the Wikipedia result shape it depends on).

### Program.cs DI edits

- [X] T017 Edit `MovieTracker.Api/Program.cs`. Four changes, all in the DI block before `builder.Build()`:
    - **(a)** Change the existing `builder.Services.AddSingleton<IChatClient>(...)` registration (currently at approx. line 47) to `builder.Services.AddScoped<IChatClient>(...)` (research Decision 6). Body of the factory is unchanged.
    - **(b)** Change the existing `builder.Services.AddSingleton<AIAgent>(...)` registration (currently at approx. line 74) to `builder.Services.AddScoped<AIAgent>(...)`. Inside the factory body, resolve `ChatPlanner chatPlanner = sp.GetRequiredService<ChatPlanner>();` and append `.. chatPlanner.CreateTools()` to the `tools` list literal, so the final list reads `[.. tmdbFunctions.CreateTools(), .. DateTimeKernelFunctions.CreateTools(), .. chatPlanner.CreateTools()]` (FR-002).
    - **(c)** Add three new scoped registrations immediately before the `AIAgent` registration: `builder.Services.AddScoped<OpenMovieDbAgent>();`, `builder.Services.AddScoped<TrailerAgent>();`, `builder.Services.AddScoped<ChatPlanner>();`.
    - **(d)** Add a typed-client registration: `builder.Services.AddHttpClient<WikipediaSearchAgent>(httpClient => { ... });`. Inside the lambda, add two `UserAgent` `ProductInfoHeaderValue` entries — the first `new ProductInfoHeaderValue(serviceName, serviceVersion)` where `serviceName` is this API's assembly name (`typeof(Program).Assembly.GetName().Name ?? "MovieTracker.Api"`) and `serviceVersion` is its assembly version string (`typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"`); the second `new ProductInfoHeaderValue("(+https://github.com/anoushm/movie-tracker-api)")` (research Decision 2). Then set `httpClient.Timeout = TimeSpan.FromSeconds(15);`. Do NOT add a separate `AddScoped<WikipediaSearchAgent>()` — the typed-client registration is the one that resolves (FR-005; backend `Program.cs:202-205` comment).
    - Add `using MovieTracker.Api.Agents;` and `using MovieTracker.Api.Prompts;` at the top of the file if either is missing.
    - Blocked by T013-T016 (all three agent service classes and ChatPlanner must exist for DI resolution to compile).

### ChatTurnEndpoints.cs: funny-fact flow and envelope wiring (FR-007, FR-011)

- [X] T018 Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`. Five changes:
    - **(a)** Add a private static helper `private static async Task<string?> SafeGenerateFunnyFactAsync(ChatPlanner chatPlanner, string input, ILogger logger)` that wraps `await chatPlanner.GenerateEnhancedFunnyFact(input)` in a `try { return ... } catch (Exception ex) { logger.LogWarning(ex, "Funny fact generation failed; answering without one"); return null; }`. Matches `Functions/Function.cs:686-697` exactly.
    - **(b)** In `RunTurnAsync` (the one conversational turn method shared by `/ask` and `/ask/v2`), add a `ChatPlanner chatPlanner` parameter and an `ILogger logger` parameter if not already present (threaded in from `Ask` / `AskV2` handler parameters — add `ChatPlanner chatPlanner` to both handler parameter lists; see T017's DI registrations).
    - **(c)** Inside `RunTurnAsync`, kick off `Task<string?> funnyFactTask = SafeGenerateFunnyFactAsync(chatPlanner, input, logger);` as the FIRST statement, BEFORE `chatMessages.Add(new AIChatMessage(ChatRole.User, input));` and BEFORE the context reducer / `agent.RunAsync` call (FR-007 clause 1). This matches `Functions/Function.cs:774`.
    - **(d)** AFTER `chatMessages.AddRange(result.Messages);` (where the model's reply is appended to stored history), add:
        ```
        string? funnyFact = await funnyFactTask;
        if (funnyFact != null)
        {
            chatSession.FunnyFact = funnyFact;
        }
        ```
        Matches `Functions/Function.cs:839-843` verbatim. `null` MUST NOT overwrite an existing non-null value (FR-007 clause 4).
    - **(e)** Both `Ask` and `AskV2` handlers currently construct their envelope with a literal `null` first argument. Change those constructions to pass `chatSession.FunnyFact` instead: `new ChatMessageResponse(chatSession.FunnyFact, responseMessages)` and `new ChatTurnResponse(chatSession.FunnyFact, turn)` (FR-011; backend `Function.cs:888, :932`).
    - Blocked by T016 (ChatPlanner must exist) and T017 (DI must resolve `ChatPlanner`).

### infrastructure/container-app.bicep edits (FR-012)

- [X] T019 Edit `infrastructure/container-app.bicep`. Two changes:
    - **(a)** Append an entry to the `secrets` array literal (currently containing `appinsights-connection-string`, `azure-openai-key`, `themoviedb-api-key`): `{ name: 'omdb-api-key', keyVaultUrl: '${keyVaultUri}/omdb-api-key', identity: managedIdentityId }`.
    - **(b)** Append an entry to the `envVars` array literal: `{ name: 'OpenMovieDb__Api-Key', secretRef: 'omdb-api-key' }`.
    - Do NOT touch `infrastructure/key-vault.bicep` — the owner seeds the secret value directly in Key Vault (T020, recorded Development-Workflow exception per spec FR-012).

**Checkpoint**: Phase 2 complete. All ported code and infrastructure wiring is in place. The build should succeed; the service will fail at first chat turn until T020 runs.

---

## Phase 2.5: Owner key setup (blocks all user-story verification)

**Purpose**: Seed the new OMDb key in Key Vault and locally. This is a recorded Development-Workflow exception (spec FR-012); it matches how the existing `azure-openai-key` and `themoviedb-api-key` secrets are seeded.

- [X] T020 [OWNER] Perform the OMDb key seeding (FR-012, FR-013, FR-014):
    1. Issue a new OMDb API key at `omdbapi.com`. **MUST NOT** reuse the key present in `movie-tracker-backend/appsettings.json` (that key is in git history and is treated as compromised per Principle V).
    2. Set the Key Vault secret so the deployed Container App can resolve it:
        ```
        az keyvault secret set --vault-name <keyVaultName> --name omdb-api-key --value "<new OMDb key>"
        ```
    3. Set the same key in local user-secrets so `dotnet run` can resolve it:
        ```
        dotnet user-secrets --project MovieTracker.Api/MovieTracker.Api.csproj set "OpenMovieDb:Api-Key" "<same new OMDb key>"
        ```
    4. **MUST NOT** paste the key value into any file in this repository. `appsettings.json` and `appsettings.Development.json` MUST stay OMDb-free.

**Checkpoint**: Local `dotnet run` can now resolve `OpenMovieDb:Api-Key`; the next deploy will surface it as `OpenMovieDb__Api-Key` from the Key Vault reference.

---

## Phase 3: User Story 1 — Ratings tools (Priority: P1) 🎯 MVP

**Goal**: A rating question in chat produces an IMDb-grounded answer via `GetMovieRating`, `CompareMovieRatings`, or `FilterMoviesByRating` (FR-001, FR-003).

**Independent Test**: Spec US1 Independent Test; executes `quickstart.md` Check 1 against a fresh `chatId`.

- [ ] T021 [US1] Start the service locally (`dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`) and run `quickstart.md` **Check 1** against `http://localhost:5065`. Verify all three sub-checks pass: single-rating names the IMDb value from omdbapi.com (`tt1375666`), identifies it as the IMDb rating; the comparison answer names the OMDb-highest of three films; the filter answer lists only the films at or above the threshold.

**Checkpoint**: US1 verifiable locally.

---

## Phase 4: User Story 2 — Trailer tool (Priority: P1)

**Goal**: A trailer question in chat produces a `[TRAILER]{youtube-url}[/TRAILER]` wrapper via `HandleTrailerRequest` on both `/ask` and `/ask/v2` (FR-001, FR-004).

**Independent Test**: Spec US2 Independent Test; executes `quickstart.md` Check 2 against a fresh `chatId`.

- [ ] T022 [US2] Run `quickstart.md` **Check 2** against the running local service. Verify (a) `/ask` returns the `[TRAILER]` wrapper for Dune, (b) a pronoun follow-up still returns the wrapper (model resolves "it"; the tool receives the title), (c) `/ask/v2` on a fresh chat for Oppenheimer also returns the wrapper.

**Checkpoint**: US2 verifiable locally.

---

## Phase 5: User Story 3 — Funny fact (Priority: P2)

**Goal**: A fresh chat with a well-known-movie input returns a non-null `funnyFact` in the envelope; a fresh chat with a generic input returns `null` (FR-007, FR-011).

**Independent Test**: Spec US3 Independent Test; executes `quickstart.md` Check 3 against two fresh `chatId`s.

- [ ] T023 [US3] Run `quickstart.md` **Check 3** against the running local service. Verify (a) the non-null branch returns a non-null funny fact on at least one fresh chat (re-run per SC-008 if the first attempt lands in the `NONE`/below-gate branch — model non-determinism is a legal outcome, not a defect), (b) the null branch (generic input "popular movies last year") returns `funnyFact: null`.

**Checkpoint**: US3 verifiable locally.

---

## Phase 6: User Story 4 — Wikipedia context tools (Priority: P3)

**Goal**: A chat question about a well-known entity returns a well-formed response; the Wikipedia context tools (`GetChatContext`, `GetMovieContext`) do not crash and no tool failure escalates to a 503 (FR-009, FR-017).

**Independent Test**: Spec US4 Independent Test; executes `quickstart.md` Check 4 against a fresh `chatId`.

- [ ] T024 [US4] Run `quickstart.md` **Check 4** against the running local service. Verify (a) `/ask` returns 200 and non-empty assistant text for "tell me about Christopher Nolan"; (b) no exception from the context-tool path escalates to a non-200 response.

**Checkpoint**: US4 verifiable locally. All stories verifiable locally.

---

## Phase 7: Deploy and deployed-revision verification

**Purpose**: Owner triggers the deploy and re-runs the US1-US4 checks against the deployed Container App to prove the Key Vault → container env var → `IConfiguration` wiring resolves end to end (SC-005).

- [X] T025 [OWNER] Pre-merge review gate. Before merging the PR, confirm:
    - The `Validate Bicep` and `What-If` workflow jobs on the PR are green (constitution Development Workflow). A What-If showing an unexpected delete or replace on any resource other than the Container App revision (which `activeRevisionsMode: 'Single'` naturally cycles) **blocks** the merge.
    - Code review covered the quickstart Static review items: SC-002 (one `AddScoped<AIAgent>` block whose tool list ends with `.. chatPlanner.CreateTools()`), SC-003 (container-app.bicep has the `omdb-api-key` secrets entry under the managed identity and the `OpenMovieDb__Api-Key` env var with `secretRef`; `key-vault.bicep` is unchanged; `appsettings*.json` carries no OMDb entry), SC-006 (env var is `secretRef`, not `value`), SC-007 (ported files are present at `Agents/` and `Prompts/ChatPlanner.cs`; `OmdbApiNet` 1.3.0 is the only new PackageReference).
- [X] T026 [OWNER] Merge the PR to trigger the deploy via the existing GitHub Actions workflow. **Do NOT** run `az acr build` or `az containerapp update` from the local workstation; deployment is pipeline-triggered. After the merge, **wait for the deploy pipeline to complete successfully** and confirm the Container App revision now serves the slice's image before proceeding to T027-T030. One cheap confirmation: `GET /version` on the deployed FQDN returns an `ImageTag` / `GitSha` matching the merged commit.
- [X] T027 [OWNER] [US1] Repeat `quickstart.md` **Check 1** against the deployed Container App FQDN. The IMDb rating the chat text names MUST match what `omdbapi.com` reports for `tt1375666` at the time of the check (SC-005). A direct `omdbapi.com` call from the owner's workstation using the newly issued key is the cross-check.
- [ ] T028 [OWNER] [US2] Repeat `quickstart.md` **Check 2** against the deployed FQDN. The `[TRAILER]` wrapper must appear with a working YouTube key.
- [ ] T029 [OWNER] [US3] Repeat `quickstart.md` **Check 3** against the deployed FQDN. Non-null fact on fresh chat with a qualifying input; `null` on a generic input. SC-008's re-run allowance applies.
- [X] T030 [OWNER] [US4] Repeat `quickstart.md` **Check 4** against the deployed FQDN.

**Checkpoint**: Slice 004 is deployed and verified end-to-end against both local and deployed environments.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1.
  - T002-T012 (record / class ports) are all `[P]` — different files, no dependencies.
  - T013 depends on T002, T003 (records it returns).
  - T014 is independent of T013 (TrailerAgent takes slice 003's `TMDbClient`, not OpenMovieDbAgent).
  - T015 depends on T004-T006 (`WikipediaResult`/`WikipediaContent`/`WikidataInfo` are its public return types) and transitively on T007-T012 (JSON DTOs it deserializes into).
  - T016 depends on T004-T006, T013, T014, T015.
  - T017 depends on T013, T014, T015, T016 (DI resolution).
  - T018 depends on T016, T017.
  - T019 is independent of the C# tasks and can run anytime after T001.
- **Phase 2.5 (OWNER setup)**: can start anytime (seeding the OMDb key is independent of the code tasks). It blocks every verification task (T021-T024 locally, T027-T030 deployed).
- **Phase 3-6 (US1-US4 local verification)**: depend on Phase 2 and T020. Can run in parallel if the owner runs multiple chat clients, otherwise sequentially.
- **Phase 7**: T025 (pre-merge gate) depends on all local verification being green and on Phase 2 complete. T026 (merge + wait for pipeline) depends on T025. T027-T030 depend on T026 completing successfully, including the pipeline finishing and the revision serving the merged image (checked via `GET /version` per T026).

### User-story dependencies

- US1, US2, US3, US4 are independently testable (each check runs against its own fresh `chatId`). All four share the same AIAgent and ChatPlanner but exercise disjoint tool subsets.

### Parallel opportunities

- T002-T012: eleven file-port tasks all `[P]`.
- T013-T015: three service-class tasks all `[P]` (none of them depend on each other).
- T027-T030: deployed-revision verification tasks are independent of each other once T026 completes.

---

## Parallel Example: Foundational record / class ports

```text
# Launch all eleven Agents/ record and class ports together:
Task: T002 - Create MovieTracker.Api/Agents/MovieRatingResult.cs
Task: T003 - Create MovieTracker.Api/Agents/RatingComparisonResult.cs
Task: T004 - Create MovieTracker.Api/Agents/WikipediaResult.cs
Task: T005 - Create MovieTracker.Api/Agents/WikipediaContent.cs
Task: T006 - Create MovieTracker.Api/Agents/WikidataInfo.cs
Task: T007 - Create MovieTracker.Api/Agents/WikipediaSummary.cs
Task: T008 - Create MovieTracker.Api/Agents/WikipediaThumbnail.cs
Task: T009 - Create MovieTracker.Api/Agents/WikidataResponse.cs
Task: T010 - Create MovieTracker.Api/Agents/WikidataHead.cs
Task: T011 - Create MovieTracker.Api/Agents/WikidataResults.cs
Task: T012 - Create MovieTracker.Api/Agents/WikidataValue.cs
```

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Complete Phase 1 (T001).
2. Complete Phase 2 (T002-T019). This is the whole code port; without the ChatPlanner tool set attached to the agent, the chat has no new behaviour.
3. Complete Phase 2.5 (T020) — owner seeds the OMDb key locally and in Key Vault.
4. Complete Phase 3 (T021). Validate US1 locally.
5. Stop here if that is the MVP cut.

### Incremental delivery (adds US2 → US3 → US4)

6. Complete Phase 4 (T022). US2 is verifiable locally.
7. Complete Phase 5 (T023). US3 is verifiable locally.
8. Complete Phase 6 (T024). US4 is verifiable locally.
9. Complete Phase 7 (T025 pre-merge gate → T026 merge + wait for pipeline → T027-T030 deployed re-verification against the Container App FQDN).

### Parallel team strategy

With multiple contributors:

1. One contributor finishes T001.
2. Up to eleven contributors claim T002-T012 in parallel (eleven file-port tasks). Three more claim T013-T015 after T002-T012 are done.
3. One contributor serialises T016 → T017 → T018 (all three touch files that depend on each other).
4. One contributor handles T019 in parallel with the C# work.
5. The owner handles T020 and all `[OWNER]` tasks.

---

## Notes

- `[P]` tasks = different files, no incomplete-task dependencies.
- `[Story]` label maps a verification task to the user story it covers.
- `[OWNER]` tasks require the repository owner to execute them (manual secret seeding, PR merge, deployed-revision verification). No automated agent may run them.
- The port is behaviour-preserving. Any discrepancy from the Function App source (confidence weights, SPARQL dispatch, parse normalisation, user-agent pattern, 15s timeout, concurrency pattern, tool descriptions) is a port defect, not an improvement opportunity.
- No test project. Verification is manual quickstart.
- No new health probes (OMDb, Wikipedia, Wikidata). Recorded Principle IV exception per spec FR-015.
- No telemetry — that is slice 005.
- No commit step; the owner commits.
