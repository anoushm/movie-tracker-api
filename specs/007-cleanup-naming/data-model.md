# Data Model: Cleanup and Naming

**Slice**: 007-cleanup-naming | **Date**: 2026-11-06

## Status: no data model changes in this slice

This slice is a C#-level rename and move. No Cosmos document shape, no JSON serialisation shape, no record property name, no `[JsonPropertyName]` attribute, and no partition key changes.

## Frozen entities (documented for reviewer verification, not changed)

### MovieTrackerChatSession (C# type name corrected from `MoviceTrackerChatSession`)

**Storage**: Azure Cosmos DB, serverless, NoSQL. One session per logical chat.

**Serialised fields (unchanged by this slice)** — verified against `MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs`:

- `id` — string, lowercase property name. Cosmos document id.
- `PartitionKey` — string. Separately persisted property used as the Cosmos partition key.
- `ChatHistory` — `List<Microsoft.Extensions.AI.ChatMessage>`. This is the Microsoft.Extensions.AI message type (the LLM protocol type), **not** the API's polymorphic `MovieTracker.Api.Chat.ChatMessage` (which is the DTO returned to the frontend). The two are deliberately distinct; the API type-converts between them in `ChatTurnEndpoints`.
- `FunnyFact` — string (nullable).
- `HydratedMovies` — `Dictionary<string, MovieViewModel>?` keyed by TMDb movie id. The hydration cache embedded in the session.

**Verification**: After the slice, open a session document written by the pre-slice build (for example by calling `GET /api/chat/start` against the pre-slice binary, noting the `chatId`, then rebuilding with the slice and calling `POST /api/chat/{chatId}/ask` against the stored document) and confirm:

1. The repository loads it into the renamed type without error (`ChatSessionRepository.GetChatSession` returns successfully).
2. Every field above deserialises with the same value as the stored document.
3. After a subsequent save, the Cosmos document's JSON keys are byte-identical to the pre-slice document (confirmed by inspecting the document in the Azure portal's Data Explorer or by fetching it with the Cosmos SDK).

The spec's acceptance scenario 7 asserts (1) and (2); (3) is the storage-shape assertion required to catch any accidental serialisation attribute drift.

### ChatMessage and subtypes (API DTOs returned from `/api/chat/{chatId}/ask*`)

**Serialisation**: System.Text.Json with type discriminator property `role`. Subtypes `UserChatMessage` (`role = "user"`) and `AssistantChatMessage` (`role = "assistant"`) are frozen. These types are the **DTOs the frontend consumes**; they are distinct from the `Microsoft.Extensions.AI.ChatMessage` that `ChatHistory` holds inside the Cosmos document.

**Verification**: The JSON discriminator strings and every property name on `ChatMessage`, `UserChatMessage`, `AssistantChatMessage`, `ChatMessageResponse`, `ChatTurnResponse`, `MovieViewModel`, `MovieListItem`, `MovieListResponse`, `MovieTrailerInfo`, and the Wikidata/Wikipedia records remain byte-identical to the pre-slice form.

### Ask

**Shape**: `Ask { string Input }`. Deserialised from the request body of both `POST /api/chat/{chatId}/ask` and `.../ask/v2`. Frozen.

## Deleted types (no replacement, no new type introduced)

- `AskRequestDot`, `AskResponseDot` — DTOs for the removed `AskController`.
- `AppError`, `Result`, `ResultExtensions` — ROP primitives unused after `AskController` deletion.
- `WeatherForecast` — scaffold leftover.

None of these appear in any persisted Cosmos document (verified: they are code-only types referenced from code-only call sites that this slice also deletes).

## Migrations

None. No Cosmos container is touched. No stored document is modified. No index policy changes.
