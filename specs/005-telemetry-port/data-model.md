# Phase 1 Data Model: Telemetry Port From Function App

**Feature**: 005-telemetry-port
**Date**: 2026-10-22
**Status**: Phase 1

This slice ports telemetry wiring. It introduces no new persisted entities,
no new wire-level DTOs, and no new Cosmos documents. The "data model" below
describes the small set of in-process types that the port materialises,
their lifetimes, their observable identities (metric names, span names,
tag keys), and the config keys that reach them.

## Types

### `Telemetry` (static, `MovieTracker.Api.Telemetry/Telemetry.cs`)

Ported from `movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:37-127`.

**Shape**: static class.

**Public surface**:

| Member | Kind | Notes |
|---|---|---|
| `SourceName` | `public const string = "movie-tracker-api"` | The Function App uses `"movie-tracker-backend"`. Spec FR-010. Load-bearing: must equal the `sourceName` passed to `.UseOpenTelemetry` on the chat client AND the string passed to `AddSource(...)` and `AddMeter(...)` on the OTel builders. |
| `ServiceVersion` | `public const string = "1.0.0"` | Spec FR-010. Matches Function App's `Telemetry.ServiceVersion`. |
| `Meter` | `private static readonly Meter(SourceName, ServiceVersion)` | Single `Meter` for the five instruments. Captured by `AddMeter(SourceName)` on the OTel metrics builder. |
| `ToolDuration` | `public static readonly Histogram<double>` | Name `"gen_ai.client.tool.duration"`, unit `"s"`. Dimensions: `gen_ai.tool.name` (string), `outcome` (string, `"ok"` / `"error"`). |
| `ToolInvocations` | `public static readonly Counter<long>` | Name `"gen_ai.client.tool.invocations"`, unit `"{invocation}"`. Same two dimensions as above. |
| `ToolCallsPerTurn` | `public static readonly Histogram<int>` | Name `"chat.turn.tool_calls"`, unit `"{call}"`. No dimensions. |
| `ContextRetainedRatio` | `public static readonly Histogram<double>` | Name `"chat.context.retained_ratio"`, unit `"1"`. No dimensions. |
| `CostUsd` | `public static readonly Counter<double>` | Name `"gen_ai.usage.cost_usd"`, unit `"USD"`. Dimension: `gen_ai.request.model` (string, the deployment name). |
| `RecordTool(string toolName, TimeSpan elapsed, bool succeeded)` | `public static void` | Records `ToolDuration` and `ToolInvocations` with the two dimensions. |
| `RecordCost(string model, UsageDetails usage, ModelPricing pricing)` | `public static void` | Computes `cost = freshInput/1M*InputRate + cachedInput/1M*(CachedRate ?? InputRate) + output/1M*OutputRate`, then records `CostUsd` with `gen_ai.request.model = model` iff `cost > 0`. |

**Lifetime**: static — one `Meter` per process. Not registered in DI.

**Load-bearing invariants**:

- The `Meter`'s name MUST equal `SourceName`. The framework's `OpenTelemetryChatClient` derives both its `ActivitySource` and its `Meter` from the `sourceName` passed to `.UseOpenTelemetry`, so a single `AddMeter(SourceName)` registration covers both the five custom instruments and the framework's `gen_ai.client.token.usage` + `gen_ai.client.operation.duration` histograms. (Spec FR-004, FR-010, research Decision 3.)
- `RecordTool`'s dimension cardinality is bounded: tool name (23 tools in the current agent, slice 004), outcome (2 values). Total time series across both instruments: 23 × 2 × 2 = 92. Comfortably under Azure Monitor's 5,000 custom-metric time-series/24h/metric cap. Do NOT add `chatId`, movie id, or user text as a dimension. (Spec FR-004 edge case, Function App `Telemetry.cs:32-35`.)
- `RecordCost` is a no-op when `cost <= 0`. The `if (cost > 0)` guard matches the Function App's `Telemetry.cs:122` exactly.

### `InstrumentedAIFunction` (sealed, `MovieTracker.Api.Telemetry/InstrumentedAIFunction.cs`)

Ported from `movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:143-223`.

**Shape**: `internal sealed class InstrumentedAIFunction(AIFunction inner, ILogger? logger = null, bool logArguments = false) : DelegatingAIFunction(inner)`.

**Public surface**: inherited from `DelegatingAIFunction` — `Name`,
`Description`, and `JsonSchema` are forwarded to `inner`, so the tool
schema the model sees is byte-identical to the unwrapped tool.

**Behaviour**:

- `InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken ct)` starts a `Stopwatch`, calls `await base.InvokeCoreAsync(arguments, ct)`. On success: `Telemetry.RecordTool(Name, stopwatch.Elapsed, succeeded: true)` and `LogCall(arguments, elapsed, error: null)`, returns the result. On exception: `Telemetry.RecordTool(Name, stopwatch.Elapsed, succeeded: false)` and `LogCall(arguments, elapsed, ex)`, then rethrow untouched.
- `LogCall(AIFunctionArguments arguments, TimeSpan elapsed, Exception? error)` early-returns when `logger is null` or `logger.IsEnabled(LogLevel.Information)` is false. Renders arguments via `Render(arguments)` when `logArguments == true`, else `"(arguments not logged)"`. Emits either `LogInformation("tool {ToolName}({ToolArguments}) ok in {ElapsedMs}ms", Name, rendered, (int)elapsed.TotalMilliseconds)` on success or `LogWarning("tool {ToolName}({ToolArguments}) FAILED in {ElapsedMs}ms: {Error}", Name, rendered, (int)elapsed.TotalMilliseconds, error.Message)` on failure.
- `Render(AIFunctionArguments arguments)` is a `private static string` helper. Returns `string.Empty` when arguments is null or empty. Joins `key=value` pairs skipping null values; truncates any value longer than 120 characters with a trailing `"..."`.

