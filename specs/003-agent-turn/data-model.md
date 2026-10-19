# Phase 1 Data Model: Agent Turn With TMDb Tools And Hydration

Every type listed is ported from `C:\projects\dev\movie-tracker-backend\src\MovieTracker.Backend`
with the adaptations named below. The field lists are the spec's authoritative
source (FR-002, FR-003, FR-006, FR-010, FR-013); this document names where
each lives in the API after the port and what rules govern it.

---

## MoviceTrackerChatSession (Cosmos document — enlarged)

**File**: `MovieTracker.Api/ChatSessions/MoviceTrackerChatSession.cs`
(slice 002, extended).

**Fields**:

| Field | CLR type | Serialized form | Source |
|---|---|---|---|
| `id` | `string` | `id` (Cosmos' required identifier) | slice 002 |
| `PartitionKey` | `string` | `PartitionKey` (equal to `id`) | slice 002 |
| `ChatHistory` | `List<Microsoft.Extensions.AI.ChatMessage>` | `ChatHistory` (array) | slice 002 |
| `FunnyFact` | `string?` | `FunnyFact` (nullable) | slice 002 (stays null in this slice) |
| `HydratedMovies` | `Dictionary<string, MovieViewModel>?` | `HydratedMovies` (nullable map, keyed by TMDb movie id string) | **NEW** (FR-006) |

**Rules**:
- `HydratedMovies` default is `null`. Documents written by slice 002
  deserialize cleanly (FR-006).
- `HydratedMovies` is only written by a turn that touches it; a 404 on
  `GetChatSession` performs no write.
- `HydratedMovies` is bounded to 150 entries on save (FR-011), via
  `.Take(150)` applied to the per-session cache. The slice does not formally
  guarantee a 2 MB Cosmos item limit (spec FR-011).

**Why**: FR-006 names the exact property. Reads load into the per-request
cache; writes trim and replace. The 150-entry bound is specifically a
hydration-map bound, not a `ChatHistory` bound.

**How to apply**: `LoadHydrationCache` builds a `ConcurrentDictionary` from
`HydratedMovies` (treating null as empty). `SaveSessionAsync` writes
`session.HydratedMovies = sessionCache.Take(150).ToDictionary(...)` and logs a
single-line informational message if the cache held more than 150 entries
before the trim, matching the Function App.

---

## MovieViewModel (hydrated per-turn and persisted in `HydratedMovies`)

**File**: `MovieTracker.Api/Chat/MovieViewModel.cs` (ported; one type per file).

**CLR fields** (FR-010, PascalCase in source; camelCase on the wire via
`MovieViewModel` serialization under the default web-JSON policy):

| Field | CLR type | Notes |
|---|---|---|
| `PosterPath` | `string` | from `Movie.PosterPath` |
| `Adult` | `bool` | from `Movie.Adult` |
| `Overview` | `string` | from `Movie.Overview` |
| `ReleaseDate` | `DateTime?` | from `Movie.ReleaseDate` |
| `GenreIds` | `List<int>` | from `Movie.Genres.Select(g => g.Id)` |
| `Id` | `string` | the TMDb movie id as string |
| `OriginalTitle` | `string` | from `Movie.OriginalTitle` |
| `OriginalLanguage` | `string` | from `Movie.OriginalLanguage` |
| `Title` | `string` | from `Movie.Title` |
| `BackdropPath` | `string` | from `Movie.BackdropPath` |
| `Popularity` | `double?` | from `Movie.Popularity` |
| `VoteCount` | `int` | from `Movie.VoteCount` |
| `Video` | `bool` | from `Movie.Video` |
| `VoteAverage` | `double` | from `Movie.VoteAverage` |
| `Favorite` | `bool` | always `false` in this slice (FR-010) |
| `ImdbId` | `string` | from `Movie.ImdbId ?? ""` |
| `Trailer` | `MovieTrailerInfo?` | selected per FR-010 trailer rule |
| `GenreNames` | `List<string>?` | from `Movie.Genres.Select(g => g.Name)` (additive, nullable) |
| `Runtime` | `int?` | from `Movie.Runtime` (additive, nullable) |
| `Tagline` | `string?` | from `Movie.Tagline` (additive, nullable) |

**Why**: FR-010 enumerates every field.

**How to apply**: In `ProcessMovieAsync`, a cache miss calls
`tmdbClient.GetMovieAsync(id, MovieMethods.Videos)` once per missed movie
and constructs the view model. A hit skips the detail call. The slice does
not add any new TMDb fields.

---

## MovieTrailerInfo (nested in `MovieViewModel.Trailer`)

**File**: `MovieTracker.Api/Chat/MovieTrailerInfo.cs` (ported from the
backend's `Agents/OpenMovieDbAgent.cs`; one type per file). The
`CreateTrailerInfo` helper (ported from `Function.cs`) lives in the same file
because it is a factory for this record and nothing else uses it.

**CLR fields** (FR-010):

| Field | CLR type |
|---|---|
| `Key` | `string` (YouTube video id) |
| `Name` | `string` |
| `Site` | `string` (must be `"YouTube"` or the trailer is `null`) |
| `Type` | `string` (`"Trailer"`, `"Teaser"`, `"Clip"`, ...) |
| `Official` | `bool` |
| `YouTubeUrl` | `string` = `$"https://www.youtube.com/watch?v={Key}"` |
| `EmbedUrl` | `string` = `$"https://www.youtube.com/embed/{Key}"` |
| `ThumbnailUrl` | `string` = `$"https://img.youtube.com/vi/{Key}/maxresdefault.jpg"` |

**Rules**:
- `Trailer` is null if no `TMDbLib.Objects.General.Video` matches
  `Type == "Trailer" && Site == "YouTube"`.
- Selection order: `Official` descending, then `Size` descending, then first.

---

## Chat message polymorphic hierarchy (wire shape)

**Files** (one type per file):
- `MovieTracker.Api/Chat/ChatMessage.cs` — polymorphic base.
- `MovieTracker.Api/Chat/UserChatMessage.cs` — concrete, role = "user".
- `MovieTracker.Api/Chat/AssistantChatMessage.cs` — concrete, role =
  "assistant", carries `MovieList`.

**Shape** (FR-002):

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = nameof(role))]
[JsonDerivedType(typeof(UserChatMessage), typeDiscriminator: "user")]
[JsonDerivedType(typeof(AssistantChatMessage), typeDiscriminator: "assistant")]
public abstract class ChatMessage
{
    public abstract string role { get; }
    public abstract string Text { get; }
}

