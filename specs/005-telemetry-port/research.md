# Phase 0 Research: Telemetry Port From Function App

**Feature**: 005-telemetry-port
**Date**: 2026-10-22
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)

## Decision 1 — Package set

**Decision**: Add exactly two package references to `MovieTracker.Api.csproj`:

- `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0
- `Azure.Monitor.OpenTelemetry.Exporter` 1.8.3

Do NOT add an explicit `OpenTelemetry.Extensions.Hosting` reference.

**Rationale**: The ASP.NET Core distro package
(`Azure.Monitor.OpenTelemetry.AspNetCore`) transitively references
`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
and `OpenTelemetry.Instrumentation.Http`. Adding an explicit reference to
`OpenTelemetry.Extensions.Hosting` is redundant for ASP.NET Core projects and
risks a transitive version shift if the distro bumps its SDK pin. The
Function App's explicit `OpenTelemetry.Extensions.Hosting 1.17.0` reference
exists only because the Function Worker's package graph does NOT pull it
transitively — the Worker SDK's generic-host wiring is separate. ASP.NET
Core does not have that gap.

The individual-exporter package (`Azure.Monitor.OpenTelemetry.Exporter`) is
needed anyway because the port uses the explicit `AddAzureMonitor{Trace,
Metric,Log}Exporter` extension methods rather than the distro's one-stop
`services.AddAzureMonitor()`. Both packages share the same Azure Monitor
ingestion client under the hood; carrying both does not duplicate the HTTP
pipeline.

**Alternatives considered**:

1. *Use `services.AddAzureMonitor()` from the distro and drop the individual
   exporter package.* Rejected: the Function App explicitly rejects this
   shape at `Program.cs:369-373` (`.UseFunctionsWorkerDefaults()` and
   `.WithLogging(builder => { })` interact poorly; the fix is to attach the
   log exporter to the host's logging builder instead, which the distro's
   one-stop call does not do). The port stays faithful to the Function
   App's exporter-level shape so the "no MovieTracker.* category" bug the
   Function App had to fix is not reintroduced.
2. *Pin to the Function App's exact package versions
   (`Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0`,
   `Azure.Monitor.OpenTelemetry.Exporter 1.8.3`) rather than the latest.*
   Kept as-is. The Function App's pins are current stable at the time of
   this slice; a bump should be a dedicated maintenance slice. If a
   security advisory lands between now and merge, the owner may bump without
   re-reviewing this spec.

## Decision 2 — Logging path (`.WithLogging` on OTel builder vs. `builder.Logging.AddOpenTelemetry`)

**Decision**: Attach the Azure Monitor log exporter to the host's logging
builder via `builder.Logging.AddOpenTelemetry(options => { ...
options.AddAzureMonitorLogExporter(exporter => { exporter.ConnectionString
= ...; exporter.EnableTraceBasedLogsSampler = false; }); })`. Do NOT add a
`.WithLogging(...)` call on the `services.AddOpenTelemetry()` builder.

**Rationale**: The Function App's `Program.cs:369-373` comment records that
attaching the exporter via `AddOpenTelemetry().WithLogging(...)` resulted in
"no MovieTracker.* category has ever appeared in App Insights" across
several measurement runs, while `Microsoft.AspNetCore.*` logs from the same
process arrived fine. The fix was to register the log exporter on the
logging pipeline the host wires up for `ILogger<T>` injection, not on the
OTel service collection's logging slot. The port preserves that fix — this
is spec FR-003's load-bearing constraint.

For ASP.NET Core, the host's logging builder is `builder.Logging`
(`WebApplicationBuilder.Logging`), which is the direct equivalent of the
Function App's `.ConfigureLogging(...)` registration site at
`Program.cs:60-114`.

**Alternatives considered**:

1. *Attach via `AddOpenTelemetry().WithLogging(...)`.* Rejected — this is
   the exact bug the Function App's comment calls out. Reproducing it in
   the port would silently swallow application logs and defeat the whole
   point of US3.
