# Contract: `GET /api/chat/start`

**Route name**: `ChatStart`
**Span name**: `movie-tracker-api.chat-start`
**Behavior**: unchanged from `main`.

## Request

- Method: `GET`
- Path: `/api/chat/start`
- No body, no query parameters, no headers beyond framework defaults.

## Responses

### 200 OK

```json
{
  "chatId": "<string>"
}
```

- `chatId` is the new session id produced by `ChatSessionRepository.NewChatSessionAsync`. The initial `ChatHistory` contains one system message with `ChatStartSystemPrompt.Text`. `FunnyFact` is `null`. `HydratedMovies` is `null`.

### 503 Service Unavailable

```json
{
  "error": "Session creation failed."
}
```

- Fired when `repo.NewChatSessionAsync` throws. The handler logs `Chat-Start session creation failed.` at `Error` level with the exception (identical to `main`).

## Pipeline mapping

- Endpoint delegates all session-creation work to the chat pipeline via a single call. On success the endpoint returns `Results.Ok(new { chatId = <id> })`.
- Any exception from the pipeline is caught by the endpoint's outer try/catch and mapped to the 503 body above.
- The specific pipeline method name and outcome type are left to `/speckit-tasks` / `/speckit-implement`.

## Observability

- OpenTelemetry span `movie-tracker-api.chat-start` wraps the handler body.
- No additional metrics beyond what the Cosmos SDK emits.