public class UserChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "user";
    public override string Text { get; }

    public UserChatMessage(string text)
    {
        Text = text;
    }
}

public class AssistantChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "assistant";
    public override string Text { get; }
    public List<MovieViewModel> MovieList { get; set; } = new();

    public AssistantChatMessage(string text, List<MovieViewModel> movieList)
    {
        Text = text;
        MovieList = movieList;
    }
}
```

**Rules**:
- `role` is lowercase and MUST NOT be renamed (FR-002, Key Entities).
- The outer and nested shapes serialize under the default web-JSON policy
  (camelCase for PascalCase CLR properties — `movieList`, `text`); `role`
  stays lowercase because the base class declares the property that way
  and `[JsonPolymorphic]` carries the discriminator value.
- **`[JsonIgnore]` on both derived `role` overrides is required**, exactly
  as the Function App has it. Without it, each derived instance would
  serialize `role` twice — once as the polymorphic discriminator and once
  as the overridden property — producing a duplicate JSON property that
  breaks the wire contract.

---

## Chat-route response envelopes

**Files** (one type per file):
- `MovieTracker.Api/Chat/ChatMessageResponse.cs` (`/ask`).
- `MovieTracker.Api/Chat/ChatTurnResponse.cs` (`/ask/v2`).

```csharp
public record ChatMessageResponse(string? FunnyFact, List<ChatMessage> Messages);
public record ChatTurnResponse(string? FunnyFact, ChatMessage Turn);
```

(The envelopes are FR-002 and FR-003 respectively; see the prose above.)

**Rules**:
- `FunnyFact` serializes as `funnyFact` and is always `null` in this slice
  (FR-004).
- `Messages` serializes as `messages`; `Turn` serializes as `turn`.
- The `/ask/v2` fallback (empty final assistant message) emits
  `turn = AssistantChatMessage("No movies were found", [])` (spec Edge Case).

---

## MovieListResponse / MovieListItem (model-facing structured output)

**Files** (one type per file):
- `MovieTracker.Api/Chat/MovieListResponse.cs`.
- `MovieTracker.Api/Chat/MovieListItem.cs`.

```csharp
public class MovieListResponse
{
    public string SystemMessage { get; set; }
    public List<MovieListItem> MovieList { get; set; }
}