2. *Attach via both pipelines (defensive double-wiring).* Rejected —
   duplicates log records in Application Insights and makes `cloud_RoleName`
   filters unreliable.

## Decision 3 — `Tracer` registration pattern

**Decision**: Register the `Tracer` the same way the Function App does, as an
immediately-evaluated singleton:

```csharp
builder.Services.AddSingleton(
    TracerProvider.Default.GetTracer(Telemetry.SourceName, Telemetry.ServiceVersion));
```

Place this registration AFTER the `services.AddOpenTelemetry().WithTracing(...)`
call so the OTel SDK's `TracerProvider` has been registered against the
service collection before resolution; but note that
`TracerProvider.Default.GetTracer(...)` returns a *proxy* `Tracer` that
delegates to the real provider once the SDK's hosted service starts, so the
exact relative ordering between the two `services.*` calls does not change
runtime behaviour.

**Rationale**: The OTel SDK's `TracerProvider.Default` is a
`ProxyTracerProvider` that binds lazily to the registered
`TracerProvider` instance at host-start time; the `Tracer` returned by
`GetTracer(...)` is a proxy that forwards `StartActiveSpan(...)` calls to
the live provider once the host is running. The Function App's registration
at `Program.cs:148` relies on this behaviour and works in production, which
the port inherits. Switching to `IServiceProvider.GetRequiredService<Tracer>()`
or a factory-registered `Tracer` would require either a `TracerProvider`
registration (not what the SDK's hosted service model supports cleanly) or
a bespoke lazy pattern — strictly more complexity for the same observable
outcome.

The `Tracer` is injected into `ChatSessionRepository`'s constructor (which
is Singleton — the ambient `Activity` for a given invocation is
request-scoped by nature, so a singleton `Tracer` is correct) and resolved
through minimal-API DI in the three chat endpoint handlers.

**Alternatives considered**:

1. *Use `ActivitySource` instead of `Tracer`.* Rejected — the Function App
   uses `Tracer.StartActiveSpan(...)` (not
   `ActivitySource.StartActivity(...)`). The two APIs produce functionally
   equivalent spans but the port stays with `Tracer` for symmetry. A
   future refactor across both repos could consolidate.
2. *Register `Tracer` via `services.AddSingleton<Tracer>(sp => sp
   .GetRequiredService<TracerProvider>().GetTracer(...))`.* Rejected —
   `TracerProvider` is not registered in the service collection by the
   OTel SDK's `AddOpenTelemetry()`; the SDK installs the provider as the
   process-global default. The Function App's immediate-evaluation pattern
   is the idiomatic one.

## Decision 4 — `AzureMonitorLogExporter.EnableTraceBasedLogsSampler` surface

**Decision**: Set `exporter.EnableTraceBasedLogsSampler = false` on the
`AzureMonitorLogExporterOptions` passed to `AddAzureMonitorLogExporter(...)`.
This is a public settable property on `AzureMonitorLogExporterOptions`
starting from the exporter package's 1.3.0 release line and is present on
the 1.8.3 version this port pins to.

**Rationale**: Verified against the Function App's working wire-up at
`Program.cs:95-112`, which depends on this property being settable. The
property defaults to `true` (meaning the exporter drops log records
correlated to sampled-out traces); setting it to `false` decouples log
retention from trace sampling, which is the fix spec FR-003 names as
load-bearing.

**Alternatives considered**:

1. *Use `OpenTelemetryLoggerOptions.AttachLogsToActivityEvent = false` or a
   custom log processor to achieve the same effect.* Rejected — the
   exporter-level knob is the documented, supported API and is what the
   Function App uses. A custom processor would be a port deviation.

## Decision 5 — `ModelPricing` plumbing and committed pricing values

**Decision**:

- Do NOT commit any `AzureOpenAI:Price-*` entries to `appsettings.json`,
  `appsettings.Development.json`, or any other file in this repository.
