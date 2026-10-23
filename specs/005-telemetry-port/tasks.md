---
description: "Task list for slice 005 — Telemetry Port From Function App"
---

# Tasks: Telemetry Port From Function App

**Input**: Design documents from `/specs/005-telemetry-port/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Organization**: One phase per user story (US1 metrics, US2 traces, US3 logs, US4 sensitive-data toggle), preceded by Setup and Foundational phases that are shared across all stories. Each user-story phase mixes the specific recording-site edits for that story with a single verification task against `quickstart.md`.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: Which user story this task belongs to (e.g. US1, US2, US3, US4).
- **[OWNER]**: Task must be executed by the repository owner (not by an automated agent). Covers merge, deploy, and deployed-revision verification.
- All file paths are relative to the repository root (e.g. `MovieTracker.Api/Program.cs`). Reference files outside the repo use absolute paths (e.g. the Function App source under `C:/projects/dev/movie-tracker-backend/`).

## Path Conventions

Single ASP.NET Core project rooted at `MovieTracker.Api/`. Ported Function App source lives at `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/` and is the reference for every port task below. Coding rules for every C# file touched by this slice: explicit types (project `var` waiver is active but new code uses explicit types where it reads cleaner), braces on every `if`/`else`/`for`/`while` even when single-line, no inline comments inside method bodies, no new XML `<summary>` doc-comments on ported code (design rationale lives in `research.md` and `data-model.md`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the two new NuGet packages the ported telemetry wiring needs.

- [X] T001 Add two `<PackageReference>` entries to `MovieTracker.Api/MovieTracker.Api.csproj`: `Azure.Monitor.OpenTelemetry.AspNetCore` version `1.6.0` and `Azure.Monitor.OpenTelemetry.Exporter` version `1.8.3`. Match the Function App's `MovieTracker.Backend.csproj` versions verbatim. Do NOT add an explicit `OpenTelemetry.Extensions.Hosting` reference — it is pulled transitively by the distro package (research Decision 1). Run `dotnet restore MovieTracker.Api/MovieTracker.Api.csproj` to confirm both packages resolve.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared OpenTelemetry plumbing — the three new `Telemetry/*.cs` files, the `appsettings.json` logging block, the Program.cs OTel registrations, and the `ChatSessionRepository` constructor extension. US1-US4 all depend on this phase being complete.

**âš ï¸ CRITICAL**: No user story recording-site task can begin until this phase is complete.

### Ported Telemetry types (one public type per file)

- [X] T002 [P] Create `MovieTracker.Api/Telemetry/Telemetry.cs` by porting `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:37-127`. Namespace `MovieTracker.Api.Telemetry`. Internal static class. Public surface MUST exactly match data-model.md's "Telemetry (static)" table: `const string SourceName = "movie-tracker-api"` (NOT `"movie-tracker-backend"` — spec FR-010), `const string ServiceVersion = "1.0.0"`, a `private static readonly Meter Meter = new(SourceName, ServiceVersion)`, and five instruments with the Function App's exact names/units/dimensions (contracts/instruments.md): `Histogram<double> ToolDuration` (`gen_ai.client.tool.duration`, `"s"`), `Counter<long> ToolInvocations` (`gen_ai.client.tool.invocations`, `"{invocation}"`), `Histogram<int> ToolCallsPerTurn` (`chat.turn.tool_calls`, `"{call}"`), `Histogram<double> ContextRetainedRatio` (`chat.context.retained_ratio`, `"1"`), `Counter<double> CostUsd` (`gen_ai.usage.cost_usd`, `"USD"`). Also port the two static helpers: `RecordTool(string toolName, TimeSpan elapsed, bool succeeded)` emits both tool instruments with `gen_ai.tool.name` and `outcome` dimensions; `RecordCost(string model, UsageDetails usage, ModelPricing pricing)` computes `cost = freshInput/1M * Input + cachedInput/1M * (Cached ?? Input) + output/1M * Output` with `cachedInput = usage.CachedInputTokenCount ?? 0` and `freshInput = Math.Max(0, (usage.InputTokenCount ?? 0) - cachedInput)`, then records `CostUsd` with `gen_ai.request.model` dimension only when `cost > 0`. Adaptations: namespace change; no inline comments inside methods; braces on every `if`. Do NOT copy forward the backend's file-level `<summary>` block (research/data-model carry the rationale).

- [X] T003 [P] Create `MovieTracker.Api/Telemetry/InstrumentedAIFunction.cs` by porting the `InstrumentedAIFunction` class from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:143-223`. Namespace `MovieTracker.Api.Telemetry`. Internal sealed class deriving from `Microsoft.Extensions.AI.DelegatingAIFunction`. Primary constructor `(AIFunction inner, ILogger? logger = null, bool logArguments = false)`. `InvokeCoreAsync` MUST: start a `Stopwatch`, `await base.InvokeCoreAsync(arguments, cancellationToken)`, record `Telemetry.RecordTool(Name, elapsed, succeeded: true)` and call `LogCall(arguments, elapsed, error: null)` on success, or record `Telemetry.RecordTool(Name, elapsed, succeeded: false)` and call `LogCall(arguments, elapsed, ex)` and rethrow unchanged on exception. A `LogCall(AIFunctionArguments arguments, TimeSpan elapsed, Exception? error)` method MUST early-return when `logger is null` or `logger.IsEnabled(LogLevel.Information)` is false, render arguments via a `Render` helper when `logArguments == true` else emit the literal `"(arguments not logged)"`, and write either `LogInformation("tool {ToolName}({ToolArguments}) ok in {ElapsedMs}ms", Name, rendered, (int)elapsed.TotalMilliseconds)` on success or `LogWarning("tool {ToolName}({ToolArguments}) FAILED in {ElapsedMs}ms: {Error}", Name, rendered, (int)elapsed.TotalMilliseconds, error.Message)` on failure. The `Render(AIFunctionArguments arguments)` helper MUST: return `string.Empty` when `arguments is null || arguments.Count == 0`; join `key=value` pairs skipping null values; truncate values beyond `MaxLoggedArgumentChars = 120` with a trailing `"..."`. `LogCall` and `Render` MUST be private instance methods (per the project's `.NET` conventions — private helpers inside a non-static class are instance members unless a specific exception justifies `static`; neither helper here touches instance state in a way that requires `static`). The Function App declares `Render` as `private static` and `LogCall` as `private void`; the port drops the `static` modifier for parity with the project's conventions. Adaptations otherwise: namespace change; braces on every `if`; no inline comments; no XML `<summary>` doc-comments copied forward.

- [X] T004 [P] Create `MovieTracker.Api/Telemetry/ModelPricing.cs` by porting the `ModelPricing` record from `C:/projects/dev/movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:235-238`. Namespace `MovieTracker.Api.Telemetry`. Internal sealed record positional: `(double InputPerMillion, double OutputPerMillion, double? CachedInputPerMillion)` — three fields in that order. No XML `<summary>` doc-comments; the field semantics are documented in data-model.md.

### appsettings.json edit

- [X] T005 [P] Edit `MovieTracker.Api/appsettings.json`. Add an `OpenTelemetry` subsection under the existing `Logging` block with three boolean entries (contracts/config-keys.md): `"IncludeFormattedMessage": true`, `"IncludeScopes": true`, `"ParseStateValues": true`. The resulting `Logging` section reads: `{ "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }, "OpenTelemetry": { "IncludeFormattedMessage": true, "IncludeScopes": true, "ParseStateValues": true } }`. Preserve slice 003's `LogLevel` entries exactly. Do NOT edit `appsettings.Development.json`.

### Program.cs OTel wiring (FR-002, FR-003, FR-006, FR-007 Tracer registration, FR-009, FR-010, FR-012)

- [X] T006 Edit `MovieTracker.Api/Program.cs`. All edits land before `builder.Build()` and compose with slice 004's existing registrations:
    - **(a)** Read `Telemetry:Enable-Sensitive-Data` once at startup into a local `bool enableSensitiveTelemetry = builder.Configuration.GetValue<bool?>("Telemetry:Enable-Sensitive-Data") ?? true;` — default `true`, matching the Function App's `Program.cs:125-126` behaviour (FR-006).
    - **(b)** Read the three pricing keys once at startup via `double.TryParse` on `builder.Configuration["AzureOpenAI:Price-Input-Per-1M"]`, `"AzureOpenAI:Price-Output-Per-1M"`, `"AzureOpenAI:Price-Cached-Input-Per-1M"`. Construct `ModelPricing? modelPricing = null` and, only when BOTH input and output parse successfully, replace it with `new ModelPricing(inputRate, outputRate, cachedRate)` where `cachedRate` is the third `TryParse`'s out value if that parse succeeded, otherwise `null` — matching the Function App's `Function.cs:212-218` ternary (FR-009). Register the result: `builder.Services.AddSingleton<ModelPricing?>(_ => modelPricing);`.
    - **(c)** Register the `Tracer` singleton: `builder.Services.AddSingleton(TracerProvider.Default.GetTracer(Telemetry.SourceName, Telemetry.ServiceVersion));` (FR-007, research Decision 3).
    - **(d)** Attach the Azure Monitor log exporter to the host's logging builder: `builder.Logging.AddOpenTelemetry(options => { options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Telemetry.SourceName, Telemetry.ServiceVersion)); options.AddAzureMonitorLogExporter(exporter => { exporter.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]; exporter.EnableTraceBasedLogsSampler = false; }); });`. `EnableTraceBasedLogsSampler = false` is load-bearing (FR-003, research Decision 2). The `IncludeFormattedMessage`, `IncludeScopes`, and `ParseStateValues` flags are bound from `Logging:OpenTelemetry:*` in `appsettings.json` (T005) — the authoritative source (research Decision 8); do NOT also set them in the lambda.
    - **(e)** Wire the trace and metric exporters: `string appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]; ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService(Telemetry.SourceName, Telemetry.ServiceVersion); builder.Services.AddOpenTelemetry() .WithTracing(tracing => { tracing.AddSource(Telemetry.SourceName).AddSource("Microsoft.Extensions.AI*").AddSource("Microsoft.Agents.AI*").AddSource("Azure.AI.OpenAI*").AddSource("OpenAI*").SetResourceBuilder(resource).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddAzureMonitorTraceExporter(options => options.ConnectionString = appInsightsConnectionString); }) .WithMetrics(metrics => { metrics.AddMeter(Telemetry.SourceName).SetResourceBuilder(resource).AddHttpClientInstrumentation().AddAzureMonitorMetricExporter(options => options.ConnectionString = appInsightsConnectionString); });` NO `.WithLogging(...)` call on this builder (FR-002; research Decision 2 names the "silent swallow" bug).
    - **(f)** Inside the existing `builder.Services.AddScoped<IChatClient>(sp => ...)` factory (slice 004 moved it to Scoped), insert `.UseOpenTelemetry(loggerFactory, Telemetry.SourceName, options => options.EnableSensitiveData = enableSensitiveTelemetry)` after `.UseFunctionInvocation(...)` and before `.Build(sp)` (FR-006, research Decision 6). Do NOT apply `.UseOpenTelemetry` on the `AIAgent` registration (FR-006; research Decision 6 names the token-usage double-counting bug).
    - **(g)** Inside the existing `builder.Services.AddScoped<AIAgent>(sp => ...)` factory, wrap each tool with `InstrumentedAIFunction`. Before building the agent, resolve `ILogger toolLogger = loggerFactory.CreateLogger("MovieTracker.Api.ToolCalls");` and transform the `tools` list: `tools = [.. tools.Select(tool => tool is AIFunction function ? new InstrumentedAIFunction(function, toolLogger, enableSensitiveTelemetry) : tool)];` (FR-005, FR-006). The transformation runs AFTER the three `CreateTools()` spreads so every TMDb, DateTime, and ChatPlanner tool is wrapped.
    - Add `using` directives at the top of the file: `using Azure.Monitor.OpenTelemetry.Exporter;`, `using Microsoft.Extensions.AI;` (if not already present via slice 004), `using Microsoft.Extensions.Logging;` (ditto), `using MovieTracker.Api.Telemetry;`, `using OpenTelemetry;`, `using OpenTelemetry.Logs;`, `using OpenTelemetry.Metrics;`, `using OpenTelemetry.Resources;`, `using OpenTelemetry.Trace;`.
    - Blocked by T002, T003, T004 (the three `Telemetry/*.cs` types must exist for compilation).

### ChatSessionRepository constructor extension (research Decision 9)

- [X] T007 Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`. Extend the primary constructor from `(CosmosClient cosmosClient, string database, string container)` to `(CosmosClient cosmosClient, string database, string container, Tracer tracer, ILogger<ChatSessionRepository> logger)`. Capture `tracer` and `logger` into private readonly fields alongside the existing `chatSessionsContainer` field. Also edit `MovieTracker.Api/Program.cs`'s existing `builder.Services.AddSingleton(sp => new ChatSessionRepository(...))` factory lambda (around line 119) to resolve the two new parameters: `sp.GetRequiredService<Tracer>()` and `sp.GetRequiredService<ILogger<ChatSessionRepository>>()`. Add `using OpenTelemetry.Trace;` to `ChatSessionRepository.cs`. The three method bodies stay unchanged in this task; US2 (T016-T018) and US3 (T022-T024) will add the span and catch-arm wrappers. Blocked by T006 (Tracer singleton must be registered).

**Checkpoint**: Phase 2 complete. All ported Telemetry types exist, OTel is wired, logs/traces/metrics exporters are registered, and ChatSessionRepository can receive a Tracer and ILogger. The build should succeed. No instruments are recorded yet; no spans fire; the chat still runs end to end exactly as slice 004 left it, but with `cloud_RoleName == "movie-tracker-api"` ASP.NET Core request spans and HttpClient dependency spans now landing in Application Insights.

---

## Phase 3: User Story 1 — Five custom instruments recording (Priority: P1) ðŸŽ¯ MVP

**Goal**: The five custom instruments (`gen_ai.client.tool.duration`, `gen_ai.client.tool.invocations`, `chat.turn.tool_calls`, `chat.context.retained_ratio`, `gen_ai.usage.cost_usd`) emit from the API's running revision in Application Insights, matching the Function App's shape (FR-004; contracts/instruments.md).

**Independent Test**: Spec US1 Independent Test; executes `quickstart.md` SC-001 against a chat turn.

**Note**: The two tool instruments (`gen_ai.client.tool.duration` and `gen_ai.client.tool.invocations`) are already wired by Phase 2's `InstrumentedAIFunction` wrapping (T006 bullet g). This phase adds the three remaining recording sites in `ChatTurnEndpoints.cs`.

- [X] T008 [US1] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`. In `RunTurnAsync`, AFTER the `AgentResponse result = await agent.RunAsync(...)` call and the existing `chatMessages.AddRange(result.Messages);` line, add: `Telemetry.ToolCallsPerTurn.Record(result.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count());` Matches `Functions/Function.cs:831-832` behaviour verbatim (FR-004 instrument #3). Add `using MovieTracker.Api.Telemetry;` to the file if not present.

- [X] T009 [US1] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`. In `RunTurnAsync`, BEFORE the `AgentResponse result = await agent.RunAsync(...)` call and AFTER the existing `List<AIChatMessage> modelMessages = reduced.ToList();` line, add a guarded record: `if (chatMessages.Count > 0) { Telemetry.ContextRetainedRatio.Record((double)modelMessages.Count / chatMessages.Count); }` The `chatMessages.Count > 0` guard matches `Functions/Function.cs:815-818` and avoids a divide-by-zero on an empty history (FR-004 instrument #4).

- [X] T010 [US1] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` to add a `LogUsage` helper and wire pricing/deployment through the two public handlers. Three sub-edits:
    - **(a)** Add a `ModelPricing? modelPricing` parameter and a `string deployedModel` parameter to the private static `RunTurnAsync` signature (currently `RunTurnAsync(AIAgent agent, ChatPlanner chatPlanner, MoviceTrackerChatSession chatSession, string input, ILogger logger)`); keep the trailing position so the new signature reads `RunTurnAsync(AIAgent agent, ChatPlanner chatPlanner, MoviceTrackerChatSession chatSession, string input, ILogger logger, ModelPricing? modelPricing, string deployedModel)`.
    - **(b)** Add a private static helper `LogUsage(AgentResponse result, ILogger logger, ModelPricing? modelPricing, string deployedModel)` that early-returns when `result.Usage is not { } usage`, otherwise calls `Telemetry.RecordCost(deployedModel, usage, pricing)` iff `modelPricing is { } pricing` (US2 T015 will add the Activity.Current tags to this same helper; US3 T021 will add the `LogInformation("Chat-Ask usage: ...")` call to this same helper — all three concerns share one helper, mirroring Function App `Function.cs:711-737`). Call `LogUsage(result, logger, modelPricing, deployedModel);` from `RunTurnAsync` AFTER the existing `chatMessages.AddRange(result.Messages);` and the T008 `ToolCallsPerTurn.Record` line (order within the post-model-call block does not matter).
    - **(c)** Both `Ask` and `AskV2` handler signatures must gain two new minimal-API DI parameters so the service provider resolves them: `ModelPricing? modelPricing` and `IConfiguration configuration`. In each handler body, resolve `string deployedModel = configuration["AzureOpenAI:DeploymentName"]!;` (slice 003 validated this key at startup; the `!` is correct here per the slice-003 pattern) and pass both to `RunTurnAsync(..., modelPricing, deployedModel)`.
    - Add `using Microsoft.Extensions.Configuration;` to the file (minimal APIs include it transitively but naming the directive is safe). Add `using MovieTracker.Api.Telemetry;` if not already present. Blocked by T006 (ModelPricing? Singleton registered).

- [X] T011 [US1] Start the service locally (`dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj`) with `APPLICATIONINSIGHTS_CONNECTION_STRING` set in `dotnet user-secrets` (quickstart.md Setup section) and run `quickstart.md` **SC-001** against the dev endpoint per the quickstart's exact instructions and conditional expectations. The quickstart governs acceptance — do NOT invent stricter gates.

**Checkpoint**: US1 verifiable locally — five instruments emit (or don't, for cost) as the spec requires.

---

## Phase 4: User Story 2 — Hand-rolled spans and token-usage tags (Priority: P1)

**Goal**: Application Insights Transaction search shows the three endpoint spans (`movie-tracker-api.chat-start`, `.chat-ask`, `.chat-ask-v2`), the three repository spans (`.chat-session-repository.new-chat-session`, `.get-chat-session`, `.save-chat-session`), and the four `gen_ai.usage.*` tags on the enclosing endpoint span after a chat turn (FR-007, contracts/spans.md).

**Independent Test**: Spec US2 Independent Test; executes `quickstart.md` SC-002 against a chat turn.

- [X] T012 [US2] Edit `MovieTracker.Api/Program.cs`. Add a `Tracer tracer` parameter to the existing `app.MapGet("/api/chat/start", async (ChatSessionRepository repo, ILogger<Program> logger) => { ... })` handler signature so it becomes `async (ChatSessionRepository repo, Tracer tracer, ILogger<Program> logger)`. Inside the handler, open a span around the full existing body: `using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-start");` as the first line, before the existing `try { ... }` block. The `using` scope ends at the method boundary, so the span closes before the handler returns regardless of success/exception. Add `using OpenTelemetry.Trace;` to `Program.cs` if not already present. Matches `Functions/Function.cs:387` behaviour (span name prefix changes from `movie-tracker-func` to `movie-tracker-api` per FR-007).

- [X] T013 [US2] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`. Add a `Tracer tracer` parameter to the `Ask` handler signature (minimal-API DI resolves it from the T006 singleton registration). As the FIRST line of the `try { ... }` body, add `using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-ask");` Matches `Functions/Function.cs:854`. Add `using OpenTelemetry.Trace;` to the file.

- [X] T014 [US2] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`. Add a `Tracer tracer` parameter to the `AskV2` handler signature. As the FIRST line of the `try { ... }` body, add `using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-ask-v2");` Matches `Functions/Function.cs:906`.

- [X] T015 [US2] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`'s `LogUsage` helper (added in T010). After the early-return guard, add four `Activity.Current?.SetTag(...)` calls, each guarded so a null `UsageDetails` field is NOT tagged — matching Function App `Function.cs:728-732` with its `Activity.Current?.SetTag(...)` pattern:
    ```
    Activity? activity = Activity.Current;
    if (activity != null)
    {
        if (usage.InputTokenCount is { } inputTokens)
        {
            activity.SetTag("gen_ai.usage.input_tokens", inputTokens);
        }
        if (usage.OutputTokenCount is { } outputTokens)
        {
            activity.SetTag("gen_ai.usage.output_tokens", outputTokens);
        }
        if (usage.CachedInputTokenCount is { } cachedInputTokens)
        {
            activity.SetTag("gen_ai.usage.cache_read.input_tokens", cachedInputTokens);
        }
        if (usage.ReasoningTokenCount is { } reasoningTokens)
        {
            activity.SetTag("gen_ai.usage.reasoning.output_tokens", reasoningTokens);
        }
    }
    ```
    Add `using System.Diagnostics;` if not already present (needed for the raw `Activity` type). The ambient `Activity.Current` is the `.chat-ask` / `.chat-ask-v2` span opened in T013 / T014. The guard behaviour mirrors FR-007 bullet 3 exactly: null fields are not tagged; a null `Activity.Current` is a no-op.

- [X] T016 [US2] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `NewChatSession` method. Open a span around the method body: `using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.new-chat-session");` as the FIRST statement of the method body, before the existing `string id = GenId();` line. The `using` scope ends at the method boundary. Matches `ChatSessionRepository.cs:57` behaviour (prefix change per FR-007). (US3 T022 will add the try/catch wrapper to the SAME method; the span `using` and the try/catch are compatible — the `using` is lexically outer.)

- [X] T017 [US2] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `GetChatSession` method. Open `using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.get-chat-session");` as the FIRST statement of the method body. Matches `ChatSessionRepository.cs:82`.

- [X] T018 [US2] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `SaveChatSession` method. Open `using TelemetrySpan activity = this.tracer.StartActiveSpan("movie-tracker-api.chat-session-repository.save-chat-session");` as the FIRST statement of the method body. Matches `ChatSessionRepository.cs:100`.

- [X] T019 [US2] With the service running locally and `APPLICATIONINSIGHTS_CONNECTION_STRING` set, run one chat turn that invokes at least one tool (`POST /api/chat/{chatId}/ask`) and run `quickstart.md` **SC-002** against the Application Insights Transaction search. Verify (a) one `movie-tracker-api.chat-ask` span at the top (not `movie-tracker-func.*`), (b) at least one framework `chat` span under it, (c) at least one `execute_tool <name>` span for a tool that actually ran, (d) a `movie-tracker-api.chat-session-repository.save-chat-session` span, (e) the enclosing endpoint span carries the `gen_ai.usage.*` tags corresponding to the non-null fields of the returned `UsageDetails` (null fields untagged).

**Checkpoint**: US2 verifiable locally — the six hand-rolled spans and four token-usage tags land in App Insights.

---

## Phase 5: User Story 3 — Application logs reach App Insights (Priority: P2)

**Goal**: `LogInformation` and `LogCritical` lines from the API's chat path land in Application Insights `traces` / `exceptions` with `IncludeFormattedMessage` + `IncludeScopes` + `EnableTraceBasedLogsSampler = false` applied so log retention does not inherit the trace-sampling decision (FR-003, FR-008; the "silent swallow" fix at Function App `Program.cs:99-111`).

**Independent Test**: Spec US3 Independent Test; executes `quickstart.md` SC-003 against a `LogCritical`-triggering request and a successful chat turn.

**Note**: The log pipeline itself (exporter attached to the host's logging builder, `EnableTraceBasedLogsSampler = false`, `IncludeFormattedMessage`/`IncludeScopes`) is already wired by T006 bullet d. This phase adds the five log sites.

- [X] T020 [US3] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`'s `RunTurnAsync`. BEFORE the existing `AgentResponse result = await agent.RunAsync(...)` call and AFTER the T009 `ContextRetainedRatio.Record` block, add: `logger.LogInformation("Chat-Ask context: sending {IncludedMessages} of {TotalMessages} stored messages to the model", modelMessages.Count, chatMessages.Count);` Message template MUST be verbatim from `Functions/Function.cs:808-810` (FR-008 bullet 2; the `"Chat-Ask"` operation-class prefix is kept as-is).

- [X] T021 [US3] Edit `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`'s `LogUsage` helper (added in T010, extended with Activity tags in T015). AFTER the early-return guard and BEFORE the Activity.Current tag block, add: `logger.LogInformation("Chat-Ask usage: input={InputTokens} cachedInput={CachedInputTokens} output={OutputTokens} reasoning={ReasoningTokens} total={TotalTokens}", usage.InputTokenCount, usage.CachedInputTokenCount, usage.OutputTokenCount, usage.ReasoningTokenCount, usage.TotalTokenCount);` Message template MUST be verbatim from `Functions/Function.cs:718-724` (FR-008 bullet 1).

- [X] T022 [US3] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `NewChatSession` method. Wrap the method body INSIDE the T016 span's `using` scope in a try/catch: the method body currently reads span-open then `string id = GenId(); ... await ... CreateItemAsync(session, new PartitionKey(id)); return session;`. Wrap everything from the first statement AFTER the `using TelemetrySpan activity = ...;` line through `return session;` in `try { ... } catch (Exception ex) { this.logger.LogCritical("{@ex}", ex); throw; }` The `{@ex}` structured-exception capturing operator MUST be preserved so the structured exception object reaches App Insights (FR-008 bullet 4). `throw;` re-throws the original exception unchanged. Matches `ChatSessionRepository.cs:75` behaviour (catch arm).

- [X] T023 [US3] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `GetChatSession` method. Wrap the body (inside the T017 span's `using` scope) in `try { ItemResponse<...> response = ...; return response.Resource; } catch (Exception ex) { this.logger.LogCritical("{@ex}", ex); throw; }` IMPORTANT: this catch is UNFILTERED (`catch (Exception ex)` with no `when` clause) — it catches ALL exceptions INCLUDING `CosmosException` with `StatusCode == NotFound`. The endpoint handler in `ChatTurnEndpoints.Ask` / `AskV2` already filters `CosmosException` with `StatusCode == NotFound` into its 404 branch; the repository's `LogCritical` fires BEFORE that filtering reaches the endpoint. Do NOT add exception-type filtering here (FR-008 bullet 4 explicitly matches the Function App's unfiltered catch at `ChatSessionRepository.cs:88-91`).

- [X] T024 [US3] Edit `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`'s `SaveChatSession` method. Wrap the body (inside the T018 span's `using` scope) in `try { ItemResponse<...> response = ...; return response.Resource; } catch (Exception ex) { this.logger.LogCritical("{@ex}", ex); throw; }` Matches `ChatSessionRepository.cs:108` (catch arm).

- [X] T025 [US3] With the service running locally, run `quickstart.md` **SC-003** per the quickstart's instructions: (a) `POST /api/chat/{unknownChatId}/ask` to a `chatId` the repository will not find — verify the Kusto query `traces | where cloud_RoleName == "movie-tracker-api" and severityLevel >= 3` shows the `LogCritical` line (Critical = `severityLevel == 4`); (b) run a successful chat turn and verify the `traces` table shows the `Chat-Ask usage: ...` and `Chat-Ask context: sending ... of ... stored messages` lines with the five / two structured properties under `customDimensions`; (c) code-review Program.cs and confirm the single `AzureMonitorLogExporter` registration site sets `exporter.EnableTraceBasedLogsSampler = false` (US3 acceptance scenario 2).

**Checkpoint**: US3 verifiable locally — LogCritical fires on repository exceptions, LogInformation fires on every successful turn, both land in `traces` with fully formatted messages.

---

## Phase 6: User Story 4 — Sensitive-data toggle (Priority: P3)

**Goal**: Setting `Telemetry:Enable-Sensitive-Data` to `false` disables prompt/completion recording on the chat client's `chat` spans AND switches `InstrumentedAIFunction`'s log line to `"(arguments not logged)"`; the default (`true` / unset) keeps both carrying user-supplied names/titles (FR-006).

**Independent Test**: Spec US4 Independent Test; executes `quickstart.md`'s US4 walkthrough.

**Note**: The toggle wiring is entirely in Phase 2 — T006 bullet (a) reads the key once; T006 bullet (f) feeds `EnableSensitiveData` into the chat client's `.UseOpenTelemetry`; T006 bullet (g) feeds `logArguments` into `InstrumentedAIFunction`'s constructor. There is no new code for this phase; US4 is a verification-only phase.

- [X] T026 [US4] Run the `quickstart.md` **US4 walkthrough** per the quickstart's exact instructions: with the toggle unset (default), the `InstrumentedAIFunction` log line carries the rendered arguments AND the `execute_tool` span's `gen_ai.tool.call.arguments` attribute (or equivalent framework-named attribute) carries the arguments; with `Telemetry__Enable-Sensitive-Data=false`, the log line reads `(arguments not logged)` AND the span attribute is absent. The quickstart's wording governs acceptance.

**Checkpoint**: US4 verifiable locally.

---

## Phase 7: Deploy and deployed-revision verification

**Purpose**: Owner triggers the deploy and re-runs the SC checks against the deployed Container App to prove the connection-string env var â†’ three exporters â†’ Application Insights pipeline resolves end to end on the real revision.

- [ ] T027 [OWNER] Pre-merge code-review gate. Before merging the PR, confirm:
    - `MovieTracker.Api/MovieTracker.Api.csproj` carries exactly the two required telemetry packages (`Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 and `Azure.Monitor.OpenTelemetry.Exporter` 1.8.3); no `Microsoft.ApplicationInsights.*` reference appears (SC-004).
    - `MovieTracker.Api/Program.cs` has exactly one `services.AddOpenTelemetry()` call wiring `.WithTracing(...)` and `.WithMetrics(...)` (no `.WithLogging(...)` on this builder), exactly one `builder.Logging.AddOpenTelemetry(...)` with `exporter.EnableTraceBasedLogsSampler = false`, exactly one `.UseOpenTelemetry(...)` on the chat client (zero on the agent), exactly one `services.AddSingleton(TracerProvider.Default.GetTracer(...))` (SC-005).
    - `git diff --name-only main...HEAD -- infrastructure/` prints empty output — no Bicep file is touched (SC-006).
- [ ] T028 [OWNER] Merge the PR to trigger the deploy via the existing GitHub Actions workflow. Do NOT run `az acr build` or `az containerapp update` from the local workstation; deployment is pipeline-triggered. After the merge, wait for the deploy pipeline to complete successfully before proceeding to T029.
- [ ] T029 [OWNER] Run the `quickstart.md` SC-001, SC-002, SC-003, SC-007 checks and the US4 walkthrough against the deployed Container App FQDN per the quickstart's exact instructions. The quickstart's wording governs acceptance — do NOT invent stricter gates.

**Checkpoint**: Slice 005 is deployed and verified end-to-end against the deployed revision per quickstart. Local verification during impl is optional feedback, not a gating pass.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1.
    - T002, T003, T004, T005 are all `[P]` — different files, no cross-dependencies.
    - T006 depends on T002, T003, T004 (the three Telemetry types must exist for compilation).
    - T007 depends on T006 (Tracer singleton must be registered before the ChatSessionRepository factory can resolve it).
- **Phase 3 (US1)**: depends on Phase 2. T008 and T009 edit different parts of `RunTurnAsync` and are logically independent but share a file (no `[P]`). T010 depends on T006 (ModelPricing? Singleton). T011 (local verification) depends on T008, T009, T010.
- **Phase 4 (US2)**: depends on Phase 2, T010 (for the LogUsage helper that T015 extends).
    - T012-T014 edit three different handlers but all three handlers are callable only after the Tracer is registered (T006). T012 edits Program.cs; T013 and T014 edit ChatTurnEndpoints.cs — same file but different method bodies so they can be done in sequence without conflict.
    - T015 depends on T010 (LogUsage helper exists).
    - T016, T017, T018 all edit `ChatSessionRepository.cs` — same file, sequential within one PR.
    - T019 (local verification) depends on T012-T018 completing.
- **Phase 5 (US3)**: depends on Phase 2, T010.
    - T020 and T021 edit `ChatTurnEndpoints.cs` (two different sites); T021 depends on T010.
    - T022, T023, T024 edit `ChatSessionRepository.cs` method bodies (adding try/catch inside each span's `using` scope). Logically depend on T016, T017, T018 respectively so the span `using` is already in place.
    - T025 (local verification) depends on T020-T024 completing.
- **Phase 6 (US4)**: depends on Phase 2 (the toggle is entirely a Phase-2 concern). T026 is verification only.
- **Phase 7**: T027 (pre-merge gate) depends on Phases 1-6 being code-complete (static review only — the local verification tasks T011/T019/T025/T026 are recommended for developer feedback during impl but are NOT a hard pre-merge gate; the deployed verification in T029 is the authoritative pass). T028 (merge + wait for pipeline) depends on T027. T029 depends on T028 completing successfully.

### User-story dependencies

- US1, US2, US3, US4 are independently testable (each verification runs against its own chat turn or against the running revision's App Insights state). US1 metrics, US2 traces, and US3 logs flow out of the same chat turns but are inspected in different Application Insights tables and transaction views. US4 is a toggle verified by comparing two runs with the toggle in different states; its implementation is zero new code (Phase 2 handles the wiring).

### Parallel opportunities

- T002, T003, T004, T005: four Phase-2 file edits, all `[P]`.
- Within each repository method's span + try/catch pair (T016 â†” T022, T017 â†” T023, T018 â†” T024): the pair MUST be applied together because the try/catch nests inside the span's `using` block; different pairs could be done in parallel if the implementer edits each method body in isolation, but all three live in one `ChatSessionRepository.cs` file so sequential is simpler.
- T029: one deployed-revision verification task — walks through SC-001, SC-002, SC-003, SC-007, and the US4 toggle in sequence per the quickstart.

---

## Parallel Example: Foundational Telemetry types

```text
# Launch all four [P] Phase-2 edits together:
Task: T002 - Create MovieTracker.Api/Telemetry/Telemetry.cs
Task: T003 - Create MovieTracker.Api/Telemetry/InstrumentedAIFunction.cs
Task: T004 - Create MovieTracker.Api/Telemetry/ModelPricing.cs
Task: T005 - Edit MovieTracker.Api/appsettings.json
```

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Complete Phase 1 (T001).
2. Complete Phase 2 (T002-T007). This gets the whole telemetry pipeline wired and ChatSessionRepository ready for span injection. No instruments record yet; no spans fire; the chat still runs end to end.
3. Complete Phase 3 (T008-T011). Validate US1 locally.
4. Stop here if that is the MVP cut.

### Incremental delivery (adds US2 â†’ US3 â†’ US4)

5. Complete Phase 4 (T012-T019). US2 traces verifiable locally.
6. Complete Phase 5 (T020-T025). US3 logs verifiable locally.
7. Complete Phase 6 (T026). US4 toggle verifiable locally.
8. Complete Phase 7 (T027 pre-merge gate â†’ T028 merge + wait for pipeline â†’ T029 deployed re-verification).

### Parallel team strategy

With multiple contributors:

1. One contributor finishes T001.
2. Up to four contributors claim T002, T003, T004, T005 in parallel.
3. One contributor serialises T006 â†’ T007 (both touch Program.cs).
4. Three contributors can claim US1 (T008-T010), US2 (T012-T018), and US3 (T020-T024) in parallel once Phase 2 is done — they touch different sites but overlap in `ChatTurnEndpoints.cs`'s `LogUsage` helper (T010 adds it, T015 and T021 extend it); best to serialise the three LogUsage-touching tasks within one contributor's branch.
5. The owner handles T027-T029.

---

## Notes

- `[P]` tasks = different files, no incomplete-task dependencies.
- `[Story]` label maps a task to the user story it covers.
- `[OWNER]` tasks require the repository owner to execute them (merge, deployed-revision verification). No automated agent may run them.
- The port is behaviour-preserving. Any discrepancy from the Function App source (instrument names, dimensions, log templates, span names, cost formula, sensitive-data gate behaviour) is a port defect, not an improvement opportunity.
- No test project. Verification is manual quickstart.
- No new metrics beyond the five in FR-004. No new dashboards, alerts, workbooks, action groups, or diagnostic settings.
- No Bicep changes (user directive; connection string env var already surfaced at `container-app.bicep:52-54`).
- No pricing values committed (user directive; Function App commits none).
- No commit step inside Phases 1-6; the owner commits as they see fit. Phase 7 explicitly handles the merge and deploy.
