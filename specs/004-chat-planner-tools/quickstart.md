# Quickstart: ChatPlanner Tools

Manual verification, mirroring slice 003. Run the local checks first; then
after the owner deploys the revision, repeat against the deployed FQDN.
Each check maps to a spec user story (US1-US4) and the corresponding
Success Criterion.

Commands assume PowerShell. Each HTTP call captures the response as a
hashtable with `statusCode` and `body` so the checks can assert status
codes alongside payload shape.

```powershell
function Invoke-Chat {
    param([string]$Url, [string]$BodyJson)
    $resp = Invoke-WebRequest -Uri $Url -Method POST -Body $BodyJson `
        -ContentType "application/json" -SkipHttpErrorCheck
    return @{
        statusCode = [int]$resp.StatusCode
        body = $resp.Content | ConvertFrom-Json
    }
}

function New-Chat {
    param([string]$BaseUrl)
    $r = Invoke-WebRequest -Uri "$BaseUrl/api/chat/start" -SkipHttpErrorCheck
    if ([int]$r.StatusCode -ne 200) { throw "chat/start returned $($r.StatusCode)" }
    return ($r.Content | ConvertFrom-Json).chatId
}
```

## Prerequisites

- Slice 003 deployed and verified (`POST /api/chat/{chatId}/ask` and
  `/ask/v2` return 200 with `funnyFact: null` and the hydrated-transcript
  shape locally and on the deployed revision).
- Slice 003 local prerequisites unchanged: `az login`, `AZURE_CLIENT_ID`
  unset locally, Cosmos / TMDb / Azure OpenAI config resolved via
  user-secrets or `appsettings*.json`.
- **OWNER step (one-time, before the first deploy that includes this slice,
  spec FR-012)**: issue a new OMDb key at `omdbapi.com` (NOT the key
  committed in the `movie-tracker-backend` repository), then set it as a
  Key Vault secret:

  ```powershell
  az keyvault secret set --vault-name <keyVaultName> `
      --name omdb-api-key --value "<new OMDb key>"
  ```

  No pipeline parameter, no Bicep edit to `key-vault.bicep` — same
  pattern as `azure-openai-key` and `themoviedb-api-key`.
- **Local-dev secret** (FR-013), once per clone:

  ```powershell
  dotnet user-secrets --project MovieTracker.Api/MovieTracker.Api.csproj `
      set "OpenMovieDb:Api-Key" "<same new OMDb key>"
  ```
- Function App's `Chat-Start`, `Chat-Ask`, and `Chat-Ask-V2` endpoints are
  reachable (for the reference calls in US3 and the comparison in US4).

## Local run

```powershell
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
```

Base URL below is `$BASE = "http://localhost:5065"` (default). After the
owner deploys, repeat each check against the Container App FQDN.

### Check 1 — US1 (ratings tools) — SC-001, SC-002

A rating query answered from IMDb (not from a TMDb vote average).

```powershell
$BASE = "http://localhost:5065"
$chatId = New-Chat $BASE

$r1 = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "what's the IMDb rating of Inception?" } | ConvertTo-Json)
if ($r1.statusCode -ne 200) { throw "US1 single-rating: expected 200, got $($r1.statusCode)" }
$text1 = $r1.body.messages[-1].text
if ($text1 -notmatch '(?i)IMDb') { throw "US1: assistant text does not identify the rating source as IMDb: $text1" }

# Cross-check: hit omdbapi.com directly with your new key and confirm the current IMDb value for tt1375666 is quoted somewhere in $text1 (visual compare).
"US1 single-rating ok. text=$text1"

$r2 = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "which has the highest IMDb rating: Inception, The Dark Knight, Interstellar?" } | ConvertTo-Json)
if ($r2.statusCode -ne 200) { throw "US1 comparison: expected 200, got $($r2.statusCode)" }
"US1 comparison text=$($r2.body.messages[-1].text)"

$r3 = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "which of The Godfather, Jaws, Howard the Duck, and Rocky are rated 7 or above?" } | ConvertTo-Json)
if ($r3.statusCode -ne 200) { throw "US1 filter: expected 200, got $($r3.statusCode)" }
"US1 filter text=$($r3.body.messages[-1].text)"
```

Expected (US1 scenarios 1-3):
- `text1` names the IMDb rating value `omdbapi.com` returns at test time
  for `tt1375666` (e.g. "8.8") and attributes it to IMDb.
- The comparison answer names the OMDb-highest of the three.
- The filter answer lists exactly the films whose OMDb IMDb rating is
  `>= 7.0` at test time.

### Check 2 — US2 (trailer tool) — SC-001

Both routes return a `[TRAILER]` wrapper. Also exercises the pronoun flow.

```powershell
$chatId = New-Chat $BASE

$r1 = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "show me the trailer for Dune" } | ConvertTo-Json)
if ($r1.statusCode -ne 200) { throw "US2 /ask: expected 200, got $($r1.statusCode)" }
$text1 = $r1.body.messages[-1].text
if ($text1 -notmatch '\[TRAILER\]https://www\.youtube\.com/watch\?v=[A-Za-z0-9_-]+\[/TRAILER\]') {
    throw "US2 /ask: no valid [TRAILER] wrapper in: $text1"
}
"US2 /ask ok: $($matches[0])"