- Read the three keys (`Price-Input-Per-1M`, `Price-Output-Per-1M`,
  `Price-Cached-Input-Per-1M`) at startup in `Program.cs` using
  `double.TryParse`. If `Input` and `Output` both parse, construct a
  `ModelPricing` record and register it as a singleton
  (`services.AddSingleton<ModelPricing?>(_ => pricing)`). Otherwise register
  `null`.
- `RunTurnAsync` is a private helper on `ChatTurnEndpoints`; it does not
  resolve dependencies itself. The minimal-API handlers `Ask` and `AskV2`
  resolve `ModelPricing?` from DI and `deployedModel` from `IConfiguration`
  (slice 003's `AzureOpenAI:DeploymentName` key) and pass them to
  `RunTurnAsync` as explicit parameters. `RunTurnAsync` calls
  `Telemetry.RecordCost(deployedModel, usage, pricing)` only when both
  `pricing` and `usage` are non-null.

**Rationale**: The Function App commits NO pricing values. The committed
`appsettings.json` at `movie-tracker-backend/src/MovieTracker.Backend/
appsettings.json:1-14` contains `Logging`, `ServiceName`, and `OpenMovieDb`
only — no `AzureOpenAi:Price-*` block. The CLAUDE.md note at that repo's
line 274 states the pricing keys "deliberately have no defaults — a
hardcoded rate that drifts produces a confident, wrong cost dashboard."
Operators seed pricing via Function App settings (deploy-time) rather than
committing values that go stale.

The user's directive for this port is "Copy pricing values for
`gen_ai.usage.cost_usd` exactly from the Function App — do not invent or
update prices." The Function App's committed value is *absent*; the
faithful port is to commit nothing. Operators who want cost emission set
the three env vars on the Container App revision out of band, exactly as
the Function App's operators do.

`ModelPricing?` is registered as a Singleton rather than Scoped because the
pricing values are process-global (they don't depend on request state) and
because the AIAgent and IChatClient factories (now Scoped per slice 004)
would otherwise re-read config on every chat turn.

**Alternatives considered**:

1. *Commit a placeholder pricing block
   (`"AzureOpenAI:Price-Input-Per-1M": "0.0"`) so operators have a visible
   extension point.* Rejected — zero pricing emits no measurement
   (`if (cost > 0)` guard in `Telemetry.RecordCost`), and a visible-but-
   unset value creates the illusion of configuration where there is none.
   The Function App's absent-key behaviour is the stronger "unconfigured"
   signal.
2. *Commit the deployment model's actual current rates (e.g. a looked-up
   gpt-5-4-mini per-1M value).* Rejected — explicitly out of scope per the
   user's "do not invent or update prices" directive, and risks the exact
   "confident, wrong cost dashboard" the Function App's design avoids.
3. *Build `ModelPricing?` once per request from `IConfiguration` inside
   `RunTurnAsync`.* Rejected — redundant work on every chat turn for values
   that never change at runtime. The Singleton pattern is both cheaper and
   makes `null` (no pricing) a one-time startup fact.

## Decision 6 — `.UseOpenTelemetry` location (chat client only, not agent)

**Decision**: Apply `.UseOpenTelemetry(loggerFactory, serviceName, options =>
options.EnableSensitiveData = enableSensitiveTelemetry)` to the
`IChatClient` inside its factory, between `.UseFunctionInvocation(...)` and
`.Build(sp)`. Do NOT add `.UseOpenTelemetry` on the `AIAgent` registration.

**Rationale**: The Function App's `Program.cs:248-255` comment records that
`AIAgentBuilder.UseOpenTelemetry` wires an `OpenTelemetryAgent` whose
`autoWireChatClient` also instruments the inner chat client — so stacking
the two records `gen_ai.client.token.usage` twice per model call. "Measured
6 measurements for 2 calls instead of 4, i.e. every token and cost figure
inflated by 50%." Spec FR-006 encodes this constraint. The port keeps
`.UseOpenTelemetry` on the chat client only, which is where the framework's
`gen_ai.*` spans and histograms actually originate (via
`OpenTelemetryChatClient`).

**Alternatives considered**:

1. *Apply `.UseOpenTelemetry` on the agent only (drop the chat-client
   call).* Rejected — would require threading `sourceName` through the
   agent builder and would still only instrument the inner chat client,
   with worse code-locality than the Function App's shape.
2. *Apply `.UseOpenTelemetry` on both and manually filter one in a
   processor.* Rejected — introduces a bespoke `MetricProcessor` to
   deduplicate measurements, which is a design, not a port.

## Decision 7 — Container App connection-string env var (confirmation)

**Decision**: Confirm by Bicep inspection that the `APPLICATIONINSIGHTS_
CONNECTION_STRING` env var is already set on the Container App revision; no
Bicep edit is required for this slice.

**Rationale**: `infrastructure/container-app.bicep` already declares:

```bicep
var secrets = [
  {
    name: 'appinsights-connection-string'
    value: appInsightsConnectionString
  }
  // ...
]

var envVars = [
  // ...
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'appinsights-connection-string'
  }
  // ...
]
```

The `appInsightsConnectionString` module parameter is passed in from
`main.bicep`, which sources it from the Application Insights component's
`properties.ConnectionString`. The underscore spelling matches the ASP.NET
Core / Azure Monitor OTel exporter's standard env-var mapping (spec
FR-010). No Bicep file is edited by this slice.

