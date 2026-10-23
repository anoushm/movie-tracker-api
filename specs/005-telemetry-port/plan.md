# Implementation Plan: Telemetry Port From Function App

**Branch**: `005-telemetry-port` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

## Summary

Port the Function App's telemetry wiring into `MovieTracker.Api` as-is: three
Azure Monitor OpenTelemetry exporters (traces, metrics, logs), five custom
instruments, `InstrumentedAIFunction`, hand-rolled spans on the three chat
endpoints and the three `ChatSessionRepository` methods, four structured log
sites, and the `Telemetry:Enable-Sensitive-Data` gate. The port keeps the
Function App's explicit-exporter shape (`AddOpenTelemetry().WithTracing(...)` +
`.WithMetrics(...)` + separate `builder.Logging.AddOpenTelemetry(...)`), the
same `EnableTraceBasedLogsSampler = false` fix, the same cached-input cost
split, and the same `.UseOpenTelemetry` on the chat client only (not on the
agent).

Two NuGet packages (`Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0`,
`Azure.Monitor.OpenTelemetry.Exporter 1.8.3`); `OpenTelemetry.Extensions.Hosting`
is pulled transitively and does not need an explicit reference (Phase 0
Decision 1). One new config block under `Logging:OpenTelemetry` in
`appsettings.json`. No Bicep changes — the Container App already surfaces
`APPLICATIONINSIGHTS_CONNECTION_STRING` as an env var via a `secretRef` to
the `appinsights-connection-string` Container App secret, whose value is
passed in as the `appInsightsConnectionString` module parameter from
`main.bicep` (`infrastructure/container-app.bicep:52-54`, Phase 0 Decision
7). The secret currently uses a direct `value:`, not a `keyVaultUrl:`
reference; routing it via Key Vault is inherited debt from slice 001 and is
out of scope here.

Pricing is a deliberate non-port. The Function App's committed source carries
NO `AzureOpenAi:Price-*` values — only the config reader at `Function.cs:213-218`
and a CLAUDE.md note stating the keys have "deliberately **no defaults**"
because a hardcoded rate that drifts produces a confident, wrong cost
dashboard. The port honours that: `AzureOpenAI:Price-Input-Per-1M`,
`Price-Output-Per-1M`, and `Price-Cached-Input-Per-1M` are **read** but not
seeded in `appsettings.json`. `gen_ai.usage.cost_usd` emits nothing until the
owner sets the three env vars on the Container App revision out of band,
exactly as the Function App behaves today (spec FR-009 / FR-010, Phase 0
Decision 5).

Six source files land in the API: `Telemetry/Telemetry.cs`,
`Telemetry/InstrumentedAIFunction.cs`, `Telemetry/ModelPricing.cs` — the
three types the Function App combines in one file, split one-per-file per the
project's coding-standard convention slice 004 already follows. The three
chat-endpoint handlers in `Program.cs` and `Chat/ChatTurnEndpoints.cs` and
the three `ChatSessions/ChatSessionRepository.cs` methods grow hand-rolled
spans, token-usage tags, three structured `LogInformation` lines in
`RunTurnAsync`, and three `LogCritical` lines in the repository's catch
arms. `Program.cs` gains the OTel registration block and the sensitive-data
gate; the IChatClient factory gains `.UseOpenTelemetry(...)`; the AIAgent
factory wraps each `AIFunction` in `InstrumentedAIFunction`.

The source form adapts to the project's coding standards: explicit types
(`var` is waived per project convention), braces on every `if`/`else`/`for`/
`while`, no inline comments on new code, one public type per file. The
Function App's design rationale that currently lives in its `Telemetry.cs`
file-level and record-level `<summary>` blocks (cardinality caps, why the
Meter sourceName matches `UseOpenTelemetry`, why `EnableTraceBasedLogsSampler
= false`) is captured in this plan and in `research.md`/`data-model.md`,
not copied as code comments. Manual quickstart against local and deployed
revisions is the only verification, per owner directive — same stance as
slices 003 and 004.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core (unchanged from slice 003).