public class MovieListItem
{
    public string MovieId { get; set; }
    public string MovieName { get; set; }
}
```

**Rules** (FR-013):
- The structured-output JSON schema the model is forced to produce is built
  from `MovieListResponse` with `DisallowAdditionalProperties = true` and
  `RequireAllProperties = true`.
- The schema is generated against a `JsonSerializerOptions`
  (`new(AIJsonUtilities.DefaultOptions) { PropertyNamingPolicy = null }`) so
  the schema names are PascalCase (`SystemMessage`, `MovieList`, `MovieId`,
  `MovieName`) — this is what the system prompt expects.
- `MovieListResponse` deliberately does NOT carry a `FunnyFact` field — the
  model never writes one (FR-004 serves `null`).
- The format is applied per-turn through `ChatClientAgentRunOptions`, not as
  an agent-wide default — spec FR-013. This matters because `/ask` (US3) must
  reach the model without this format, so it keeps returning plain text.

---

## Ask (chat-route request)

**File**: `MovieTracker.Api/Chat/Ask.cs` (ported).

```csharp
public class Ask
{
    public string Input { get; set; } = string.Empty;
}
```

**Rules** (FR-001, FR-016):
- A missing or unparseable JSON body returns 400 with a generic body (spec
  permits "Invalid request, missing ask object"); no agent call is made.
- A body that deserializes but whose `Input` is null / missing / empty /
  whitespace is NOT validated — it is passed straight to the agent, matching
  the Function App.

---

## State transitions in a turn

For both `/api/chat/{chatId}/ask` and `/api/chat/{chatId}/ask/v2`:

1. **Load**: `GetChatSession(chatId)` performs a Cosmos point-read.
   - Miss → 404 (generic body); no further action.
   - Non-NotFound Cosmos fault → 503 (generic body); logged; no further action.
2. **Build per-session cache**: `LoadHydrationCache(session)` → fresh
   `ConcurrentDictionary` (treating null `HydratedMovies` as empty).
3. **Run turn**: `RunTurnAsync(session, ask.Input)` appends the user message,
   reduces the stored history via `ContextReducer.ReduceAsync([..chatMessages])`
   (over a copy — FR-012), runs `agent.RunAsync(modelMessages, null, …)` with
   the per-turn structured-output format applied, appends every model-produced
   message (including tool-call and tool-result messages) onto the stored
   `ChatHistory`, and returns the final assistant message (or `null` if no
   non-empty assistant text was produced).
4. **Hydrate**:
   - `/ask`: walk the full `ChatHistory`, run `ToClientMessageAsync` on each,
     collect non-null results. Hydration runs concurrently per
     assistant message via `Task.WhenAll` across its `MovieList`, writing
     into a pre-sized `MovieViewModel?[]` array by index so the model's
     ordering is preserved, then compacting out null slots.
   - `/ask/v2`: run `ToClientMessageAsync` once on the final assistant
     message. If `RunTurnAsync` returned null, or if the message hydrates to
     null, emit `AssistantChatMessage("No movies were found", [])`.
5. **Save**: `SaveSessionAsync(session, sessionCache)` writes the trimmed
   `HydratedMovies` and replaces the Cosmos document via
   `ReplaceItemAsync`.
6. **Respond**:
   - `/ask`: `Results.Ok(new ChatMessageResponse(null, messages))`.
   - `/ask/v2`: `Results.Ok(new ChatTurnResponse(null, turn))`.
   - A failure between steps 3–5 that reaches the handler → 503 generic body.

---

## What is NOT in the data model

- `FunnyFact` is still present on `MoviceTrackerChatSession` (slice 002
  wrote the property shape). This slice does not write to it; both response
  envelopes serialize it as `null` (FR-004).
- No `OMDb` fields, no reasoning-effort setting, no telemetry types
  (`Telemetry.cs`, `InstrumentedAIFunction`, `ModelPricing`) — spec Out of
  Scope.
- `ChatPlanner` tool payload shapes (ratings, trailers, Wikipedia, funny
  fact) — spec Out of Scope.
