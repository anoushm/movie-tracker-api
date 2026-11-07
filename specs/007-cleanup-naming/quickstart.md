# Quickstart: Cleanup and Naming — post-refactor smoke tests

**Slice**: 007-cleanup-naming | **Audience**: the developer finishing the slice and the Codex reviewer

## Prerequisites

- .NET 10 SDK installed and on PATH.
- The repository at `C:\projects\dev\movie-tracker-api` on the branch the slice is being developed on.
- Azure login reachable by `AzureCliCredential` (for `/health/cosmos`): `az login` with access to the Cosmos account declared in `appsettings.Development.json`.
- `AZURE_CLIENT_ID` **UNSET** locally so the credential chain falls back to the Azure CLI identity (see `CLAUDE.md`).
- A terminal in `C:\projects\dev\movie-tracker-api`.

## Build

```powershell
dotnet build MovieTracker.Api/MovieTracker.Api.csproj
```

**Expected**: Build succeeds. Warning count is the same as the start-of-slice build (captured from a `dotnet build` run immediately before any file in `MovieTracker.Api/` was edited). Any new warning is a slice defect.

## Run

```powershell
dotnet run --project MovieTracker.Api/MovieTracker.Api.csproj
```

Note the HTTPS and HTTP ports the ASP.NET Core host logs on start (typically `https://localhost:7xxx` and `http://localhost:5xxx`). Use the HTTP port in the checks below unless HTTPS is explicitly wanted.

## Smoke checks

Each check asserts "same HTTP status + same response body shape + same top-level field names." Response **values** vary normally — the LLM emits a fresh completion every call, each chat session gets a fresh `chatId`, `/version` returns live process timestamps — and this slice does not freeze values. The assertion is on structure. If a status code, JSON shape, or field-name set differs from the pre-slice behaviour, that is a slice defect.

Host and port: ASP.NET Core logs the listening ports on startup. HTTPS is enabled only when running under the `Development` environment **and not** when `ASPNETCORE_ENVIRONMENT` is `Container`. If you run `dotnet run` locally (Development), the HTTP endpoint 307-redirects to HTTPS; follow redirects (`curl -L`) or hit HTTPS directly. If you set `ASPNETCORE_ENVIRONMENT=Container`, the server listens on HTTP without redirecting. Use whichever matches your setup.

### C1. Health endpoints return the same shape

Each call prints the response body and, on the next line, the final HTTP status code (via `-w "\n%{http_code}\n"`).

```powershell
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/health
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/health/ready
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/health/cosmos
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/health/tmdb
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/health/openai
curl -sL -w "`n%{http_code}`n" http://localhost:<port>/version
```

**Expected**:
- `/health` returns `200` with body `"healthy"`.
- `/health/ready` returns `200` with body `"ready"`.
- `/health/cosmos` returns `200` with a JSON object whose top-level keys are `status`, `account`, `database`, `container` (status may be the string `"ok"`; the other three carry live resource names).
- `/health/tmdb` returns `200` with a JSON object whose top-level keys are `status`, `baseUrl`.
- `/health/openai` returns `200` with a JSON object whose top-level keys are `status`, `endpoint`, `deployment`.
- `/version` returns `200` with a JSON object carrying the pre-slice property set (`ImageTag`, `GitSha`, `BuildTime`, `AssemblyVersion`, `Revision`, `Replica`, `Environment`, `Framework`, `StartedAt`, `Uptime`). Timestamps and process-local values are expected to change between runs.

### C2. Chat start returns a session id

```powershell
curl -sL -w "`n%{http_code}`n" -X GET http://localhost:<port>/api/chat/start
```

**Expected**: `200` with a JSON object whose only top-level key is `chatId` (string). Capture the returned `chatId` for the next checks.

### C3. Chat ask (v1) returns funnyFact + messages

```powershell
$body = '{ "input": "Recommend a sci-fi movie from the 90s" }'
curl -sL -w "`n%{http_code}`n" -X POST -H "Content-Type: application/json" -d $body http://localhost:<port>/api/chat/<chatId>/ask
```

**Expected**: `200` with a JSON object whose top-level keys are `funnyFact` (string, possibly null) and `messages` (array of chat message objects, each carrying the pre-slice discriminator `role`). The content of `funnyFact` and `messages` varies by LLM output and is not asserted.

