# Quickstart: Telemetry Port From Function App

**Feature**: 005-telemetry-port
**Spec**: [spec.md](./spec.md) — Success Criteria SC-001..SC-007
**Plan**: [plan.md](./plan.md)

Manual verification mirroring slices 001-004. No test project runs; the gate
is "the five instruments, six spans, and the ported logs are observable in
Application Insights for the API, with the same shape they already have for
the Function App."

## Prerequisites

- The slice is implemented (packages added, OTel wired, five instruments
  defined, hand-rolled spans in place, `appsettings.json` has the
  `Logging:OpenTelemetry` block).
- The container image has been built and deployed to the Container App
  revision via the existing GitHub Actions workflow.
- Access to the Application Insights workspace linked in
  `infrastructure/main.bicep` (`appInsights` component — the one slice 001
  provisions).

## Setup (local verification)

```powershell
# Required for the OTel exporter to send to Application Insights.
# Same connection string the deployed revision uses — pull from the
# Container App's env var or the AI component properties.
dotnet user-secrets set "APPLICATIONINSIGHTS_CONNECTION_STRING" "<the component's connection string>" --project MovieTracker.Api

# Optional — defaults to true (prompts/arguments ARE logged). Set to
# false to verify the "arguments not logged" branch.
# dotnet user-secrets set "Telemetry:Enable-Sensitive-Data" "false" --project MovieTracker.Api
```

## Setup (deployed revision)

No action for the connection string — the Container App revision already has
`APPLICATIONINSIGHTS_CONNECTION_STRING` set via Bicep (research Decision 7).
No pricing values are committed and this slice does NOT introduce any — the
Function App commits none and the port carries that stance forward (research
Decision 5, spec FR-009). `gen_ai.usage.cost_usd` therefore stays unemitted
under the committed configuration; verification focuses on the
unconfigured-emits-nothing branch (SC-001).

## Verification steps (per Success Criterion)

### SC-001 — five custom instruments present

1. Create a chat session: `GET /api/chat/start` → note the `chatId`.
2. Run one chat turn that invokes a tool: `POST /api/chat/{chatId}/ask`
   with `{ "Input": "what's the IMDb rating of Inception?" }` (exercises
   `GetMovieRating`, which hits OMDb).
3. In Application Insights Logs, run:
   ```kusto
   customMetrics
   | where cloud_RoleName == "movie-tracker-api"
   | summarize count() by name
   ```
   **Expected**: `gen_ai.client.tool.duration`, `gen_ai.client.tool.invocations`,
   `chat.turn.tool_calls`, and `chat.context.retained_ratio` (the last one
   iff the turn's stored history was non-empty) all appear.
4. Under the committed configuration (no `AzureOpenAI:Price-*` values),
   confirm `gen_ai.usage.cost_usd` does NOT appear — this is the
   "unconfigured, nothing emitted" branch required by spec FR-009.
5. For completeness, confirm `gen_ai.client.tool.duration` carries the
   `gen_ai.tool.name` and `outcome` dimensions:
   ```kusto
   customMetrics
   | where cloud_RoleName == "movie-tracker-api"
     and name == "gen_ai.client.tool.duration"
   | extend tool = tostring(customDimensions["gen_ai.tool.name"]),
            outcome = tostring(customDimensions["outcome"])
   | summarize count() by tool, outcome
   ```

### SC-002 — hand-rolled spans and token-usage tags present

1. On the same chat turn as SC-001, open Application Insights **Transaction
   search** and locate the request for `POST /api/chat/{chatId}/ask`.
2. **Expected trace tree**: one `movie-tracker-api.chat-ask` span at the
   top (not `movie-tracker-func.*`), under it one or more framework `chat`
   spans, at least one `execute_tool <name>` span for the tool that ran,
   and a `movie-tracker-api.chat-session-repository.save-chat-session`
   span.
3. Click the `.chat-ask` span and inspect **Custom properties / Properties**.
   The four `gen_ai.usage.*` tags corresponding to the non-null fields on
   the model's `UsageDetails` should be present (`input_tokens`,
   `output_tokens`, `cache_read.input_tokens`, `reasoning.output_tokens`).
   A null field is correctly untagged (matches
   `Activity.Current?.SetTag(...)` behaviour).

### SC-003 — ported application logs reach App Insights

1. Trigger a `LogCritical` from the ported repository catch arm by calling
   `POST /api/chat/{unknownChatId}/ask` with a `chatId` the repository
   will not find. The ported `GetChatSession` catches ALL exceptions
   (including the NotFound), emits `LogCritical("{@ex}", ex)`, and
   rethrows; the endpoint handler then catches `CosmosException` with
   `StatusCode == NotFound` and returns 404 to the caller. The repository's
   `LogCritical` fires regardless (that is the whole point of catching at
   the repository level — the Function App's pattern, see spec FR-008
   bullet 4). No bogus configuration or redeploy is needed.

2. In Application Insights Logs, run:
   ```kusto
   traces
   | where cloud_RoleName == "movie-tracker-api"
     and severityLevel >= 3
   | project timestamp, message, operation_Id
   | order by timestamp desc
   ```
   **Expected**: the triggered `LogCritical` line appears (Application
   Insights represents it as `severityLevel == 4`; `severityLevel >= 3`
   captures both Error and Critical) with its message **fully formatted**
   (no `{Placeholder}` tokens — the `{@ex}` structured-exception capturing
   operator serialises the exception object into the message /
   `customDimensions`). Also, from a successful turn:
   ```kusto
   traces
   | where cloud_RoleName == "movie-tracker-api"
     and message startswith "Chat-Ask usage:"
   | project timestamp, message, customDimensions
   ```
   confirms the token-usage structured log appears with
   `InputTokens`/`CachedInputTokens`/`OutputTokens`/`ReasoningTokens`/
   `TotalTokens` under `customDimensions`.