$r2 = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "show me the trailer for it" } | ConvertTo-Json)
if ($r2.statusCode -ne 200) { throw "US2 pronoun: expected 200, got $($r2.statusCode)" }
if ($r2.body.messages[-1].text -notmatch '\[TRAILER\]https://www\.youtube\.com/watch\?v=[A-Za-z0-9_-]+\[/TRAILER\]') {
    throw "US2 pronoun: no [TRAILER] wrapper on follow-up"
}
"US2 pronoun ok (model resolved 'it' to Dune)"

# /ask/v2 variant on a fresh chat:
$chatId2 = New-Chat $BASE
$r3 = Invoke-Chat "$BASE/api/chat/$chatId2/ask/v2" (@{ Input = "show me the trailer for Oppenheimer" } | ConvertTo-Json)
if ($r3.statusCode -ne 200) { throw "US2 /ask/v2: expected 200, got $($r3.statusCode)" }
if ($r3.body.turn.text -notmatch '\[TRAILER\]https://www\.youtube\.com/watch\?v=[A-Za-z0-9_-]+\[/TRAILER\]') {
    throw "US2 /ask/v2: no [TRAILER] wrapper in turn.text"
}
"US2 /ask/v2 ok: $($matches[0])"
```

Expected (US2 scenarios 1-2): a valid `[TRAILER]...[/TRAILER]` wrapper on
both routes. Paste the `v=<key>` substring into youtube.com to visually
confirm it plays the correct film's trailer.

### Check 3 — US3 (funny fact) — SC-001, SC-008

Two one-shot calls on separate fresh `chatId`s: one for the non-null
branch, one for the null branch. If the non-null call lands in the
`NONE`/below-gate branch (model non-determinism), re-run on a new fresh
`chatId` with the same or a different qualifying input per SC-008.

```powershell
# Non-null branch: fresh chat, well-known movie (SC-008):
$chatId = New-Chat $BASE
$r = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "Tell me about Inception" } | ConvertTo-Json)
if ($r.statusCode -ne 200) { throw "US3 non-null: expected 200, got $($r.statusCode)" }
if ($r.body.funnyFact -eq $null -or $r.body.funnyFact -eq "") {
    Write-Warning "US3 non-null: funnyFact was null. Legal under FR-007 (NONE / below-gate / Wikipedia outage). Re-run on a new fresh chatId per SC-008."
} else {
    "US3 non-null ok: funnyFact=$($r.body.funnyFact)"
}

# Null branch: a different fresh chat, generic input (US3 scenario 3):
$chatId2 = New-Chat $BASE
$r = Invoke-Chat "$BASE/api/chat/$chatId2/ask" (@{ Input = "popular movies last year" } | ConvertTo-Json)
if ($r.statusCode -ne 200) { throw "US3 null-branch: expected 200, got $($r.statusCode)" }
if ($r.body.funnyFact -ne $null) { throw "US3 null-branch: expected funnyFact null for generic query, got '$($r.body.funnyFact)'" }
"US3 null-branch ok"
```

Expected (US3 scenarios 1 and 3; SC-008): a non-null `funnyFact` on the
first fresh chat (re-run with a new fresh `chatId` if the call lands in
the `NONE`/below-gate branch); `null` on a generic-input fresh session.

### Check 4 — US4 (Wikipedia context tools) — SC-001

A well-known entity (Christopher Nolan) returns a well-formed response.

```powershell
$chatId = New-Chat $BASE
$r = Invoke-Chat "$BASE/api/chat/$chatId/ask" (@{ Input = "tell me about Christopher Nolan" } | ConvertTo-Json)
if ($r.statusCode -ne 200) { throw "US4 /ask: expected 200, got $($r.statusCode)" }
if ([string]::IsNullOrEmpty($r.body.messages[-1].text)) { throw "US4 /ask: assistant text is empty" }
"US4 /ask ok. text=$($r.body.messages[-1].text)"
```

Expected (US4 scenarios 1-3): 200 and a well-formed response. The
presence of a Wikipedia-grounded clause is best-effort (US4 scenario 1
notes this).

## Static review

- **SC-002** (single `AIAgent` registration with the three tool sources): Covered by code review.
- **SC-003** (Bicep wiring and no OMDb key in the repo): Covered by code review.
- **SC-005** (deployed revision resolves the OMDb key end-to-end): Covered by Check 1 against the deployed FQDN plus a direct `omdbapi.com` comparison the owner runs on their workstation.
- **SC-006** (Container App env var is a `secretRef`): Covered by code review.
- **SC-007** (ported files present at the backend's relative paths): Covered by code review.

## Deployed-revision run

Replace `$BASE = "http://localhost:5065"` with the Container App's FQDN
and repeat Checks 1-4.

## Not verified by this quickstart

- Health probes for OMDb / Wikipedia / Wikidata — none are added
  (FR-015, recorded Principle IV exception).
- Tool-invocation telemetry / per-tool duration metrics — slice 005.
- Wikipedia unreachable → `funnyFact: null` — SC-004 was removed per
  owner decision; the degraded-dependency behaviour still falls out of
  FR-017 (`GetEnhancedInfo` catches, returns null) and FR-007
  (SafeGenerate swallows exceptions). Not a quickstart check.