### C4. Chat ask v2 returns funnyFact + turn

```powershell
$body = '{ "input": "Show me its trailer" }'
curl -sL -w "`n%{http_code}`n" -X POST -H "Content-Type: application/json" -d $body http://localhost:<port>/api/chat/<chatId>/ask/v2
```

**Expected**: `200` with a JSON object whose top-level keys are `funnyFact` (string, possibly null) and `turn` (a chat message object with the pre-slice discriminator `role`).

### C5. Legacy endpoints are gone

```powershell
curl -sL -o $null -w "%{http_code}`n" -X POST http://localhost:<port>/Ask
curl -sL -o $null -w "%{http_code}`n" http://localhost:<port>/weatherforecast
```

**Expected**: both print `404`.

### C6. Session round-trip after the rename (FR-005)

```powershell
# Create a session using the pre-slice baseline binary, note the chatId, then
# rebuild with the slice applied and do:
curl -sL -w "`n%{http_code}`n" -X POST -H "Content-Type: application/json" -d '{ "input": "Say hello" }' http://localhost:<port>/api/chat/<chatIdFromPreSlice>/ask
```

**Expected**: `200`. The post-slice server MUST load the session the pre-slice server wrote, deserialise `ChatHistory`, `PartitionKey`, `id`, `FunnyFact`, `HydratedMovies` with their pre-slice values, and successfully save the updated session back. If the pre-slice session cannot be read, the rename has drifted the Cosmos JSON shape — a slice defect.

## Static checks

### S1. No `*Agent` class other than `MovieAgent`

```powershell
rg -n --type cs --glob "!bin/**" --glob "!obj/**" "class\s+\w+Agent\b" MovieTracker.Api
```

**Expected**: exactly one match, the declaration of `MovieAgent`.

### S2. No references to removed folders

```powershell
rg -n --type cs --glob "!bin/**" --glob "!obj/**" "MovieTracker\.Api\.(Prompts|Core|Controllers)" MovieTracker.Api
```

**Expected**: zero matches.

### S3. Folders are gone

```powershell
Test-Path MovieTracker.Api/Prompts
Test-Path MovieTracker.Api/Core
Test-Path MovieTracker.Api/Controllers
```

**Expected**: all three print `False`.

### S4. Code-style checks across changed files

For each file touched by this slice, run the following. `<file>` is either a single `.cs` path, a space-separated list of paths, or a directory passed to ripgrep's directory mode.

Explicit types, no `var`:

```powershell
rg -n "\bvar\s" <file>
```

No documentation comments (`///`), no inline `//` comments, no block `/* */` comments. These are three separate regexes because the FR-008 requirement is to remove **all** code comments from changed files; a single anchored pattern misses trailing `//` on code lines and `/* */` entirely:

```powershell
rg -n "///" <file>
rg -nP "(^|\s)//(?!/)" <file>     # // that is not /// (so, real inline/line comments)
rg -nU "/\*[\s\S]*?\*/" <file>    # block comments; multi-line
```

Private fields have no underscore prefix:

```powershell
rg -n "\bprivate\s+(readonly\s+|static\s+|static\s+readonly\s+)?[A-Za-z0-9_<>?.\[\]]+\s+_[a-zA-Z0-9]" <file>
```

`Async` suffix is required on async methods **except** those registered as tools (every method that is wrapped by `AIFunctionFactory.Create`, which keeps its pre-slice name):

```powershell
rg -nP "\basync\s+[A-Za-z0-9_<>?.\[\]]+\s+([A-Za-z][A-Za-z0-9]*)\s*\(" <file>
# Review each match: if the method is NOT a tool, its name must end in `Async`.
```

**Expected**: zero matches from the comment checks, the `var` check, and the underscore-field check. The async-naming check is a manual review: tool methods keep their pre-slice names (FR-002); every other async method ends in `Async`.

**Explicitly exempt from PascalCase enforcement**: the frozen wire/storage names `id` (on `MovieTrackerChatSession`) and `role` (JSON discriminator on `ChatMessage`), plus every record property name on existing DTOs whose casing is consumed by the frontend or Cosmos. Do not rename them to PascalCase; FR-005 freezes the wire shape.

