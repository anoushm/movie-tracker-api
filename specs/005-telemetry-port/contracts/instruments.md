# Contract: Custom Instruments

**Feature**: 005-telemetry-port
**Spec**: [spec.md](../spec.md) FR-004
**Function App source**: `movie-tracker-backend/src/MovieTracker.Backend/Telemetry.cs:37-127` and `Functions/Function.cs:711-737, 808-818, 831-833`.

The port emits exactly these five custom instruments on a single `Meter`
named `movie-tracker-api` (version `1.0.0`). The framework-emitted metrics
(`gen_ai.client.token.usage`, `gen_ai.client.operation.duration`, and their
streaming equivalents) continue to come out of
`OpenTelemetryChatClient`'s own instrumentation — they are NOT redefined
here; the Meter above captures them automatically because `AddMeter("movie-
tracker-api")` matches both the custom Meter's name and the framework's
sourceName.

## 1. `gen_ai.client.tool.duration`

| Property | Value |
|---|---|
| **Instrument type** | `Histogram<double>` |
| **Unit** | `s` (seconds) |
| **Dimensions** | `gen_ai.tool.name` (string: the tool's `AIFunction.Name`), `outcome` (string: `"ok"` or `"error"`) |
| **Recording site** | `InstrumentedAIFunction.InvokeCoreAsync` — on both success and failure paths, with the elapsed `Stopwatch.Elapsed.TotalSeconds` |
| **Cardinality (expected)** | 23 tools × 2 outcomes = 46 time series |
| **Function App parity** | `Telemetry.cs:51-54`, recording at `Telemetry.cs:103` |

## 2. `gen_ai.client.tool.invocations`

| Property | Value |
|---|---|
| **Instrument type** | `Counter<long>` |
| **Unit** | `{invocation}` |
| **Dimensions** | `gen_ai.tool.name` (string), `outcome` (string: `"ok"` or `"error"`) |
| **Recording site** | `InstrumentedAIFunction.InvokeCoreAsync` — on both success and failure paths, with increment `1` |
| **Cardinality (expected)** | 23 tools × 2 outcomes = 46 time series |
| **Why it exists** | Framework spans are sampled; this counter is not. "Which tool is failing" cannot be answered from sampled spans with confidence. |
| **Function App parity** | `Telemetry.cs:57-60`, recording at `Telemetry.cs:104` |

## 3. `chat.turn.tool_calls`

| Property | Value |
|---|---|
| **Instrument type** | `Histogram<int>` |
| **Unit** | `{call}` |
| **Dimensions** | *(none)* |
| **Recording site** | `ChatTurnEndpoints.RunTurnAsync`, after `agent.RunAsync` returns. Value is `result.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Count()` |
| **Cardinality** | 1 time series |
| **Why it exists** | `FunctionInvokingChatClient` enforces `MaximumIterationsPerRequest = 12` (slice 003's cap) internally and offers no callback when the cap trips. A distribution pressed up against 12 means real queries are being cut off. |
| **Function App parity** | `Telemetry.cs:68-71`, recording at `Function.cs:831-832` |

## 4. `chat.context.retained_ratio`

| Property | Value |
|---|---|
| **Instrument type** | `Histogram<double>` |
| **Unit** | `1` (dimensionless) |
| **Dimensions** | *(none)* |
| **Recording site** | `ChatTurnEndpoints.RunTurnAsync`, before `agent.RunAsync` is called. Value is `(double)modelMessages.Count / chatMessages.Count`. **Guarded by `chatMessages.Count > 0`** — a turn on an empty history MUST NOT record. |
| **Cardinality** | 1 time series |
| **Why it exists** | Measures how much of the stored conversation compaction actually sent to the model. A value trending toward 1.0 means compaction has stopped saving anything and the token-per-minute ceiling is getting closer. |
| **Function App parity** | `Telemetry.cs:78-81`, recording at `Function.cs:815-818` |

## 5. `gen_ai.usage.cost_usd`

| Property | Value |
|---|---|
| **Instrument type** | `Counter<double>` |
| **Unit** | `USD` |
| **Dimensions** | `gen_ai.request.model` (string: the deployment name, slice 003's `AzureOpenAI:DeploymentName`) |
| **Recording site** | `ChatTurnEndpoints.RunTurnAsync`'s `LogUsage` helper, after the model call. Called ONLY when `AgentResponse.Usage` is non-null AND `ModelPricing?` resolved from DI is non-null. |
| **Recording formula** | `cost = freshInput/1_000_000 * InputPerMillion + cachedInput/1_000_000 * (CachedInputPerMillion ?? InputPerMillion) + output/1_000_000 * OutputPerMillion`, where `cachedInput = usage.CachedInputTokenCount ?? 0` and `freshInput = Math.Max(0, (usage.InputTokenCount ?? 0) - cachedInput)`. Recorded iff `cost > 0`. |
| **Cardinality** | Expected 1-2 time series (one per deployment currently used). |
| **"Unconfigured" state** | When either `AzureOpenAI:Price-Input-Per-1M` or `AzureOpenAI:Price-Output-Per-1M` fails to parse at startup, `ModelPricing?` is null and this counter emits nothing. The committed `appsettings.json` has neither key, matching the Function App's "deliberately no defaults" stance (research Decision 5). |
| **Function App parity** | `Telemetry.cs:89-92` (declaration), `Telemetry.cs:113-126` (`RecordCost`), `Function.cs:212-218` (pricing read), `Function.cs:734-737` (recording call) |

## Framework-emitted metrics (captured by this slice's `AddMeter`, not redefined)

| Metric | Source | Captured because |
|---|---|---|
| `gen_ai.client.token.usage` | `OpenTelemetryChatClient` (Microsoft.Extensions.AI) | `.UseOpenTelemetry(..., sourceName: "movie-tracker-api", ...)` in the IChatClient factory makes the framework's Meter name match `movie-tracker-api`, which `AddMeter("movie-tracker-api")` in `Program.cs` registers. |
| `gen_ai.client.operation.duration` | `OpenTelemetryChatClient` | Same route. |
| HTTP client duration histograms | `AddHttpClientInstrumentation()` on the metrics builder | Direct registration. |

The port does NOT redefine, rename, or shadow any of the above. The spec's
"No new metrics" out-of-scope entry refers to adding instruments beyond the
five custom ones; the framework-emitted set is a port requirement
(FR-002, FR-006).

## Non-contracts

- No per-endpoint request counter (ASP.NET Core instrumentation covers it).
- No Cosmos operation histogram (not in the Function App).
- No OMDb / TMDb / Wikipedia dependency metric (HttpClient instrumentation covers it as generic dependency spans and histograms).
- No health-probe counter.
- No `TrackMetric`, `TrackEvent`, or any classic AI 2.x SDK recording surface.