3. Open `Program.cs` and confirm the single `AddAzureMonitorLogExporter(...)`
   call sets `exporter.EnableTraceBasedLogsSampler = false`. This is a code
   review, not a runtime check — US3 acceptance scenario 2 requires
   evidence that the knob is set at the registration site; FR-003 names it
   load-bearing.

### SC-004 — package references audit

In the project root:
```powershell
Select-String -Path MovieTracker.Api/MovieTracker.Api.csproj -Pattern "ApplicationInsights|OpenTelemetry|Azure\.Monitor"
```
**Expected**: `Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` and
`Azure.Monitor.OpenTelemetry.Exporter 1.8.3` appear. `OpenTelemetry.
Extensions.Hosting` MAY appear only if the implementer determined it was
not pulled transitively (unlikely per research Decision 1). No
`Microsoft.ApplicationInsights.*` reference appears.

### SC-005 — `Program.cs` wiring audit

Open `MovieTracker.Api/Program.cs`. Confirm:

- Exactly one `services.AddOpenTelemetry()` call with `.WithTracing(...)` and
  `.WithMetrics(...)` (NO `.WithLogging(...)`).
- Exactly one `builder.Logging.AddOpenTelemetry(...)` call that attaches
  `AddAzureMonitorLogExporter(exporter => { ... exporter.
  EnableTraceBasedLogsSampler = false; ...})`.
- Exactly one `.UseOpenTelemetry(...)` on the chat client (inside the
  `services.AddScoped<IChatClient>(sp => ...)` factory).
- Zero `.UseOpenTelemetry` calls on the `AIAgent`.
- Exactly one `services.AddSingleton(TracerProvider.Default.GetTracer(...))`
  registration.

### SC-006 — zero Bicep edits

```powershell
git diff --name-only main...HEAD -- infrastructure/
```
**Expected**: empty output. No file under `infrastructure/` is touched by
this slice.

### SC-007 — four App Insights tables populated

After a successful chat turn against the deployed revision, run:
```kusto
let api = "movie-tracker-api";
let since = ago(30m);
union
  (requests       | where cloud_RoleName == api and timestamp > since | summarize count() | extend table_="requests"),
  (dependencies   | where cloud_RoleName == api and timestamp > since | summarize count() | extend table_="dependencies"),
  (customMetrics  | where cloud_RoleName == api and timestamp > since | summarize count() | extend table_="customMetrics"),
  (traces         | where cloud_RoleName == api and timestamp > since | summarize count() | extend table_="traces")
```
**Expected**: a count ≥ 1 in each row (`requests` from
`AddAspNetCoreInstrumentation`, `dependencies` from
`AddHttpClientInstrumentation`, `customMetrics` from the five custom
instruments, `traces` from `AzureMonitorLogExporter`).

## US-level acceptance (manual walkthroughs)

### US1 — Operator sees five instruments

Covered by SC-001 above.

### US2 — Trace a chat turn end-to-end

Covered by SC-002 above. Additional check for the Cosmos repository span:
```kusto
dependencies
| where cloud_RoleName == "movie-tracker-api"
  and name startswith "movie-tracker-api.chat-session-repository."
| summarize count() by name
```
(The spans may appear in `dependencies` or `traces` depending on how the
Azure Monitor exporter classifies `StartActiveSpan` outputs; both are
acceptable.)

### US3 — Application logs land with trace-based sampling disabled

Covered by SC-003. Explicit code-review step for `EnableTraceBasedLogsSampler
= false` is the US3 acceptance scenario 2 requirement.

### US4 — Sensitive-data toggle

1. Set `Telemetry__Enable-Sensitive-Data=false` on the revision (or
   via `dotnet user-secrets set "Telemetry:Enable-Sensitive-Data" "false"`
   locally).
2. Run a chat turn with a tool call.
3. In Application Insights Logs, inspect the tool-call log line:
   ```kusto
   traces
   | where cloud_RoleName == "movie-tracker-api"
     and message startswith "tool "
   | project timestamp, message
   ```
   **Expected (false)**: `tool GetMovieRating((arguments not logged)) ok in
   ...ms`. **Expected (true/unset)**: `tool GetMovieRating(imdbId=tt1375666)
   ok in ...ms`.
4. Inspect an `execute_tool` span in Transaction search for the same turn.
   **Expected (false)**: no `gen_ai.tool.call.arguments` attribute (or an
   equivalent framework-named attribute carrying the arguments is absent).
   **Expected (true/unset)**: the attribute carries the arguments.

## Rollback

The slice is additive. To roll back:

1. Revert the commits that touched `MovieTracker.Api.csproj`,
   `Program.cs`, `ChatTurnEndpoints.cs`, `ChatSessionRepository.cs`,
   `appsettings.json`, and the `Telemetry/` folder.
2. Redeploy. The Container App revision pre-slice-005 is a known-good state;
   the `APPLICATIONINSIGHTS_CONNECTION_STRING` env var stays set (no Bicep
   change rolled back), and the exporters simply emit nothing because the
   application no longer calls them.

## Done when

- All seven SCs pass against the deployed revision.
- The owner records the verification in the slice's PR description.
- The constitution's Codex cross-review loop on `plan.md` and `tasks.md` has
  approved.
