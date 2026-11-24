# Quickstart: Byte-Identical Error-Contract Verification

**Branch**: `013-chat-pipeline` | **Date**: 2026-11-24

Validates that the three chat endpoints return the status codes and bodies on `013-chat-pipeline` that the [contracts](./contracts/) specify (identical to `main`). Also validates the eval rating fix. The owner prompt permits `git` only for step 0 of the owner prompt (branch creation) and excludes infrastructure changes; this quickstart does neither.

## Prerequisites

- `az login` complete; the signed-in identity has access to Key Vault and Cosmos DB in `RG-MovieTracker-Demo`.
- Repo at `C:\projects\dev\movie-tracker-api`, on branch `013-chat-pipeline`.
- PowerShell on Windows.
- The three contract files (`contracts/chat-start.md`, `contracts/chat-ask.md`, `contracts/chat-ask-v2.md`) describe the expected bodies — this quickstart verifies the current branch matches those contracts; the contracts themselves match `main`.

## Reference evidence instead of re-running `main`

The owner prompt forbids additional git operations in this slice. The baseline-vs-branch comparison relies on two non-mutating sources rather than a `git checkout main`:

- The three contract markdown files under `contracts/` — authoritative for the expected 200 / 400 / 404 / 503 bodies.
- `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` on the current branch — the exact error-body string literals and log templates MUST remain byte-for-byte identical to their current values. Grep against that file and against the three contract files as the baseline:

```powershell
Select-String -Path MovieTracker.Api\Chat\ChatTurnEndpoints.cs -Pattern 'Invalid request, missing ask object|Chat not found|Session creation failed\.|Chat request failed\.|Chat-Start session creation failed\.|Chat-Ask failed for session|Chat-Ask-V2 failed for session'
```

Expected: the seven strings above appear in the file after the refactor, verbatim. Each appears **inside the endpoint handlers** (no agent call, no reducer, no hydration, no response-format block in the file).

## Local run

```powershell
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
```

(In a second PowerShell window, run the verifications below. Note the chosen URL / port — the launch settings fix this; typically `http://localhost:5094` for the HTTP profile.)

### 1. `GET /api/chat/start` → 200

```powershell
Invoke-RestMethod -Method GET -Uri 'http://localhost:5094/api/chat/start'
```

Expected: `{ "chatId": "<guid-ish>" }`. Capture it: `$chatId = (Invoke-RestMethod -Method GET -Uri 'http://localhost:5094/api/chat/start').chatId`.

### 2. `POST /api/chat/{chatId}/ask` with a valid body → 200

```powershell
$body = @{ Input = 'Recommend two 1990s crime films.' } | ConvertTo-Json
Invoke-RestMethod -Method POST -Uri "http://localhost:5094/api/chat/$chatId/ask" -ContentType 'application/json' -Body $body
```

Expected: a `ChatMessageResponse { FunnyFact, Messages }` whose `Messages` is the full hydrated chat history. See [contracts/chat-ask.md](./contracts/chat-ask.md) for the response shape. Agent text is non-deterministic; **shape** MUST match.

### 3. `POST /api/chat/{chatId}/ask/v2` with a valid body → 200

```powershell
$body = @{ Input = 'Any more by the same director?' } | ConvertTo-Json
Invoke-RestMethod -Method POST -Uri "http://localhost:5094/api/chat/$chatId/ask/v2" -ContentType 'application/json' -Body $body
```

Expected: a `ChatTurnResponse { FunnyFact, Turn }` whose `Turn` is the single newly hydrated assistant message. See [contracts/chat-ask-v2.md](./contracts/chat-ask-v2.md).

### 4. `POST /api/chat/{chatId}/ask` with no body → 400

```powershell
try {
    Invoke-WebRequest -Method POST -Uri "http://localhost:5094/api/chat/$chatId/ask" -ContentType 'application/json' -Body ''
} catch {
    [int]$_.Exception.Response.StatusCode
    (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
```

Expected: `400`, body `{"error":"Invalid request, missing ask object"}`.

### 5. `POST /api/chat/{chatId}/ask/v2` with no body → 400