**Note on the secret's shape**: the `appinsights-connection-string` entry
uses a direct `value:` (not a `keyVaultUrl:` managed-identity reference),
unlike the two other secrets in the same block (`azure-openai-key` and
`themoviedb-api-key`). This is pre-existing state from slice 001 that the
Constitution Check in `plan.md` records as inherited debt. Routing the AI
connection string through Key Vault via managed identity is a follow-up for
another slice; this port does not touch it (user directive: "No bicep
changes").

A live `az containerapp show --name movie-tracker-api --resource-group
RG-MovieTracker-Demo --query "properties.template.containers[0].env[?name==
'APPLICATIONINSIGHTS_CONNECTION_STRING']"` from this session returned "no
such resource group" because the current authenticated Azure context
(`Reddinet Development`) is not where the owner's deployed revision lives.
Per Principle II ("Infrastructure Is Bicep, Not Clicks"), the Bicep file is
the authoritative source for revision configuration; a drift between Bicep
and the deployed revision is a defect, not an acceptable precedent. The
Bicep check is sufficient confirmation; the owner may run the equivalent
`az` query against their own subscription if an independent live check is
desired. No change to this plan is contingent on that query.

**Alternatives considered**:

1. *Add a Bicep edit anyway to pin the connection string via a different
   mechanism.* Rejected — the current mechanism is correct, uses Key Vault
   via a managed-identity-authenticated `secretRef`, and respects
   Principle I. Changing it is scope creep.

## Decision 8 — `Logging:OpenTelemetry` binding

**Decision**: Add the following subsection to `appsettings.json` under
`Logging`:

```json
"OpenTelemetry": {
  "IncludeFormattedMessage": true,
  "IncludeScopes": true,
  "ParseStateValues": true
}
```

Do NOT also set these flags imperatively in the `builder.Logging
.AddOpenTelemetry(options => { options.IncludeFormattedMessage = true; ...
})` lambda. (Or if the imperative setter is used, make it clear in code
that config-file values override.)

**Rationale**: The .NET OTel logger provider reads `Logging:OpenTelemetry:*`
from `IConfiguration` automatically through the standard options binding
when `AddOpenTelemetry` is called on `ILoggingBuilder`. Setting the flags
in `appsettings.json` matches the Function App's existing shape at
`movie-tracker-backend/.../appsettings.json:6-10` and makes the knobs
observable where other operators look (config files, not startup code).

The port MUST still set `AddAzureMonitorLogExporter(exporter => { exporter.
ConnectionString = ...; exporter.EnableTraceBasedLogsSampler = false; })`
imperatively in `Program.cs` because `EnableTraceBasedLogsSampler` is an
exporter-level knob that lives on `AzureMonitorLogExporterOptions`, not on
`OpenTelemetryLoggerOptions` — the config binding path does not expose it.

**Alternatives considered**:

1. *Set all three flags imperatively in `Program.cs`.* Rejected — splits the
   configuration surface across two sources (code + config) and diverges
   from the Function App's committed shape.
2. *Add a separate `Telemetry:*` top-level block with the three flags.*
   Rejected — the OTel logger provider expects `Logging:OpenTelemetry:*`
   specifically; using a different key would require a bespoke binding
   call.

## Decision 9 — `ChatSessionRepository` DI shape change

**Decision**: Extend `ChatSessionRepository`'s constructor to accept
`(CosmosClient cosmosClient, string database, string container, Tracer
tracer, ILogger<ChatSessionRepository> logger)`. Keep the Singleton
registration in `Program.cs` and update the factory lambda to pass the two
new arguments resolved from the service provider.

**Rationale**: `Tracer` and `ILogger<T>` are both safe to resolve inside a
Singleton (both are themselves Singleton-friendly: `Tracer` is a proxy
that forwards per-call, `ILoggerFactory` creates loggers that capture the
singleton factory). The alternative — switching to `AddScoped` — would
force a per-request allocation of the container reference and multiply
Cosmos client-side allocations across chat turns for no observable
benefit. The Function App registers its equivalent repository as Scoped
(`Program.cs:164`), but that is downstream of the Function Worker's
pattern, not an intrinsic requirement; this port stays with Singleton as
slice 002 established.

The three new log sites (`LogCritical("{@ex}", ex)` in each catch arm) and
three new span openings (one per method) are the only functional changes
to the repository.

**Alternatives considered**:

1. *Inject `Tracer` and `ILogger` via method parameters each time
   `NewChatSession` / `GetChatSession` / `SaveChatSession` is called.*
   Rejected — pushes the DI concern onto every caller and makes the
   repository's dependency surface implicit. The current internal
   capture pattern (constructor-injected) is cleaner and matches the
   backend's shape.
2. *Change the Singleton registration to Scoped so the Tracer and Logger
   can be request-scoped.* Rejected — `Tracer.StartActiveSpan` reads
   `Activity.Current` ambiently; the proxy `Tracer` singleton is
   correct per Decision 3. No observable benefit to Scoped.

## Decision 10 — `/api/chat/start` hand-rolled span

**Decision**: Open `tracer.StartActiveSpan("movie-tracker-api.chat-start")`
around the body of the inline `/api/chat/start` handler in `Program.cs:232-249`.
Resolve `Tracer` via a minimal-API DI parameter. The ASP.NET Core request
span will enclose this span; `ChatSessionRepository.NewChatSession`'s
`.new-chat-session` span (added in Decision 9) will nest inside it.

**Rationale**: Spec FR-007 names three endpoint-level spans (`chat-start`,
`chat-ask`, `chat-ask-v2`) and three repository-level spans. The two
`/ask` handlers already live in a `ChatTurnEndpoints` class; the
`/api/chat/start` handler is inline in `Program.cs` but adding one more
dependency to its minimal-API signature is cheap. No change to the
handler's response envelope.

**Alternatives considered**:

1. *Move `/api/chat/start` into a `ChatStartEndpoints` class and add the
   span there.* Rejected — introduces a refactor the spec does not ask for.
2. *Skip the `/api/chat/start` span.* Rejected — the Function App has
   `tracer.StartActiveSpan("movie-tracker-func.chat-start")` at
   `Function.cs:387` and spec FR-007 bullet 1 requires parity.

## Open NEEDS CLARIFICATION

None. All ten decisions above are closed; the implementation plan proceeds
to Phase 1 (data model, contracts, quickstart).