**Constants**:

- `private const int MaxLoggedArgumentChars = 120` — matches Function App's `Telemetry.cs:205`.

**Lifetime**: constructed inline at the `AIAgent` registration site in
`Program.cs`, wrapping each `AIFunction` in the agent's tool list. Not
registered in DI.

**Load-bearing invariants**:

- `InstrumentedAIFunction` deliberately opens no `Activity`. The framework's `OpenTelemetryChatClient` already opens an `execute_tool <name>` span around the tool invocation; adding a nested span would double the trace depth without new information. (Spec FR-005, Function App `Telemetry.cs:140-141`.)
- Thrown exceptions MUST be rethrown unchanged. The framework catches them, records them on its own `execute_tool` span (via `ActivityStatusCode.Error`), and hands the model a correctable error payload that several tools rely on. (Spec edge case: "A tool throws".)
- The `logger` constructor argument carries a logger with the category `"MovieTracker.Api.ToolCalls"` so operators can tune the tool-call log channel independently. (Spec FR-006 — the Function App uses `"MovieTracker.Backend.ToolCalls"`; the port updates to the API's assembly namespace.)

### `ModelPricing` (sealed record, `MovieTracker.Api.Telemetry/ModelPricing.cs`)

Ported from `movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:235-238`.

**Shape**: `internal sealed record ModelPricing(double InputPerMillion, double OutputPerMillion, double? CachedInputPerMillion)`.

**Semantics**:

- `InputPerMillion`: USD charged per 1,000,000 uncached input tokens.
- `OutputPerMillion`: USD charged per 1,000,000 output tokens.
- `CachedInputPerMillion`: USD charged per 1,000,000 cached input tokens. Null means "fall back to `InputPerMillion`" — understates the discount, which is strictly safer than overstating it. (Spec FR-004 edge case; Function App `Telemetry.cs:232-234`.)

**Lifetime**: constructed once in `Program.cs` at startup. Registered as
`services.AddSingleton<ModelPricing?>(_ => pricingOrNull)` (research
Decision 5). Null when the two required `AzureOpenAI:Price-*` keys fail to
parse (which is the committed, default-with-no-pricing state — see
research Decision 5 and FR-009).

**Load-bearing invariant**:

- `ModelPricing` is constructed ONLY when `AzureOpenAI:Price-Input-Per-1M` AND `AzureOpenAI:Price-Output-Per-1M` both parse successfully through `double.TryParse`. Partial pricing (one of the two configured, the other missing) counts as "unconfigured." (Spec FR-009, Function App `Function.cs:212-218`.)

## Metric contracts

See [contracts/instruments.md](./contracts/instruments.md) for the full
instrument contract (name, unit, dimensions, recording site, cardinality
notes).

## Span contracts

See [contracts/spans.md](./contracts/spans.md) for the full hand-rolled span
contract (span name, opening site, enclosing span, nested spans, tags).

## Config contracts

See [contracts/config-keys.md](./contracts/config-keys.md) for the full
config-key contract (key, type, source, default, consumer).

## Non-persisted state

- `Activity.Current` — the ambient `Activity` the Tracer sets up when
  `StartActiveSpan(...)` is called. Read by `RunTurnAsync`'s `LogUsage`
  helper to attach the four `gen_ai.usage.*` tags; the four tags are set
  only when the corresponding `UsageDetails` field is non-null (spec FR-007
  bullet 3, matching Function App `Function.cs:728-732`'s `Activity.Current
  ?.SetTag(...)` pattern).

## Explicitly NOT in this data model

- **No `TelemetryClient`, `TelemetryConfiguration`, `TelemetryInitializer`,
  or `TelemetryProcessor`.** The classic Application Insights 2.x SDK
  surface is explicitly out of scope (spec Out of Scope). The port uses
  OpenTelemetry end-to-end.
- **No new Cosmos documents, no new wire types, no new DTOs.** This slice
  decorates existing code paths with instrumentation; it does not change
  any serialised shape.
- **No ActivitySource owned by this project outside `TracerProvider.Default
  .GetTracer(SourceName, ServiceVersion)`.** The port registers one Tracer
  under the single service name; it does not create multiple sources per
  concern (chat vs. repository vs. health). The span's observable identity
  is its *name*, not its ActivitySource.
- **No distinct `Logger<Something>` category for the hand-rolled spans.**
  The three endpoint spans and three repository spans use
  `Tracer.StartActiveSpan(...)`, which attaches to `ActivitySource` not
  `ILogger`. The two structured `LogInformation` lines in `RunTurnAsync`
  use the endpoint's injected `ILogger<ChatMessageResponse>` /
  `ILogger<ChatTurnResponse>` as slice 004 already configures (ambient in
  the handler); the three `LogCritical` lines use the repository's
  injected `ILogger<ChatSessionRepository>` (new, added by Decision 9).

## Related research

- [research.md](./research.md) — the ten decisions behind these types and
  their lifetimes.