### S5. Deleted symbols are no longer declared under MovieTracker.Api

```powershell
rg -n --type cs --glob "!bin/**" --glob "!obj/**" "class\s+(WeatherForecast|WeatherForecastController|AskController|AskRequestDot|AskResponseDot|AppError|Result|ResultExtensions)\b" MovieTracker.Api
```

**Expected**: zero matches.

## Frozen-content checks (mandatory before claiming done)

The slice must not touch any of the files listed below. The verification is "did we write to this file?" — not a textual diff against a remote branch, because the prompt forbids every `git` command.

**Mandatory**: before starting the refactor, capture a baseline snapshot of every file this slice freezes, and also of `Program.cs` and the two LLM-observable-string files that live in moving folders. The baseline is the start-of-slice working tree.

```powershell
# One-time, BEFORE editing anything:
New-Item -ItemType Directory -Force C:\projects\temp\007-baseline | Out-Null
Copy-Item -Recurse MovieTracker.Api\appsettings.json C:\projects\temp\007-baseline\
Copy-Item -Recurse MovieTracker.Api\appsettings.Development.json C:\projects\temp\007-baseline\
Copy-Item -Recurse MovieTracker.Api\MovieTracker.Api.csproj C:\projects\temp\007-baseline\
Copy-Item -Recurse MovieTracker.Api\Program.cs C:\projects\temp\007-baseline\
Copy-Item -Recurse .gitignore C:\projects\temp\007-baseline\
Copy-Item -Recurse infrastructure C:\projects\temp\007-baseline\
Copy-Item -Recurse .github C:\projects\temp\007-baseline\
# Also snapshot the files that carry strings the LLM observes (these files move; the strings must not change):
Copy-Item MovieTracker.Api\ChatSessions\ChatStartSystemPrompt.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Prompts\TheMovieDBKernelFunctions.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Prompts\DateTimeKernelFunctions.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Prompts\ChatPlanner.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Agents\OpenMovieDbAgent.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Agents\TrailerAgent.cs C:\projects\temp\007-baseline\
Copy-Item MovieTracker.Api\Agents\WikipediaSearchAgent.cs C:\projects\temp\007-baseline\
```

If this snapshot does not exist when the refactor is reported done, the slice is not done — stop and ask the owner for a baseline (or for a last-known-good tree the baseline can be copied from). There is no acceptable fallback.

After the refactor, verify byte equality with file hashes (`Compare-Object (Get-Content ...)` normalises line endings and is not a byte check):

```powershell
# Each line below must print nothing. If any prints, the slice has drifted and must be reverted.
if ((Get-FileHash MovieTracker.Api\appsettings.json).Hash -ne (Get-FileHash C:\projects\temp\007-baseline\appsettings.json).Hash) { "DRIFT: appsettings.json" }
if ((Get-FileHash MovieTracker.Api\appsettings.Development.json).Hash -ne (Get-FileHash C:\projects\temp\007-baseline\appsettings.Development.json).Hash) { "DRIFT: appsettings.Development.json" }
if ((Get-FileHash MovieTracker.Api\MovieTracker.Api.csproj).Hash -ne (Get-FileHash C:\projects\temp\007-baseline\MovieTracker.Api.csproj).Hash) { "DRIFT: MovieTracker.Api.csproj" }
if ((Get-FileHash .gitignore).Hash -ne (Get-FileHash C:\projects\temp\007-baseline\.gitignore).Hash) { "DRIFT: .gitignore" }

# For the two tree snapshots, hash every file recursively and compare the sorted (relative-path, hash) lists:
$lhs = Get-ChildItem -Recurse -File infrastructure | ForEach-Object { [PSCustomObject]@{ Rel = ($_.FullName -replace [regex]::Escape((Resolve-Path infrastructure).Path + '\'), ''); Hash = (Get-FileHash $_.FullName).Hash } } | Sort-Object Rel
$rhs = Get-ChildItem -Recurse -File C:\projects\temp\007-baseline\infrastructure | ForEach-Object { [PSCustomObject]@{ Rel = ($_.FullName -replace [regex]::Escape((Resolve-Path C:\projects\temp\007-baseline\infrastructure).Path + '\'), ''); Hash = (Get-FileHash $_.FullName).Hash } } | Sort-Object Rel
Compare-Object $lhs $rhs -Property Rel, Hash
# Must print nothing. Repeat the same pattern for .github vs C:\projects\temp\007-baseline\.github.
```

