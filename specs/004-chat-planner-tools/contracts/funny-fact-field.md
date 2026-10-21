# Contract: `funnyFact` field on `/api/chat/{chatId}/ask` and `/ask/v2`

**Branch**: `004-chat-planner-tools` | **Date**: 2026-10-20

The two chat routes slice 003 defined are not restructured by this slice.
Their JSON contract from slice 003 (`specs/003-agent-turn/contracts/chat-ask.md`
and `chat-ask-v2.md`) stands unchanged for every top-level and per-message
property except one.

## What changes

The top-level `funnyFact` field — previously always-null (slice 003 FR-004)
— MAY now carry a non-empty string. Its type and name do not change; only
its value distribution does. No new field is added. No existing field is
renamed, retyped, or removed.

## Wire-format summary

- Property name: `funnyFact` (camelCase on the wire, matching slice 003
  FR-002's web-JSON policy).
- Type: `string` or `null`.
- Expected length when non-null: ~100 characters or less (the backend's
  grounded prompt caps the fact at this length; the port carries the
  prompt verbatim).

### `POST /api/chat/{chatId}/ask`

Envelope (slice 003 FR-002):

```json
{
  "funnyFact": "<string or null>",
  "messages": [ /* unchanged from slice 003 */ ]
}
```

### `POST /api/chat/{chatId}/ask/v2`

Envelope (slice 003 FR-003):

```json
{
  "funnyFact": "<string or null>",
  "turn": { /* unchanged from slice 003 */ }
}
```

## Value semantics (per FR-007, FR-011, FR-017)

Both routes serialize the session's current `FunnyFact` into the envelope's
`funnyFact` field. The session value is produced by
`SafeGenerateFunnyFactAsync` running alongside the model call, with the
following rules:

1. **Fresh session, groundable input** — the detected entity's confidence
   score exceeds 0.5 (Wikipedia summary length > 100 contributes 0.6;
   Wikidata facts contribute 0.4) AND the grounded completion returns a
   non-`NONE` reply: `funnyFact` is a non-empty grounded string.
2. **Fresh session, Wikidata non-2xx but Wikipedia summary present** —
   `QueryWikidata` returns null (no throw), `SearchWikipedia` returns a
   summary longer than 100 chars, `CalculateConfidence` scores 0.6, the
   gate clears on Wikipedia alone. The grounded completion runs on the
   Wikipedia summary; `funnyFact` can be a non-empty string.
3. **Fresh session, non-groundable input** — no entity named,
   `ParseDetectedEntity` returned `None`, confidence `<= 0.5` (e.g.
   Wikipedia non-2xx or short summary with no Wikidata facts), the
   grounded completion returned `NONE`, or any exception thrown inside
   `GetEnhancedInfo` reached its outer catch and returned null:
   `funnyFact` is `null`.
4. **Returning session** whose previous turn landed in state (1) or (2):
   the session's existing non-null string is preserved; a quiet turn
   (state 3) does NOT overwrite. The envelope reports the preserved value.

The three states are not newly specified here — they port
`Functions/Function.cs:839-843`:

```csharp
var funnyFact = await funnyFactTask;
if (funnyFact != null)
{
    chatSession.FunnyFact = funnyFact;
}
```

## Interleaving

Both routes read and write the same session's `FunnyFact`, so a `/ask` turn
after a `/ask/v2` turn (and vice versa) observes the latest preserved value
without any route-side coordination (slice 003 FR-007 already guarantees
both routes share the same session state).

## Non-change: structured-output schema

The structured-output DTO the model emits (`MovieListResponse`, slice 003
FR-013) does NOT gain a `FunnyFact` property. The value is produced
out-of-band by `ChatPlanner` and does not travel through the model's reply.
FR-016 and backend `Functions/Function.cs:150-154` are explicit about this.

## Verification

Spec SC-008 and US3's Independent Test exercise the non-null branch;
US3 acceptance scenario 6 and SC-008's rerun allowance exercise the
preservation branch. Full verification happens through the quickstart,
not through automated tests.
