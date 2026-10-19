# Contract: POST /api/chat/{chatId}/ask/v2

Single-turn variant. Same persistence and reduction as `/ask`; the response
carries only the assistant turn just produced, hydrated. Shape mirrors the
Function App's `ChatTurnResponse`.

## Request

- **Method**: POST
- **Path**: `/api/chat/{chatId}/ask/v2`
- **Auth**: Anonymous (FR-001).
- **Path parameters**:
  - `chatId` (string).
- **Body**: `application/json`, shape `{ "Input": string }`.

Example:

```http
POST /api/chat/XyZaBc1/ask/v2
Content-Type: application/json

{ "Input": "what did he direct in the 2010s?" }
```

## Response — 200

- **Body**: `application/json`, shape `ChatTurnResponse`.
- Serialization: ASP.NET Core default web-JSON policy. `role` stays
  lowercase, as in `/ask`.

```jsonc
{
  "funnyFact": null,
  "turn": {
    "role": "assistant",
    "text": "<SystemMessage string the model emitted>",
    "movieList": [
      {
        "posterPath": "...", "adult": false, "overview": "...",
        "releaseDate": "2014-11-05T00:00:00",
        "genreIds": [12, 18, 878],
        "id": "157336",
        "originalTitle": "Interstellar", "originalLanguage": "en",
        "title": "Interstellar", "backdropPath": "...",
        "popularity": 123.5, "voteCount": 32000, "video": false,
        "voteAverage": 8.4, "favorite": false,
        "imdbId": "tt0816692",
        "trailer": { "...": "same shape as /ask" },
        "genreNames": ["Adventure", "Drama", "Science Fiction"],
        "runtime": 169,
        "tagline": "..."
      }
    ]
  }
}
```

**Rules**:
- `turn.role` is always `"assistant"` on success (US2 acceptance scenario 1).
- `turn.movieList` is `[]` if the model emits `MovieList: []`
  (US2 acceptance scenario 3). TMDb calls the model made during the turn via
  tools are not constrained — only this hydration path.
- **Empty-final fallback** (Edge Case): if `RunTurnAsync` returns null (no
  non-empty final assistant message, including iteration-cap trips), the
  response is 200 with `turn = AssistantChatMessage("No movies were found", [])`,
  matching the Function App's v2 fallback.
- The session is still persisted (`SaveSessionAsync` runs after the hydration
  decision) so the stored transcript survives for the next turn.

## Response — 400 / 404 / 503

Same rules and bodies as `/api/chat/{chatId}/ask`. See `chat-ask.md`.

## Interleaving with /ask

- `/ask` and `/ask/v2` share the same stored session (US2 acceptance
  scenario 2). After an interleaved pair, a subsequent `/ask` call replays
  every turn in order, regardless of which route produced it.
- Both routes share the same hydration cache behaviour (FR-011) because
  both call `LoadHydrationCache` on load and `SaveSessionAsync` on save.