**Primary Dependencies**:
- `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (**NEW**) — supplies the
  distro's ASP.NET Core + HttpClient instrumentations and transitively pulls
  `OpenTelemetry` 1.9+, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.
  Instrumentation.AspNetCore`, and `OpenTelemetry.Instrumentation.Http`.
  The port does NOT invoke the distro's one-stop `services.AddAzureMonitor()`
  — it uses the explicit `.AddAzureMonitor{Trace,Metric,Log}Exporter`
  shape the Function App uses (spec FR-002).
- `Azure.Monitor.OpenTelemetry.Exporter` 1.8.3 (**NEW**) — supplies the three
  individual exporters (`AddAzureMonitorTraceExporter`,
  `AddAzureMonitorMetricExporter`, `AddAzureMonitorLogExporter`) and the
  `EnableTraceBasedLogsSampler` property. Matches the Function App's
  `MovieTracker.Backend.csproj` version pin.
- Slice 004's existing packages carry everything else: `Microsoft.Extensions.AI`
  10.7.0 (supplies `DelegatingAIFunction`, `AIFunctionArguments`, `UsageDetails`),
  `Microsoft.Agents.AI` 1.17.0, `Microsoft.Azure.Cosmos` 3.62.0, etc. No slice
  004 package is bumped.

**Storage**: No change. The Cosmos session document is read and written the
same way slice 004 does; this slice decorates the read/write paths with
spans and log calls but adds no field and no index.

**Testing**: Manual quickstart against local and deployed revisions, mirroring
slices 003 and 004. No test project (owner directive restated in spec
"Assumptions" and the out-of-scope list). The quickstart exercises each of
US1-US4 against a chat turn and queries Application Insights for the five
instruments, the six hand-rolled span names, and the structured log lines.

**Target Platform**: Linux container on Azure Container Apps, same revision
pipeline as slice 004.

**Project Type**: Single ASP.NET Core web service (`MovieTracker.Api`).

**Performance Goals**: None added. The Azure Monitor OTel exporters batch
telemetry on their own schedule and do not block the request path. The three
hand-rolled spans are `StartActiveSpan` scopes (zero-allocation when sampled
out). `InstrumentedAIFunction` adds a single `Stopwatch` per tool call plus
two metric records — negligible against the model-call cost. The port makes
no claim about Application Insights ingestion latency.

