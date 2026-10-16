# Phase 0 Research: Chat-Start Session Persistence

The spec is a direct port of a known Function App implementation. No NEEDS
CLARIFICATION markers remain. This file records the handful of adaptation
decisions required to drop the ported code into the API without pulling in
dependencies that the slice excludes.

## Decision 1: Container acquisition is driven by configuration

**Decision**: `ChatSessionRepository` resolves its `Container` via
`cosmosClient.GetContainer(database, container)` where `database` and
`container` are injected from `Cosmos:Database` and `Cosmos:Container` config
keys (read in `Program.cs`, passed to the repository as plain strings).

**Rationale**: FR-007 forbids hard-coding either name. The source hard-codes
`GetContainer("database", "chat-sessions")`; that line is the one meaningful
change to the ported class.

**Alternatives considered**:
- Reading `IConfiguration` inside the repository. Rejected — adds a dependency
  the slice doesn't need and spreads the config key names across the codebase
  instead of keeping them in `Program.cs` where slice 001 already reads them.
- Hard-coding the names and adding a comment. Rejected — directly violates
  FR-007.

## Decision 2: Drop the OpenTelemetry `Tracer` and the logger from the repository

**Decision**: The ported `ChatSessionRepository` constructor becomes
`(CosmosClient cosmosClient, string database, string container)`. No `Tracer`
parameter, no `ILogger` parameter. The `StartActiveSpan` call and the
`try/catch/LogCritical/rethrow` block in `NewChatSession` are removed; the
method's body is `GenId` → `CreateItemAsync` → return the session.

**Rationale**: The spec's Out of Scope list rejects telemetry additions, and
Decision 5 locates the single exception log at the endpoint. Keeping the
repository catch block would double-log every failure and keep an otherwise
unused logger dependency.

**Alternatives considered**:
- Keep the `Tracer` parameter and bind it to a no-op. Rejected — pulls in the
  OpenTelemetry.Api package for a value that is never used.
- Keep the repository log and remove the endpoint log. Rejected — the endpoint
  needs to know a failure happened to return 503; logging there is already
  required, so duplicating it in the repository buys nothing.

## Decision 3: Omit `HydratedMovies` from the ported class

**Decision**: `MoviceTrackerChatSession` does not declare a `HydratedMovies`
property in this slice. The newly-written Cosmos document has no such field.
`FunnyFact` is kept as `string? FunnyFact { get; set; }` initialised to `null`
(it is a primitive, not a model type).

**Rationale**: FR-004 says this slice "does not import, define, or constrain"
the value type of `HydratedMovies`. Any declaration — including a loose
`Dictionary<string, object>?` — is a constraint. A later slice will re-add the
property with its real type (`Dictionary<string, MovieViewModel>?` or
equivalent); documents created by this slice simply lack the field, which the
later slice will treat as null on read.

**Alternatives considered**:
- Port `MovieViewModel` now. Rejected — drags TMDb models into a slice that
  does no TMDb work.
- Declare `Dictionary<string, object>? HydratedMovies` and leave it null.
  Rejected — the `object` value type is still a constraint FR-004 forbids,
  and the serialised document still includes a `"HydratedMovies": null` key
  that misrepresents what the slice knows.

## Decision 4: Repository surface is write-only

**Decision**: Only `NewChatSession(List<ChatMessage>)` is ported. `GetChatSession`
and `SaveChatSession` are not copied over.

**Rationale**: Spec Key Entities and Out of Scope: "the repository surface in
this slice is write-only create." Reads land in slice 003.

**Alternatives considered**:
- Port all three now and leave the extras unused. Rejected — unused code is
  tested only by compilation and rots.

## Decision 5: Error contract lives in the endpoint, not the repository

**Decision**: `ChatSessionRepository.NewChatSession` has no try/catch — it
lets exceptions propagate. The minimal-API endpoint in `Program.cs` wraps the
call in a single `try/catch (Exception ex)`:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Chat-Start session creation failed.");
    return Results.Json(new { error = "Session creation failed." }, statusCode: 503);
}
```

This matches the shape the contract pins (`{ "error": "Session creation failed." }`)
and keeps all failure handling in one place.

**Rationale**: FR-010 says 503 + generic body + log, and forbids exception
messages / stacks / credentials in the body. `Results.Json` with an anonymous
object gives exactly that shape; `Results.Problem` would emit RFC 7807
Problem Details (`application/problem+json` with `type`/`title`/`status`),
which the contract does not describe.

**Alternatives considered**:
- `Results.Problem(statusCode: 503)`. Rejected — different body shape than the
  contract specifies.
- Return 503 inside the repository. Rejected — the repository would depend on
  `IResult`, mixing persistence with HTTP concerns.
- Add a global exception handler. Rejected — one endpoint doesn't justify
  middleware; later slices can promote this if they need it.

## Decision 6: Id generator is a `static` method, not an injected service

**Decision**: `GenId` is copied verbatim as a `private static string` on
`ChatSessionRepository`.

**Rationale**: The source implements it that way. The spec's Out of Scope list
rejects "persistence abstraction, repository pattern, or interface indirection
broader than what this one endpoint requires." A static helper is the smallest
form that satisfies FR-008/FR-009.

**Alternatives considered**:
- Extract an `IIdGenerator` service for testability. Rejected — the method
  takes no inputs and its output is directly observable. This slice has no
  test project (Decision 8); the quickstart smoke tests observe `GenId`'s
  output via the endpoint response.

## Decision 7: System prompt lives in a dedicated constant file

**Decision**: The multi-line system prompt from `Functions/Function.cs` lines
390–477 is copied verbatim into
`MovieTracker.Api/ChatSessions/ChatStartSystemPrompt.cs` as a
`public static class ChatStartSystemPrompt { public const string Text = """..."""; }`.

**Rationale**: The prompt is ~90 lines of text. Inlining it in `Program.cs`
makes the registration site unreadable; inlining it in the repository ties
"what is seeded" to "how it is seeded." A one-file constant keeps the diff
greppable and the spec requirement ("reproduced verbatim") checkable at a
glance.

**Alternatives considered**:
- Load from an embedded resource file. Rejected — adds build config and a file
  that is edited in two syntaxes; the string literal is already verbatim.

## Decision 8: Verification is manual, no tests project

**Decision**: Verification follows `quickstart.md` only. No test project is
introduced in this slice.

**Rationale**: SC-002, SC-004, and SC-005 are small manual checks (5 calls,
a point-read, four endpoint smoke responses). The repository has no tests
project today; introducing one just for this slice is scaffolding the slice
does not need. Adding tests later is a separate decision for whichever slice
first needs an automated regression gate.
