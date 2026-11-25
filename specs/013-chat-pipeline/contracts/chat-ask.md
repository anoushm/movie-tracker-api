# Contract: `POST /api/chat/{chatId}/ask`

**Route name**: `ChatAsk`
**Span name**: `movie-tracker-api.chat-ask`
**Behavior**: unchanged from `main`. Hydrates the full chat history.

## Request

- Method: `POST`
- Path: `/api/chat/{chatId}/ask`
- Content-Type: `application/json`
- Body (`Models.Ask`):

```json
{
  "Input": "<user message string>"
}
```

## Responses

### 200 OK

```json
{
  "FunnyFact": "<string|null>",
  "Messages": [
    { "/* UserChatMessage or AssistantChatMessage */" }
  ]
}
```

- `FunnyFact` is the session's `FunnyFact` after the turn. If the funny-fact generator returned a non-null value, it is assigned to the session; if the generator returned `null` or threw (caught in `SafeGenerateFunnyFactAsync` with warning `Funny fact generation failed; answering without one`), the session's previous `FunnyFact` value is retained. On a fresh session the previous value is `null`. Same semantics as `main`.
- `Messages` is the **full hydrated chat history** (every user and assistant message). System messages and non-text messages are filtered out by `MovieHydrator.ToClientMessageAsync` (same filter as `main`).
- Assistant messages have `Content` plus a `MovieList` of hydrated `MovieViewModel`. Each `MovieId` is resolved via `MovieHydrator.ProcessMovieAsync` → session cache / distributed cache / `TMDbClient.GetMovieAsync` with trailer lookup.

### 400 Bad Request

```json
{
  "error": "Invalid request, missing ask object"
}
```

- Fired on: missing JSON content type, unreadable body (JSON exception), or `Ask == null` after deserialization. No pipeline call happens; the endpoint returns directly.

### 404 Not Found

```json
{
  "error": "Chat not found"
}
```

- Fired when the pipeline returns its distinct not-found outcome (the pipeline caught `CosmosException` with status `NotFound` during session load and surfaced it as a non-exceptional result case).

### 503 Service Unavailable

```json
{
  "error": "Chat request failed."
}
```

- Fired on any other exception from the pipeline (agent, hydration, save, or an unexpected exception from the session load). The handler logs `Chat-Ask failed for session {ChatId}` at `Error` level with the exception (identical to `main`).

## Pipeline mapping

- Endpoint delegates all turn work and full-history hydration to the chat pipeline via a single call.
- Success → `Results.Ok(new ChatMessageResponse(<funnyFact>, <hydrated full history>))`.
- Chat-not-found (distinct non-exceptional outcome from the pipeline) → `Results.NotFound(new { error = "Chat not found" })`.
- Any other exception bubbles to the endpoint's outer `try/catch` → `Results.Json(new { error = "Chat request failed." }, statusCode: 503)` with log `Chat-Ask failed for session {ChatId}`.
- The specific pipeline method name and outcome type are left to `/speckit-tasks` / `/speckit-implement`.

## Side effects (match `main`)

- `ChatHistory` grows by one user message plus every message `AIAgent.RunAsync` adds (text + tool call + tool result). The context reducer summarises earlier tool calls when present.
- `FunnyFact` is overwritten only if the funny-fact generator returned a non-null value; otherwise unchanged.
- The hydration cache (session + distributed) may grow up to `HydratedMovieLimit = 150` entries; a `LogInformation` is emitted when a trim occurs.
- `TelemetryHelpers.ContextRetainedRatio`, `TelemetryHelpers.ToolCallsPerTurn`, `TelemetryHelpers.RecordCost` are recorded with the same arguments as `main`.
- Activity tags `gen_ai.usage.*` set on `Activity.Current` under the same conditions.

## Observability

- OpenTelemetry span `movie-tracker-api.chat-ask` wraps the handler body.
- Log messages (identical to `main`):
  - `Chat-Ask context: sending {IncludedMessages} of {TotalMessages} stored messages to the model` (info)
  - `Chat-Ask usage: input=... cachedInput=... output=... reasoning=... total=...` (info)
  - `Funny fact generation failed; answering without one` (warning) on funny-fact exception
  - `Failed to hydrate a movie entry; skipping it` (warning) on per-movie hydration exception
  - `Movie entry has no MovieId` / `Skipping movie with non-numeric MovieId '{MovieId}'` (warning) in hydration
  - `Hydration cache for session {ChatId} holds {Count} movies; trimming to {Limit}` (info) on cache trim
  - `Chat-Ask failed for session {ChatId}` (error) on outer exception
