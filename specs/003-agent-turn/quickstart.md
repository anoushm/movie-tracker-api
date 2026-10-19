# Quickstart: Agent Turn With TMDb Tools And Hydration

Manual verification, mirroring slice 002. Run the local checks first; then
after the owner deploys the revision, repeat against the deployed FQDN.
Each check maps to a spec success criterion or acceptance scenario.

## Prerequisites

- Slice 002 deployed and verified (`GET /api/chat/start` returns `{ chatId }`
  both locally and on the deployed revision).
- `az login` completed with an identity that holds the Cosmos data-plane
  role grant from slice 001 (local run only).
- `AZURE_CLIENT_ID` is **unset** locally (slice 001 README).
- `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, `AzureOpenAI:DeploymentName`,
  `TheMovieDb:Api-Key`, `Cosmos:Endpoint`, `Cosmos:Database`,
  `Cosmos:Container` all resolve via `appsettings*.json` or user-secrets. The
  Function App's `Chat-Ask` and `Chat-Ask-V2` endpoints are reachable (for
  shape comparison in SC-001).

## Local run

```powershell
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
```

Default launch URL is `http://localhost:5065`. The remote probes default to
PATs on the Function App; replace with the actual host used in the owner's
Function App deployment.

### Check 1 — US1 scenario 1 (SC-001, SC-002)

```powershell
$chatId = (curl.exe -s http://localhost:5065/api/chat/start | ConvertFrom-Json).chatId
if ($chatId -notmatch '^[A-Za-z0-9]{7}$') { throw "bad chatId: $chatId" }

$body = @{ Input = "best Christopher Nolan films" } | ConvertTo-Json
$response = curl.exe -s -X POST "http://localhost:5065/api/chat/$chatId/ask" `
    -H "Content-Type: application/json" -d $body
$parsed = $response | ConvertFrom-Json

if ($parsed.funnyFact -ne $null) { throw "funnyFact should be null per FR-004" }
if ($parsed.messages.Count -ne 2) { throw "expected 2 messages, got $($parsed.messages.Count)" }
if ($parsed.messages[0].role -ne "user") { throw "messages[0].role should be 'user'" }
if ($parsed.messages[1].role -ne "assistant") { throw "messages[1].role should be 'assistant'" }
if ($parsed.messages[1].text -eq $null) { throw "assistant text should be non-null" }
if ($parsed.messages[1].movieList -eq $null) { throw "movieList should be present (possibly empty)" }

"ok: chatId=$chatId first-turn-movies=$($parsed.messages[1].movieList.Count)"
```

Expected: `ok: chatId=<7 chars> first-turn-movies=<n>`.

Cosmos verification: Azure Portal → `movie-tracker-cosmos` → Data Explorer:

```sql
SELECT * FROM c WHERE c.id = "<chatId>"
```

Expected: `ChatHistory` contains the seeded system message plus the user
message plus every message the model emitted on this turn (intermediate
tool-call and tool-result messages included).

### Check 2 — US1 scenario 2 (SC-002, SC-003)

Second turn against the same `chatId`:

```powershell
$body = @{ Input = "which of those had the highest rating?" } | ConvertTo-Json
$response = curl.exe -s -X POST "http://localhost:5065/api/chat/$chatId/ask" `
    -H "Content-Type: application/json" -d $body
$parsed = $response | ConvertFrom-Json

# Messages replay in order: user1, assistant1, user2, assistant2.
if ($parsed.messages.Count -lt 4) { throw "expected >=4 messages, got $($parsed.messages.Count)" }
if ($parsed.messages[0].text -ne "best Christopher Nolan films") { throw "turn 1 user message was reordered or lost" }

"ok: total messages=$($parsed.messages.Count)"
```

Expected: `messages` includes both turns in order. A Cosmos point-read on
`<chatId>` shows `HydratedMovies` is populated for every movie id the model
has referenced across the two turns (SC-003). A reference on the second
turn to a movie id already in `HydratedMovies` should serve from cache
(verify by watching for a repeat TMDb detail call in server logs — the
hit path logs none).

### Check 3 — US2 (v2 route)

```powershell
$body = @{ Input = "and his longest?" } | ConvertTo-Json
$response = curl.exe -s -X POST "http://localhost:5065/api/chat/$chatId/ask/v2" `
    -H "Content-Type: application/json" -d $body
$parsed = $response | ConvertFrom-Json

if ($parsed.funnyFact -ne $null) { throw "funnyFact should be null per FR-004" }
if ($parsed.turn -eq $null) { throw "turn should be present" }
if ($parsed.turn.role -ne "assistant") { throw "turn.role should be 'assistant'" }
if (-not ($parsed.PSObject.Properties.Name -contains "turn")) { throw "ChatTurnResponse must have 'turn'" }
if (-not ($parsed.PSObject.Properties.Name -contains "funnyFact")) { throw "ChatTurnResponse must have 'funnyFact'" }

"ok: v2 turn text=$($parsed.turn.text.Substring(0, [Math]::Min(60, $parsed.turn.text.Length)))..."
```

Then run `/ask` against the same `chatId` and confirm the v2 turn's
user+assistant pair appears in the whole-transcript reply, in order
(US2 acceptance scenario 2).

### Check 4 — US1 scenario 3 (404 for not-found chatId)

```powershell
$status = curl.exe -s -o resp.json -w "%{http_code}" `
    -X POST "http://localhost:5065/api/chat/does-not-exist/ask" `
    -H "Content-Type: application/json" -d '{"Input":"hello"}'
