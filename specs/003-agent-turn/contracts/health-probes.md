# Contract: Dependency probes

Two new probes satisfy Principle IV for the new load-bearing dependencies
this slice adds. The existing `/health/cosmos` (slice 001) is unchanged.
Neither probe emits key material (FR-018, SC-005).

---

## GET /health/tmdb

### Request

- **Method**: GET
- **Path**: `/health/tmdb`
- **Auth**: Anonymous.

### Response — 200

- **Body**: `application/json`.

```jsonc
{
  "status": "ok",
  "baseUrl": "https://api.themoviedb.org/3"
}
```

### Response — 503

- **When**: any exception from the real round trip (including network
  errors or faults from TMDbLib's own HTTP pipeline).
- **Body**:

```jsonc
{ "status": "fail", "reason": "unavailable" }
```

- No key material. No exception message, no stack.

### Implementation

- Resolve `TMDbClient` from DI.
- Call `await tmdbClient.GetConfigAsync()`. `GetConfigAsync` is the cheapest
  real TMDb round trip, does not consume rate quota, and inherits
  `TMDbClient`'s library HTTP defaults. The probe does not add its own
  timeout: `GetConfigAsync` has no `CancellationToken` overload in TMDbLib
  3.0.0, so a client-side cancellation source would be unused.
- On success return the shape above. The `baseUrl` is the known constant
  (the probe does not read it back from the response — the point is to prove
  the client reached the service).
- On exception, classify generically as `"unavailable"`. No `"timeout"`
  distinction is returned, because the probe cannot enforce one.

---

## GET /health/openai

### Request

- **Method**: GET
- **Path**: `/health/openai`
- **Auth**: Anonymous.

### Response — 200

- **Body**: `application/json`.

```jsonc
{
  "status": "ok",
  "endpoint": "movie-tracker-openai.openai.azure.com",
  "deployment": "gpt-4o"
}
```

- `endpoint` is the host of `AzureOpenAI:Endpoint`, no path, no scheme.
- `deployment` is the value of `AzureOpenAI:DeploymentName`.

### Response — 503

- **When**: any exception, including `OperationCanceledException`.
- **Body**:

```jsonc
{
  "status": "fail",
  "reason": "unavailable",
  "endpoint": "movie-tracker-openai.openai.azure.com",
  "deployment": "gpt-4o"
}
// or reason "timeout"
```

- No key material. No exception message.

### Implementation

- Resolve `IChatClient` from DI (the one the agent is built on).
- `using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));`
- Call `await chatClient.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "ping")],
    new ChatOptions { MaxOutputTokens = 1 },
    cts.Token);`
- `MaxOutputTokens = 1` is the cheapest allowed completion.
- No tools are passed in `ChatOptions.Tools`, so the `UseFunctionInvocation`
  layer passes through without invoking any tool.
- Capture `IConfiguration` to read `AzureOpenAI:Endpoint` and
  `AzureOpenAI:DeploymentName` for the response body. Parse the host from
  `new Uri(endpoint).Host`.

---

## Health contract (constitution)

- `/health` (liveness) and `/health/ready` (readiness) stay cheap and
  dependency-free. **Unchanged** by this slice (SC-006).
- `/health/cosmos` (slice 001) is unchanged.
- The two new probes live on their own paths. A TMDb outage degrades the
  chat routes and `/health/tmdb`, but not `/health` or `/health/ready`.