For the LLM-observable string values (which live inside files whose containing folder changed, so a file-level hash does not apply), extract and compare the string literals as **content only** (no source paths, no match-record wrappers):

```powershell
# ChatStartSystemPrompt.Text is a const/static string. Compare the raw file contents of the
# baseline and post-slice ChatStartSystemPrompt.cs via Get-FileHash: the only change allowed
# in that file is its namespace (which stays "MovieTracker.Api.ChatSessions" in this slice,
# so the file is in fact byte-identical and a hash compare is the correct check).
if ((Get-FileHash C:\projects\temp\007-baseline\ChatStartSystemPrompt.cs).Hash -ne (Get-FileHash MovieTracker.Api\ChatSessions\ChatStartSystemPrompt.cs).Hash) {
    "DRIFT: ChatStartSystemPrompt.cs changed. Review the Text literal before reverting."
}

# For [Description(...)] attribute values on tool methods, the containing files move AND
# are renamed (OpenMovieDbAgent.cs -> OmdbTools.cs, etc). A file-level hash does not apply.
# Instead, extract the description literals from both trees in a form that captures:
#   - Both member attributes [Description("...")] and return-value attributes [return: Description("...")]
#   - Both single-literal and concatenated/verbatim ("foo" + "bar" or @"...") forms
#   - Order of occurrence (so repeated values are compared pairwise, not deduplicated)
# then compare the two lists as ordered sequences of literal values.
#
# Because concatenated and multi-line descriptions are not a single regex-friendly form, do
# the comparison as a documented manual pass:
#   1. In each of the baseline tool files (OpenMovieDbAgent.cs, TrailerAgent.cs,
#      WikipediaSearchAgent.cs, TheMovieDBKernelFunctions.cs, DateTimeKernelFunctions.cs,
#      ChatPlanner.cs), list every (member name, Description value) pair, in source order.
#   2. In the post-slice counterpart files (OmdbTools.cs, TrailerTools.cs, WikipediaTools.cs,
#      TmdbTools.cs, DateTimeTools.cs, ChatPlanner.cs), list every (member name, Description value) pair.
#   3. The two lists MUST be equal pairwise: same member names (method names unchanged per FR-002)
#      in the same order, each with a byte-identical Description value.
# If any Description value differs (including whitespace inside the literal), revert. If any
# tool method name differs, revert — FR-002 freezes tool names.

# The agent Name literal on ChatClientAgentOptions must be exactly "MovieTracker.Api".
rg -n --type cs --glob "!bin/**" --glob "!obj/**" 'Name\s*=\s*"MovieTracker\.Api"' MovieTracker.Api
# Must return exactly one match (inside Agents/MovieAgent.cs).
```

Delete `C:\projects\temp\007-baseline\` when the slice is approved by the owner.

Files and surfaces that MUST be byte-identical to the start-of-slice tree:

- `MovieTracker.Api/appsettings.json`
- `MovieTracker.Api/appsettings.Development.json`
- `MovieTracker.Api/MovieTracker.Api.csproj`
- `.gitignore`
- Every file under `infrastructure/`
- Every file under `.github/`
- `ChatStartSystemPrompt.Text` string literal
- Every `[Description("...")]` attribute value on tool methods (`TmdbTools`, `DateTimeTools`, `ChatPlanner`, `OmdbTools`, `TrailerTools`, `WikipediaTools`)
- The agent `Name = "MovieTracker.Api"` literal on `ChatClientAgentOptions`

## Pass criteria

- Every expected result in C1–C6 holds (status code, shape, and field-name set match the start-of-slice behaviour).
- Every expected result in S1–S5 holds.
- Every frozen-content hash/string-comparison command above prints nothing.
- `dotnet build` produced no new warnings compared to the start-of-slice build.

If all of the above hold, the slice is done and ready for the owner to commit.
