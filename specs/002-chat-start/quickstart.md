# Quickstart: Chat-Start Session Persistence

Prove the slice end-to-end against both a local run and the deployed Container
App. Each check maps to a spec acceptance scenario or success criterion.

## Prerequisites

- Slice 001 deployed and verified (`GET /health/cosmos` returns 200 locally and
  in Azure).
- `az login` completed with an identity that holds the Cosmos data-plane role
  grant from slice 001 (local run only).
- `AZURE_CLIENT_ID` is **unset** locally (slice 001 README).
- `Cosmos:Endpoint`, `Cosmos:Database`, `Cosmos:Container` resolve through
  `MovieTracker.Api/appsettings*.json` or user-secrets. The Data Explorer
  check below runs against the configured database and container.

## Local run

```powershell
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
```

The HTTP launch profile listens on `http://localhost:5065` (see
`Properties/launchSettings.json`). Replace in checks below if your profile
differs.

### Check 1 — Happy path returns 200 + chatId (SC-001, US1 scenario 1)

```powershell
$response = curl.exe -s -o response.json -w "%{http_code}" http://localhost:5065/api/chat/start
if ($response -ne "200") { throw "expected 200, got $response" }
$chatId = (Get-Content response.json | ConvertFrom-Json).chatId
if ($chatId -notmatch '^[A-Za-z0-9]{7}$') { throw "chatId '$chatId' does not match [A-Za-z0-9]{7}" }
"status=$response chatId=$chatId"
```

Expected: `status=200 chatId=<7 chars>`. Throws if either check fails.

### Check 2 — Document exists in Cosmos (SC-002, US1 scenario 2)

Azure Portal → `movie-tracker-cosmos` → Data Explorer → the container, then
run:

```sql
SELECT * FROM c WHERE c.id = "<chatId>"
```

Expected: a document whose `id` and `PartitionKey` both equal `<chatId>`, and
whose `ChatHistory` is a single-element array with role `system` and text
matching the Chat-Start system prompt.

### Check 3 — Five sequential calls, 5 distinct ids, 5 documents (SC-002, SC-005, US2 Independent Test)

```powershell
$ids = 1..5 | ForEach-Object {
    $body = curl.exe -s http://localhost:5065/api/chat/start
    ($body | ConvertFrom-Json).chatId
}
if (($ids | Sort-Object -Unique).Count -ne 5) { throw "duplicate chatId in $ids" }
foreach ($id in $ids) {
    if ($id -notmatch '^[A-Za-z0-9]{7}$') { throw "chatId '$id' violates [A-Za-z0-9]{7}" }
}
"5 ids: $ids"
```

Query each id against Cosmos using the Check 2 statement, substituting
`<chatId>` with each returned id.

### Check 4 — Existing endpoints still respond (SC-004)

```powershell
curl.exe -s -o $null -w "health=%{http_code} "        http://localhost:5065/health
curl.exe -s -o $null -w "ready=%{http_code} "         http://localhost:5065/health/ready
curl.exe -s -o $null -w "cosmos=%{http_code} "        http://localhost:5065/health/cosmos
curl.exe -s -o $null -w "version=%{http_code} "       http://localhost:5065/version
curl.exe -s -o $null -w "ask=%{http_code}`n" -X POST  http://localhost:5065/Ask `
    -H "Content-Type: application/json" -d '{"question":"ping"}'
```

Expected: `health=200 ready=200 cosmos=200 version=200 ask=200` (or whatever
`/Ask` returned on `main` before this slice — compare to a pre-change baseline).

## Deployed run

Replace `http://localhost:5065` with `https://<fqdn>` where `<fqdn>` is the
Container App's ingress FQDN. Repeat Check 1 through Check 4.

## Error path (US1 scenario 4, FR-010)

Simulate by temporarily renaming `Cosmos:Container` in `appsettings.Development.json`
to a non-existent container value, restart the API, then:

```powershell
$status = curl.exe -s -o response.json -w "%{http_code}" http://localhost:5065/api/chat/start
if ($status -ne "503") { throw "expected 503, got $status" }
$body = Get-Content response.json -Raw
if ($body -match '(?i)key|secret|connectionstring|accountkey|password') {
    throw "error body leaked a potential secret: $body"
}
"status=$status body=$body"
```

**Restore** the original `Cosmos:Container` value after this check.

Expected: `status=503`, body is the generic `{"error":"Session creation failed."}`
with no stack trace, no account key, no connection string. The server log for
this request contains the `CosmosException` with its stack.

## No-regression check (SC-003)

```powershell
git grep -nE 'AccountKey=|AccountEndpoint=|SharedAccessSignature|SAS=|AccountKey\s*=' -- MovieTracker.Api infrastructure appsettings*.json
```

Expected: no new matches beyond anything already on `main`. Compare against
`git grep` on `origin/main` to confirm.
