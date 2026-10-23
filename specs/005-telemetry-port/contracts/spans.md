# Contract: Hand-rolled Spans and Activity Tags

**Feature**: 005-telemetry-port
**Spec**: [spec.md](../spec.md) FR-007
**Function App source**: `movie-tracker-backend/src/MovieTracker.Backend/Functions/Function.cs:387, 854, 906, 728-732` and `ChatSessionRepository.cs:57, 82, 100`.

Six hand-rolled spans are emitted by this slice. All six are opened via
`Tracer.StartActiveSpan(name)` on the Singleton `Tracer` registered in
`Program.cs` (research Decision 3). All six nest inside the ambient
ASP.NET Core request span emitted by `AddAspNetCoreInstrumentation()`
(FR-002).

Framework-emitted spans (`chat`, `orchestrate_tools`, `execute_tool <name>`)
are produced by `OpenTelemetryChatClient` with the Meter/ActivitySource
sourceName `movie-tracker-api`; those sources are registered on the
`TracerProviderBuilder` via `AddSource("movie-tracker-api")` and the four
framework-fallback sources listed in FR-002.

## Endpoint spans (three)

### `movie-tracker-api.chat-start`

| Property | Value |
|---|---|
| **Opening site** | Inline minimal-API handler for `GET /api/chat/start` in `Program.cs` (around the body between the `try { ... }` arms) |
| **Encloses** | `movie-tracker-api.chat-session-repository.new-chat-session` (nested) |
| **Tags set by this slice** | *(none at endpoint level — Cosmos spans carry their own context)* |
| **Function App parity** | `Function.cs:387` (span name there is `movie-tracker-func.chat-start`; the `movie-tracker-func` prefix is renamed `movie-tracker-api` in the port, matching FR-007) |

### `movie-tracker-api.chat-ask`

| Property | Value |
|---|---|
| **Opening site** | `ChatTurnEndpoints.Ask` (minimal-API handler), opened at method entry |
| **Encloses** | `chat`, `orchestrate_tools`, `execute_tool <name>` (framework), `movie-tracker-api.chat-session-repository.get-chat-session`, `movie-tracker-api.chat-session-repository.save-chat-session` |
| **Tags set by this slice** | After the model call, `RunTurnAsync` calls `Activity.Current?.SetTag(...)` with: `gen_ai.usage.input_tokens` (when `usage.InputTokenCount` is non-null), `gen_ai.usage.output_tokens` (when non-null), `gen_ai.usage.cache_read.input_tokens` (when `usage.CachedInputTokenCount` is non-null), `gen_ai.usage.reasoning.output_tokens` (when `usage.ReasoningTokenCount` is non-null). A null field is NOT tagged. |
| **Function App parity** | `Function.cs:854` (span), `Function.cs:728-732` (tags) |

### `movie-tracker-api.chat-ask-v2`

| Property | Value |
|---|---|
| **Opening site** | `ChatTurnEndpoints.AskV2` (minimal-API handler), opened at method entry |
| **Encloses** | same framework and Cosmos spans as `.chat-ask` |
| **Tags set by this slice** | Same four `gen_ai.usage.*` tags, under the same null-field rule |
| **Function App parity** | `Function.cs:906` (span), `Function.cs:728-732` (tags, via the shared `LogUsage` helper) |

## Repository spans (three)

### `movie-tracker-api.chat-session-repository.new-chat-session`

| Property | Value |
|---|---|
| **Opening site** | `ChatSessionRepository.NewChatSession`, around the `CreateItemAsync` call |
| **Encloses** | Cosmos SDK's own HttpClient dependency span (via `AddHttpClientInstrumentation`) |
| **Tags set by this slice** | *(none)* |
| **Catch arm** | On exception, emits `logger.LogCritical("{@ex}", ex)` and rethrows (spec FR-008 bullet 4). |
| **Function App parity** | `ChatSessionRepository.cs:57` (span), `ChatSessionRepository.cs:75` (LogCritical) |

### `movie-tracker-api.chat-session-repository.get-chat-session`

| Property | Value |
|---|---|
| **Opening site** | `ChatSessionRepository.GetChatSession`, around the `ReadItemAsync` call |
| **Encloses** | Cosmos SDK's own HttpClient dependency span |
| **Tags set by this slice** | *(none)* |
| **Catch arm** | On **any** exception (including `CosmosException` with `StatusCode == NotFound`), emits `logger.LogCritical("{@ex}", ex)` and rethrows unchanged, matching the Function App. The endpoint handler in `ChatTurnEndpoints` subsequently filters `CosmosException` with `StatusCode == NotFound` into its 404 branch; the repository's `LogCritical` fires BEFORE that filtering. No exception filtering is added at the repository layer. |
| **Function App parity** | `ChatSessionRepository.cs:82` (span), `ChatSessionRepository.cs:88-91` (catch-all + LogCritical + rethrow) |

### `movie-tracker-api.chat-session-repository.save-chat-session`

| Property | Value |
|---|---|
| **Opening site** | `ChatSessionRepository.SaveChatSession`, around the `ReplaceItemAsync` call |
| **Encloses** | Cosmos SDK's own HttpClient dependency span |
| **Tags set by this slice** | *(none)* |
| **Catch arm** | On exception, emits `logger.LogCritical("{@ex}", ex)` and rethrows. |
| **Function App parity** | `ChatSessionRepository.cs:100` (span), `ChatSessionRepository.cs:108` (LogCritical) |

## Activity.Current tags (post-model-call, in RunTurnAsync)

Set via `Activity.Current?.SetTag(...)` — null `Activity.Current` is a
no-op, matching the Function App's `Function.cs:728-732` pattern.

| Tag | Source value | Set when |
|---|---|---|
| `gen_ai.usage.input_tokens` | `AgentResponse.Usage.InputTokenCount` | `usage.InputTokenCount` is non-null |
| `gen_ai.usage.output_tokens` | `AgentResponse.Usage.OutputTokenCount` | `usage.OutputTokenCount` is non-null |
| `gen_ai.usage.cache_read.input_tokens` | `AgentResponse.Usage.CachedInputTokenCount` | `usage.CachedInputTokenCount` is non-null |
| `gen_ai.usage.reasoning.output_tokens` | `AgentResponse.Usage.ReasoningTokenCount` | `usage.ReasoningTokenCount` is non-null |

All four tags land on the enclosing `.chat-ask` or `.chat-ask-v2` endpoint
span (the ambient `Activity.Current` at the point they are set).

## Non-contracts

- **No new ActivitySource**. The port uses the Singleton `Tracer` built from `TracerProvider.Default.GetTracer(Telemetry.SourceName, Telemetry.ServiceVersion)` — one source for the whole app, matching the Function App.
- **No tags beyond the four listed above** are added by this slice. The framework's `chat` span carries prompts/completions on its own (gated by `Telemetry:Enable-Sensitive-Data`); the port does not duplicate.
- **No span events** (`Activity.AddEvent(...)`) are added. The Function App emits none; the port adds none.
- **No span status manipulation**. `Activity.SetStatus(...)` is left to the framework and to the ambient ASP.NET Core request pipeline.