if ($status -ne "404") { throw "expected 404, got $status" }
$body = Get-Content resp.json -Raw
if ($body -match '(?i)key|secret|connectionstring|accountkey|password|exception|stacktrace') {
    throw "404 body leaked something: $body"
}
"ok: 404 body=$body"
```

### Check 5 — Edge Case: missing / unparseable body (FR-016)

```powershell
# Missing body:
$status1 = curl.exe -s -o NUL -w "%{http_code}" `
    -X POST "http://localhost:5065/api/chat/$chatId/ask" -H "Content-Type: application/json"
if ($status1 -ne "400") { throw "expected 400 for missing body, got $status1" }

# Malformed JSON:
$status2 = curl.exe -s -o NUL -w "%{http_code}" `
    -X POST "http://localhost:5065/api/chat/$chatId/ask" `
    -H "Content-Type: application/json" -d 'not-json'
if ($status2 -ne "400") { throw "expected 400 for unparseable body, got $status2" }

"ok: missing=$status1 malformed=$status2"
```

### Check 6 — Edge Case: Cosmos infrastructure failure (FR-016)

Simulate a non-NotFound Cosmos fault. Renaming the **container** to a
non-existent name does not work: Cosmos returns NotFound on the point-read
and the handler is required to map that to 404 (FR-016, US1 scenario 3).
Instead, point the client at an unreachable account by temporarily editing
`Cosmos:Endpoint` in `appsettings.Development.json` to a host that cannot
be reached (for example `https://movie-tracker-cosmos-does-not-exist.documents.azure.com:443/`),
then restart the API and run:

```powershell
$status = curl.exe -s -o resp.json -w "%{http_code}" `
    -X POST "http://localhost:5065/api/chat/$chatId/ask" `
    -H "Content-Type: application/json" -d '{"Input":"hello"}'
if ($status -ne "503") { throw "expected 503, got $status" }
$body = Get-Content resp.json -Raw
if ($body -match '(?i)key|secret|connectionstring|accountkey|password|exception|stacktrace') {
    throw "503 body leaked something: $body"
}
"ok: 503 body=$body"
```

Restore the original `Cosmos:Endpoint` value and restart. An equivalent
alternative is to temporarily revoke the local identity's Cosmos data-plane
role, which triggers an `AuthenticationFailedException` or `CosmosException`
with `Unauthorized`/`Forbidden` — the handler maps either path to 503.

### Check 7 — Dependency probes (FR-018, SC-006)

```powershell
$tmdb  = curl.exe -s -o tmdb.json  -w "%{http_code}" http://localhost:5065/health/tmdb
$oai   = curl.exe -s -o oai.json   -w "%{http_code}" http://localhost:5065/health/openai
if ($tmdb -ne "200") { throw "tmdb probe status $tmdb" }
if ($oai  -ne "200") { throw "openai probe status $oai" }

foreach ($file in "tmdb.json","oai.json") {
    $body = Get-Content $file -Raw
    if ($body -match '(?i)apikey|api-key|bearer|secret|password') {
        throw "$file leaked key material: $body"
    }
}
"ok: tmdb=$tmdb openai=$oai"
```

### Check 8 — US3 (/ask stateless)

```powershell
$body = @{ question = "what's a good quote from Pulp Fiction?" } | ConvertTo-Json
$response = curl.exe -s -X POST http://localhost:5065/ask `
    -H "Content-Type: application/json" -d $body
$parsed = $response | ConvertFrom-Json
if (-not ($parsed.PSObject.Properties.Name -contains "answer")) { throw "/ask must return 'answer'" }
if ($parsed.answer -match '"SystemMessage"\s*:') { throw "/ask returned JSON blob instead of plain text" }
"ok: /ask answer=$($parsed.answer.Substring(0, [Math]::Min(60, $parsed.answer.Length)))..."
```

Expected: a plain-text answer (not a `MovieListResponse` JSON blob) because
`/ask` invokes the agent without the structured-output response format
(FR-013, FR-015).

### Check 9 — Existing routes still respond (SC-006)

```powershell
curl.exe -s -o NUL -w "health=%{http_code} "        http://localhost:5065/health
curl.exe -s -o NUL -w "ready=%{http_code} "         http://localhost:5065/health/ready
curl.exe -s -o NUL -w "cosmos=%{http_code} "        http://localhost:5065/health/cosmos
curl.exe -s -o NUL -w "version=%{http_code} "       http://localhost:5065/version
curl.exe -s -o NUL -w "start=%{http_code}`n"        http://localhost:5065/api/chat/start
```

Expected: `health=200 ready=200 cosmos=200 version=200 start=200`.

### Check 10 — Function App shape parity (SC-001)

For the same `chatId` scenario captured in Check 1 and Check 3, send the
same bodies to the Function App's `Chat-Ask` and `Chat-Ask-V2` endpoints.
Compare top-level property names, types, and nesting of each response. Byte
equality is not required (live model + live TMDb). The property set and
`role` discriminator case must match.

## Deployed run

Replace `http://localhost:5065` with `https://<fqdn>` where `<fqdn>` is the
Container App's ingress FQDN after the owner deploys this revision. Repeat
Checks 1-4 and 7-10. Check 5 (missing/malformed body) is optional. Check 6
(Cosmos infrastructure failure) is local-only — it requires editing
`appsettings.Development.json` or revoking a local role.

## Configuration no-regression check (SC-005)

Covered by code review.

## Program.cs review check (SC-004)

Covered by code review.

## No-secret-material check

```powershell
git grep -nE 'AccountKey=|AccountEndpoint=|SharedAccessSignature|SAS=|TheMovieDb\s*=\s*"[0-9a-f]{32}"' -- MovieTracker.Api infrastructure appsettings*.json
```

Expected: no new matches compared to `origin/main` immediately before this
slice merges.
