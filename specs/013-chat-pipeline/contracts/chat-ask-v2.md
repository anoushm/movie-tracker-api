# Contract: `POST /api/chat/{chatId}/ask/v2`

**Route name**: `ChatAskV2`
**Span name**: `movie-tracker-api.chat-ask-v2`
**Behavior**: unchanged from `main`. Hydrates the new assistant turn only.

## Request

- Method: `POST`
- Path: `/api/chat/{chatId}/ask/v2`
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
  "Turn": { "/* AssistantChatMessage */" }
}
```

- `FunnyFact` is the session's `FunnyFact` after the turn (same semantics as `/ask`).
- `Turn` is the single newly hydrated assistant message. If `AIAgent.RunAsync` produced no assistant text or `MovieHydrator.ToClientMessageAsync` returned `null`, `Turn` is `new AssistantChatMessage("No movies were found", new List<MovieViewModel>())` (identical to `main`).

### 400 Bad Request

```json
{
  "error": "Invalid request, missing ask object"
}
```

- Same conditions as `/ask`.

### 404 Not Found

```json
{
  "error": "Chat not found"
}
```

- Fired when the pipeline returns its distinct not-found outcome (same semantics as `/ask`).

### 503 Service Unavailable

```json
{
  "error": "Chat request failed."
}
```

- Fired on any other exception from the pipeline. The handler logs `Chat-Ask-V2 failed for session {ChatId}` at `Error` level with the exception (identical to `main`).

## Pipeline mapping

- Endpoint delegates all turn work and new-turn hydration to the chat pipeline via a single call.
- Success → `Results.Ok(new ChatTurnResponse(<funnyFact>, <hydrated new turn or fallback>))`.
- Chat-not-found → `Results.NotFound(new { error = "Chat not found" })`.
- Any other exception bubbles to the endpoint's outer `try/catch` → `Results.Json(new { error = "Chat request failed." }, statusCode: 503)` with log `Chat-Ask-V2 failed for session {ChatId}`.
- The specific pipeline method name and outcome type are left to `/speckit-tasks` / `/speckit-implement`.

## Side effects (match `main`)

- Same `ChatHistory` growth and `FunnyFact` write semantics as `/ask`.
- Hydration runs over **only the new assistant message** (not the full history). That single message may contain multiple movies. Each movie lookup follows the same path as `main`: session cache → distributed cache → TMDb, populating both caches on a miss. Movies within the message are hydrated in parallel (`Task.WhenAll` over the slots), so a pre-existing race could produce more than one TMDb fetch / cache write for the same id — this is `main`'s behavior, not a new constraint. A per-movie failure is caught by the hydration wrapper and logs `Failed to hydrate a movie entry; skipping it`; the rest of the hydration continues (also `main`'s behavior). The full history is **not** re-hydrated here.
- `HydratedMovieLimit = 150` still applies on save.
- Same telemetry metrics, activity tags, and log messages as `/ask`, except the outer error log is `Chat-Ask-V2 failed for session {ChatId}`.

## Observability

- OpenTelemetry span `movie-tracker-api.chat-ask-v2`.
- Same info / warning / error log lines as `/ask`, with the `Chat-Ask-V2 failed for session {ChatId}` variant on the outer exception path.