**Constraints**:
- **No Bicep changes**. `infrastructure/container-app.bicep:52-54` already
  surfaces `APPLICATIONINSIGHTS_CONNECTION_STRING` as an env var via a
  `secretRef` to the `appinsights-connection-string` Container App secret,
  whose value is passed in as the `appInsightsConnectionString` module
  parameter from `main.bicep` (ultimately the Application Insights
  component's `.properties.ConnectionString`). The secret itself uses a
  direct `value:`, not a `keyVaultUrl:` reference; this slice does not
  change that.
  Phase 0 Decision 7 confirms the Bicep file is authoritative per Principle
  II; a live `az containerapp show` from this session's logged-in
  subscription returned no matching resource (the owner's revision lives in
  a different subscription this session isn't authenticated to), but the
  Bicep source already proves the env var is set.
- **No pricing values committed**. The Function App commits no
  `AzureOpenAi:Price-*` entries; the port commits no `AzureOpenAI:Price-*`
  entries either. Operators who want the cost histogram set the three keys
  as deploy-time env vars on the revision; without them, `CostUsd` emits
  nothing (spec FR-009 edge case, Phase 0 Decision 5).
- **No new routes, no new health probes, no new tools, no new frontend
  change.** All five health probes (`/health`, `/health/ready`,
  `/health/cosmos`, `/health/tmdb`, `/health/openai`) stay unchanged. The
  three chat routes (`/api/chat/start`, `/api/chat/{chatId}/ask`,
  `/api/chat/{chatId}/ask/v2`) acquire hand-rolled spans around their
  existing bodies; the `/version` route and the health probes are NOT
  wrapped in hand-rolled spans (FR-007 names exactly six hand-rolled spans
  overall: three endpoint + three repository).
- **No `.UseOpenTelemetry` on the AIAgent.** The Function App rejects this
  with a comment at `Program.cs:248-255` ("stacking the two records
  gen_ai.client.token.usage twice per model call"); the port keeps that
  constraint (spec FR-006).
- **No `.WithLogging(...)` on the OTel service collection builder.** The
  Function App rejects this at `Program.cs:369-373` ("no MovieTracker.*
  category has ever appeared in App Insights" with the exporter attached
  there). The port wires logs via `builder.Logging.AddOpenTelemetry(...)`
  on the host's logging builder, as the Function App does at `Program.cs:89`
  (spec FR-003).
- **No tests.** No test project added by this slice.

**Scale/Scope**:
- 3 new source files under `MovieTracker.Api/Telemetry/`
  (`Telemetry.cs`, `InstrumentedAIFunction.cs`, `ModelPricing.cs`), one
  public type each per CS-1.
- `MovieTracker.Api.csproj`: adds 2 PackageReferences.
- `MovieTracker.Api/Program.cs`: EDIT — adds OTel wiring block (~30 lines),
  reads `Telemetry:Enable-Sensitive-Data` and the three pricing keys at
  startup, adds `services.AddSingleton(TracerProvider.Default.GetTracer(...))`
  and `services.AddSingleton<ModelPricing?>(...)`, injects
  `.UseOpenTelemetry` into the chat client factory, wraps the agent's tool
  list through `InstrumentedAIFunction` in the AIAgent factory, and adds a
  hand-rolled span around the inline `/api/chat/start` handler.
- `MovieTracker.Api/Chat/ChatTurnEndpoints.cs`: EDIT — `Ask` and `AskV2` each
  open a hand-rolled span at method entry; `RunTurnAsync` acquires a
  `Tracer`, `ModelPricing?`, deployment name, and `IConfiguration`-sourced
  `deployedModel`; emits the two `LogInformation` lines (`Chat-Ask usage:
  ...` and `Chat-Ask context: sending ...`); sets the four
  `gen_ai.usage.*` tags on `Activity.Current`; records `ToolCallsPerTurn`,
  `ContextRetainedRatio`, and `CostUsd`. The hydration-trim `LogInformation`
  already exists at `ChatTurnEndpoints.cs:314-316` — unchanged.
- `MovieTracker.Api/ChatSessions/ChatSessionRepository.cs`: EDIT — constructor
  gains `Tracer` and `ILogger<ChatSessionRepository>` parameters; each of
  the three methods opens a hand-rolled span and wraps its body in a
  try/catch that emits `LogCritical("{@ex}", ex)` and rethrows (preserving
  the current behaviour where the endpoint's catch handler surfaces the
  503).
- `MovieTracker.Api/appsettings.json`: EDIT — add `Logging:OpenTelemetry`
  subsection with the three boolean flags.
- Expected diff: ~250 lines of C# (most of it in `Telemetry/*.cs` which is
  ported source with coding-standard adjustments) plus small endpoint and
  repository edits. No infra edits, no test code.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Evaluation |
|---|---|
| I. Managed Identity Over Secrets (NON-NEGOTIABLE) | PASS by preservation, with inherited debt. The App Insights connection string flows into the Container App as a plain secret `value` sourced from the `appInsightsConnectionString` module parameter in `infrastructure/container-app.bicep:24-28` (not through a `keyVaultUrl` reference — the two other secrets in the same block, `azure-openai-key` and `themoviedb-api-key`, use `keyVaultUrl`/managed-identity references, but the AI connection string does not). This is pre-existing state inherited from slice 001; routing the AI connection string through Key Vault via managed identity is a desirable follow-up that is explicitly out of scope here (user directive: "No bicep changes"). The slice adds no new identity surface and does not worsen this; the application code never reads key material, only the env var the SDK auto-discovers. |
| II. Infrastructure Is Bicep, Not Clicks | PASS by preservation. This slice makes **zero** Bicep edits. All telemetry resources (Log Analytics workspace, Application Insights component, Container App Environment linkage, the connection-string env var) are already declared in `infrastructure/main.bicep` and `infrastructure/container-app.bicep`. A live `az containerapp show` from this session's current subscription returned no matching resource — the owner's deployed revision lives in a different subscription — so the Bicep file is the authoritative confirmation per Principle II. |
| III. Incremental Migration With Declared Boundaries | PASS. Spec names what is imported (five instruments, hand-rolled spans at the six specific sites, three exporters, `EnableTraceBasedLogsSampler=false`, `Telemetry:Enable-Sensitive-Data` gate, the ChatPlanner-adjacent token-usage log line and context ratio record) and what is not (new metrics, dashboards, alerts, workbooks, diagnostic settings, snapshot/profiler, OTEL_* env vars, Serilog, tests). This plan does not exceed those boundaries. |
| IV. Dependencies Prove Themselves | PASS. No new external dependency is added — all three Azure Monitor exporters write to the Application Insights component slice 001 already provisions. The exporters' "connection string missing" state is Azure Monitor's responsibility (spec FR-014); the port MUST NOT add a bespoke `/health/appinsights` probe (that would exceed the declared boundary). The five existing health probes stay unchanged. |
| V. No Secret Material In Source | PASS. No OTEL_* env vars are introduced, no connection string is read from `appsettings*.json`, and the ported `Telemetry` class contains no secret material. Pricing values are explicitly NOT committed (see Summary and FR-009 edge case). |

**Dev-Workflow exception**: none. This slice has no manual pre-deployment
step; the Bicep file already surfaces the connection string and no
infrastructure change is required.

**Health contract**: `/health`, `/health/ready`, `/health/cosmos`,
`/health/tmdb`, `/health/openai` all stay unchanged. No new probes.

## Project Structure

### Documentation (this feature)

```text
specs/005-telemetry-port/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── instruments.md
│   ├── spans.md
│   └── config-keys.md
├── checklists/
│   └── requirements.md
└── spec.md
```

### Source Code (repository root)

```text
MovieTracker.Api/
├── Program.cs                             # EDIT: add OTel wiring, read
│                                          # Telemetry:Enable-Sensitive-Data and
│                                          # the three AzureOpenAI:Price-* keys
│                                          # at startup, add
│                                          # services.AddOpenTelemetry()
│                                          # .WithTracing(...).WithMetrics(...),
│                                          # add builder.Logging.AddOpenTelemetry(...)
│                                          # with EnableTraceBasedLogsSampler=false,
│                                          # add services.AddSingleton(
│                                          # TracerProvider.Default.GetTracer(...)),
│                                          # add services.AddSingleton<ModelPricing?>(...),
│                                          # inject .UseOpenTelemetry on the
│                                          # IChatClient inside its factory,
│                                          # wrap each AIFunction in the
│                                          # AIAgent factory with
│                                          # InstrumentedAIFunction, and open
│                                          # a hand-rolled span around the
│                                          # inline /api/chat/start handler.
├── Telemetry/                             # NEW folder.
│   ├── Telemetry.cs                       # Ported from
│   │                                      # movie-tracker-backend/.../Telemetry.cs:1-127.
│   │                                      # Static class holding the Meter and the
│   │                                      # five instruments plus RecordTool /
│   │                                      # RecordCost helpers and the
│   │                                      # SourceName / ServiceVersion
│   │                                      # constants (SourceName =
│   │                                      # "movie-tracker-api", ServiceVersion
│   │                                      # = "1.0.0"; spec FR-010).
│   ├── InstrumentedAIFunction.cs          # Ported from
│   │                                      # Telemetry.cs:143-223. Sealed
│   │                                      # DelegatingAIFunction. The
│   │                                      # tool-call ILogger category is
│   │                                      # "MovieTracker.Api.ToolCalls"
│   │                                      # (spec FR-006; the Function App
│   │                                      # uses "MovieTracker.Backend.ToolCalls",
│   │                                      # which the port updates to match
│   │                                      # the API's assembly namespace).
│   └── ModelPricing.cs                    # Ported from Telemetry.cs:235-238.
│                                          # Record with
│                                          # (double InputPerMillion,
│                                          # double OutputPerMillion,
│                                          # double? CachedInputPerMillion).
├── Chat/
│   ├── ChatTurnEndpoints.cs               # EDIT: Ask and AskV2 open a
│   │                                      # hand-rolled span at method entry
│   │                                      # ("movie-tracker-api.chat-ask" and
│   │                                      # ".chat-ask-v2"); Ask / AskV2 are
│   │                                      # the minimal-API handlers that
│   │                                      # resolve the new dependencies
│   │                                      # (Tracer, ModelPricing?,
│   │                                      # IConfiguration for deployedModel)
│   │                                      # from the service provider and
│   │                                      # pass them to the private
│   │                                      # RunTurnAsync helper as additional
│   │                                      # parameters. RunTurnAsync itself
│   │                                      # does no DI resolution. Inside the
│   │                                      # helper,
│   │                                      # emits the "Chat-Ask context:
│   │                                      # sending ..." log before the model
│   │                                      # call, records
│   │                                      # ContextRetainedRatio when
│   │                                      # chatMessages.Count > 0, calls
│   │                                      # LogUsage after the model call
│   │                                      # (which emits the "Chat-Ask usage:
│   │                                      # ..." log, sets the four
│   │                                      # gen_ai.usage.* tags on
│   │                                      # Activity.Current, and calls
│   │                                      # Telemetry.RecordCost when
│   │                                      # ModelPricing is non-null),
│   │                                      # and records ToolCallsPerTurn
│   │                                      # with the FunctionCallContent
│   │                                      # count. (Hydration-trim log at
│   │                                      # :314-316 is unchanged.)
│   └── (slice 004 types unchanged)
├── ChatSessions/
│   ├── ChatSessionRepository.cs           # EDIT: constructor adds
│   │                                      # (Tracer tracer,
│   │                                      # ILogger<ChatSessionRepository> logger)
│   │                                      # parameters. Each of
│   │                                      # NewChatSession / GetChatSession /
│   │                                      # SaveChatSession wraps its body
│   │                                      # in using var activity =
│   │                                      # tracer.StartActiveSpan(
│   │                                      # "movie-tracker-api.chat-session-repository.{op}")
│   │                                      # followed by a try/catch that
│   │                                      # emits logger.LogCritical(
│   │                                      # "{@ex}", ex) and rethrows (spec
│   │                                      # FR-007 and FR-008 bullet 4).
│   └── (slice 002 types unchanged)
├── Agents/                                # Unchanged (slice 004).
├── Prompts/                               # Unchanged (slice 004).
├── appsettings.json                       # EDIT: add Logging:OpenTelemetry
│                                          # subsection with the three flags
│                                          # IncludeFormattedMessage,
│                                          # IncludeScopes, ParseStateValues
│                                          # (spec FR-012).
├── appsettings.Development.json           # Unchanged. No telemetry overrides
│                                          # for dev — same as slice 003.
└── MovieTracker.Api.csproj                # EDIT: add two PackageReferences
                                           # (Azure.Monitor.OpenTelemetry.AspNetCore
                                           # 1.6.0, Azure.Monitor.OpenTelemetry.Exporter
                                           # 1.8.3). OpenTelemetry.Extensions.Hosting
                                           # is pulled transitively per
                                           # Phase 0 Decision 1.

infrastructure/
└── (all Bicep files UNCHANGED)            # The connection-string env var is
                                           # already set at
                                           # container-app.bicep:52-54 via a
                                           # secretRef to a direct-value
                                           # Container App secret.
```

**Structure Decision**: Single existing ASP.NET Core project. New `Telemetry/`
folder hosts the three ported types (one public type per file). Edits land
in exactly five existing files (`Program.cs`, `ChatTurnEndpoints.cs`,
`ChatSessionRepository.cs`, `appsettings.json`, `MovieTracker.Api.csproj`).
No test project. No `Agents/*` or `Prompts/*` edits — the telemetry gate
reaches tools through `InstrumentedAIFunction` which wraps them at the
agent-registration site, so the tool bodies themselves don't need to know
about telemetry.

**Delete-not-rewrite note**: nothing is deleted in this slice.

## Complexity Tracking

> Deviations from Core Principles that must be justified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |

No principles are violated. The slice adds exactly the port parity surface the
Function App has today — the three Azure Monitor exporters, the five
instruments, the six hand-rolled spans, the four structured log sites, and
the one sensitive-data gate — and nothing more. Pricing values are
deliberately not committed (matching the Function App's "no defaults"
stance). Bicep is unchanged. The port carries forward one recorded design
subtlety from the Function App: `.UseOpenTelemetry` is applied to the chat
client only, not to the agent, because `AIAgentBuilder.UseOpenTelemetry`
auto-wires the inner chat client's instrumentation and double-counts
`gen_ai.client.token.usage` (spec FR-006, Function App `Program.cs:248-255`).
This is a constraint carried through, not a new principle departure.