Repeat step 4 with `/ask/v2`. Same expected body.

### 6. `POST /api/chat/{chatId}/ask` with malformed JSON → 400

```powershell
try {
    Invoke-WebRequest -Method POST -Uri "http://localhost:5094/api/chat/$chatId/ask" -ContentType 'application/json' -Body '{"not-json'
} catch {
    [int]$_.Exception.Response.StatusCode
    (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
```

Expected: `400`, body `{"error":"Invalid request, missing ask object"}`.

### 7. `POST /api/chat/{chatId}/ask` without `application/json` content type → 400

```powershell
try {
    Invoke-WebRequest -Method POST -Uri "http://localhost:5094/api/chat/$chatId/ask" -ContentType 'text/plain' -Body '{"Input":"x"}'
} catch {
    [int]$_.Exception.Response.StatusCode
    (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
```

Expected: `400`, body `{"error":"Invalid request, missing ask object"}`.

### 8. `POST /api/chat/{unknown}/ask` → 404

```powershell
$body = @{ Input = 'ping' } | ConvertTo-Json
try {
    Invoke-WebRequest -Method POST -Uri 'http://localhost:5094/api/chat/00000000-0000-0000-0000-000000000000/ask' -ContentType 'application/json' -Body $body
} catch {
    [int]$_.Exception.Response.StatusCode
    (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
```

Expected: `404`, body `{"error":"Chat not found"}`.

### 9. `POST /api/chat/{unknown}/ask/v2` → 404

Repeat step 8 with `/ask/v2`. Same expected body.

### 10. 503 paths (code-review verification only)

The three 503 paths (`/start` repository failure, `/ask` pipeline exception, `/ask/v2` pipeline exception) are verified by code review, not by injecting infrastructure failure:

- `ChatTurnEndpoints.cs` outer `try/catch` returns `503 {"error":"Session creation failed."}` with log `Chat-Start session creation failed.` for `GET /api/chat/start`.
- `ChatTurnEndpoints.cs` outer `try/catch` returns `503 {"error":"Chat request failed."}` with log `Chat-Ask failed for session {ChatId}` for `/ask` and `Chat-Ask-V2 failed for session {ChatId}` for `/ask/v2`.

Confirm each pattern is present in `ChatTurnEndpoints.cs` after the refactor (the grep in "Reference evidence" above does this in one pass).

## Eval rating fix verification

Run the eval once and compare its console output against the committed Nov 18 baseline (`MovieTracker.Evals/results/last-run.json` on `main` — the harness's own `CheckComparisonComputer` reads it as the previous run before writing the new one, so the per-check comparison lines ARE the Nov 18 comparison).

```powershell
dotnet run --project MovieTracker.Evals
```

Capture from the console: the full table, the totals line, and the per-check comparison. `last-run.json` is overwritten by the harness — leave it as-is; the owner decides whether to keep the new baseline when committing.

Expected (FR-018):

- The console table shows `rating-1` and `rating-2` rows with `PASS` or `FAIL` in the `result` column (not `SKIP` from a missing OMDb rating in the common case).
- No non-rating check gets worse because of the refactor. If a check drops, rerun once (restoring the baseline each time); if the drop persists on the rerun, trace the cause in the diff before reporting.

## Build verification

```powershell
dotnet build movie-tracker-api.slnx
```

Expected (FR-017 / SC-008): succeeds with zero new warnings compared with `main`. The owner (who has the `main` build output available locally or in CI) compares the warning count against `main` out-of-band.

## When this is reported "done"

- Steps 1-9 behave per the contracts: byte-identical error bodies on 4-9 and matching DTO shape on 1-3.
- Step 10 grep shows the seven byte-identical strings are present in `ChatTurnEndpoints.cs`, each in an endpoint handler.
- Eval rating fix: both rating cases graded; no check gets worse vs Nov 18 baseline.
- Build: zero new warnings vs `main`.
- Only the files named in `plan.md` "Scope Guard" are changed by the slice. `results/last-run.json` will show as modified in `git status` after the eval run; the owner decides whether to keep the new baseline.
