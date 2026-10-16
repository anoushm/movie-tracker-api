# Contract: `GET /api/chat/start`

## Request

| Field | Value |
|---|---|
| Method | `GET` |
| Path | `/api/chat/start` |
| Query parameters | None |
| Headers | None required |
| Body | None |
| Authentication | Anonymous (matches Function App's `AuthorizationLevel.Anonymous`) |

## Successful response

| Field | Value |
|---|---|
| Status | `200 OK` |
| Body shape | `{ "chatId": "<string>" }` — exactly one property (FR-001) |

The response is JSON; the exact `Content-Type` header is whatever ASP.NET Core
emits for a JSON result and is not part of this contract (FR-001 excludes
response headers).

`chatId` is a 7-character string drawn from `[A-Za-z0-9]`. See
[data-model.md](../data-model.md) for the generator rule.

Side effect: before the response is written, one new document exists in the
configured Cosmos container with `id` = `PartitionKey` = `chatId` and a
`ChatHistory` of one system message carrying the Chat-Start system prompt.

## Error response

| Status | When | Body |
|---|---|---|
| `503 Service Unavailable` | Cosmos write fails for any reason (authorization, container-not-found, transient, timeout) | `{ "error": "Session creation failed." }` (generic; no exception message, no stack, no credentials, no connection string) |

The exception is logged server-side at the endpoint. FR-010 does not require
classification in the response body and forbids sensitive exception details
(message, stack, credentials, connection strings) — all Cosmos write failures
surface as the same 503 shape.

## Method handling

Any method other than `GET` on `/api/chat/start` is left to ASP.NET Core
routing's default behaviour. The spec does not pin a specific status for
unsupported methods.

## Example

```http
GET /api/chat/start HTTP/1.1
Host: movie-tracker-api.<region>.azurecontainerapps.io
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"chatId":"a9Kq2Pz"}
```
