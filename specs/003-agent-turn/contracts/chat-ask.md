# Contract: POST /api/chat/{chatId}/ask

Whole-transcript turn. Replays every stored user and assistant message, with
every assistant message hydrated. Shape mirrors the Function App's
`ChatMessageResponse`.

## Request

- **Method**: POST
- **Path**: `/api/chat/{chatId}/ask`
- **Auth**: Anonymous (FR-001).
- **Path parameters**:
  - `chatId` (string) — the id returned by `GET /api/chat/start`.
- **Body**: `application/json`, shape `{ "Input": string }`.
  Deserialized to `MovieTracker.Api.Chat.Ask`.

Example:

```http
POST /api/chat/XyZaBc1/ask
Content-Type: application/json

{ "Input": "best Christopher Nolan films" }
```

## Response — 200

- **Body**: `application/json`, shape `ChatMessageResponse`.
- Serialization: ASP.NET Core default web-JSON policy (camelCase for CLR
  PascalCase properties). The polymorphic `role` discriminator stays lowercase
  because the base class declares `role` that way (`[JsonPolymorphic]`,
  `[JsonDerivedType]`).

```jsonc
{
  "funnyFact": null,                  // FR-004: always null in this slice
  "messages": [
    { "role": "user",      "text": "best Christopher Nolan films" },
    {
      "role": "assistant",
      "text": "<SystemMessage string the model emitted>",
      "movieList": [
        {
          "posterPath": "...",
          "adult": false,
          "overview": "...",
          "releaseDate": "2010-07-15T00:00:00",
          "genreIds": [28, 878],
          "id": "27205",
          "originalTitle": "Inception",
          "originalLanguage": "en",
          "title": "Inception",
          "backdropPath": "...",
          "popularity": 85.4,
          "voteCount": 36000,
          "video": false,
          "voteAverage": 8.4,
          "favorite": false,
          "imdbId": "tt1375666",
          "trailer": {
            "key": "YoHD9XEInc0",
            "name": "...",
            "site": "YouTube",
            "type": "Trailer",
            "official": true,
            "youTubeUrl": "https://www.youtube.com/watch?v=YoHD9XEInc0",
            "embedUrl": "https://www.youtube.com/embed/YoHD9XEInc0",
            "thumbnailUrl": "https://img.youtube.com/vi/YoHD9XEInc0/maxresdefault.jpg"
          },
          "genreNames": ["Action", "Science Fiction"],
          "runtime": 148,
          "tagline": "..."
        }
      ]
    }
  ]
}
```

**Rules**:
- `messages` is the full stored transcript rehydrated — every prior
  user+assistant pair plus the new turn's pair, in order (FR-002, US1
  acceptance scenario 2).
- Intermediate tool-call and tool-result messages (which carry no text) are
  skipped by `ToClientMessageAsync` (returns null). They remain in stored
  `ChatHistory`.
- `movieList` ordering follows the model's chosen order (FR-010). Null slots
  (TMDb hydration failed for that entry) are dropped; surviving entries keep
  the original order. A hydration failure does NOT escalate to 503.
- `movieList` field set is exactly FR-010; byte equality is NOT required
  against the Function App (SC-001).

## Response — 400

- **When**: missing / unparseable JSON body (FR-016).
- **Body**: generic. `"Invalid request, missing ask object"` is acceptable
  (spec permits).
- No agent call is made; the session is not mutated.

## Response — 404

- **When**: Cosmos point-read on `chatId` returns `HttpStatusCode.NotFound`
  (FR-016, US1 acceptance scenario 3).
- **Body**: generic. No credentials, tokens, exception messages. Example:
  `{"error":"Chat not found"}`.
- No document is created.

## Response — 503

- **When** (FR-016, US1 acceptance scenario 4):
  - Any `CosmosException` other than NotFound on read or on save.
  - `AuthenticationFailedException` from `DefaultAzureCredential`.
  - Model-call failure that survives the FR-009 resilience pipeline.
  - Any other exception that reaches the top-level handler.
- **Body**: generic. No exception message, no stack trace, no credential,
  no connection string. Example: `{"error":"Chat request failed."}`.
- Server logs the exception at `LogError`.

## Behavioural invariants

- **Session persistence** (FR-005): the user message and every message the
  model emits (final assistant text + intermediate tool-call/tool-result
  messages) are appended to the session's `ChatHistory` and written back
  before responding.
- **Hydration** (FR-010, FR-011): three-tier cache (per-session dict ->
  `IDistributedCache` -> TMDb detail call). The per-session dict and the
  distributed cache are both populated on process-cache hit; both populated
  on TMDb miss. `.Take(150)` trim on save.
- **Context reducer** (FR-012): the stored history is reduced over a copy
  before being passed to the agent. The stored history itself is never
  truncated by compaction.
- **No locking** (spec Edge Case): concurrent turns on one `chatId` race on
  the Cosmos replace; last writer wins. This matches the Function App.
