# Quickstart: Function App to Container App Cutover

**Feature**: `specs/006-cutover` · **Branch**: `006-cutover` · **Date**: 2026-10-26

Two sections:

- **A. Deployed-site verification runbook** — the three manual verifications from `spec.md`, consolidated in FR-014's ordering so the operator runs the whole cutover from one page.
- **B. Local-run guide** — how to run the Angular frontend locally against the deployed Container App without any extra tooling (per FR-012).

Both sections assume you are on branch `006-cutover` of `movie-tracker-api` and have an authenticated `az` CLI session.

---

## A. Deployed-site verification runbook

The order below is mandatory per FR-014: the Function App stop happens last, only after US1 and US2 verify green on the deployed stack. **Do not** reorder.

### A.1 Prerequisites

- `az` CLI authenticated to the operator's tenant.
- `AZURE_CONFIG_DIR` set to the config dir that holds the right login for both the Pay-As-You-Go subscription (SWA) and the Development subscription (Container App). Example: `$env:AZURE_CONFIG_DIR = "$env:USERPROFILE\.azure-personal"`.
- Push access on `main` in both `movie-tracker-api` (this repo) and `movie-tracker-app` (frontend).
- A browser with DevTools and a Network tab.

### A.2 Step 1 — Deploy the Container App CORS change

Edit `infrastructure/container-app.bicep`, changing the ingress `corsPolicy.allowedOrigins` from `['*']` to the Function App parity set:

```bicep
corsPolicy: {
  allowedOrigins: [
    'https://green-mud-037bbdc1e.5.azurestaticapps.net'
    'http://localhost:4200'
  ]
  allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
  allowedHeaders: ['*']
  allowCredentials: false
  maxAge: 3600
}
```

Commit on `006-cutover`. The PR will run the existing `Validate Bicep` and `What-If` workflows. The What-If output must show exactly one property change (the `allowedOrigins` field under `corsPolicy`) with no delete / replace actions. Investigate anything else before merging.

Once merged into `main`, the deploy workflow applies the change. **Verify the deployed value before continuing**:

```powershell
az containerapp show `
  --name movie-tracker-api `
  --resource-group RG-MovieTracker-Demo `
  --subscription ba298da6-0118-4876-a867-407d081fd78d `
  --query 'properties.configuration.ingress.corsPolicy.allowedOrigins'
```

Expected output:

```json
[
  "https://green-mud-037bbdc1e.5.azurestaticapps.net",
  "http://localhost:4200"
]
```

### A.3 Step 2 — Deploy the frontend URL change

In the `movie-tracker-app` repo on `main`, edit `src/enviroments/enviroments.ts` to change the one property:

```ts
chatBaseUrl: 'https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat',
```

Leave every other property in the file as-is.

Commit on `main` and push. The push fires `.github/workflows/azure-static-web-apps-green-mud-037bbdc1e.yml`:

1. `npm run build` bakes the new URL into the bundle.
2. Deploy to the staging slot.
3. Run the existing Playwright E2E (`npm run e2e:ci`) against the staging URL.
4. On green, promote to production.

Watch the Actions tab until the production deploy step succeeds. **If the Playwright E2E fails on staging, STOP** — do not patch the test or push through. Surface the finding to the slice owner. (This is FR-011's rule; the slice does not modify the test suite.)

### A.4 Step 3 — Verify User Story 1 (end-to-end chat flow on deployed SWA)

Open `https://green-mud-037bbdc1e.5.azurestaticapps.net` in a browser with DevTools open on the Network tab. Clear the tab.

1. Open the chat UI. Observe one request to `.../api/chat/start`. **Verify** the request's host is `movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io`, status is `200`, and the response body contains a non-empty `chatId`.
2. Send a chat message. Observe the `.../api/chat/{chatId}/ask` request. **Verify** same host, status `200`, non-empty model response rendered in the UI.
3. Send one more message on the same session. **Verify** `chatId` continuity (the second `ask` uses the same `chatId` the UI received from step 1).
4. Scan the whole Network tab for the full session. **Verify** zero requests target `yh5ax4ws5lr3k-func-movie-tracker.azurewebsites.net`.

Pass criterion: all four verifications green. If any fails, STOP and roll back (step A.7).

### A.5 Step 4 — Verify User Story 2 (CORS parity set, no wildcard)

Run three preflight checks (PowerShell shown; works from any OS with `curl`):

Preflight from the production SWA origin:

```powershell
curl.exe -i -X OPTIONS `
  -H "Origin: https://green-mud-037bbdc1e.5.azurestaticapps.net" `
  -H "Access-Control-Request-Method: POST" `
  https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat/start
```

Preflight from the Angular dev origin:

```powershell
curl.exe -i -X OPTIONS `
  -H "Origin: http://localhost:4200" `
  -H "Access-Control-Request-Method: POST" `
  https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat/start
```

Preflight from an off-list origin:

```powershell
curl.exe -i -X OPTIONS `
  -H "Origin: https://evil.example.com" `
  -H "Access-Control-Request-Method: POST" `
  https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat/start
```

Pass criteria:

- First two responses include `Access-Control-Allow-Origin: <that exact origin>` (not `*`).
- Third response does not include an `Access-Control-Allow-Origin` header for `evil.example.com`.

If any check fails, STOP and investigate before proceeding to step 5.

### A.6 Step 5 — Stop the Function App (last)

Only after steps 3 and 4 pass.

Resolve the Function App's hosting subscription and resource group (do not hard-code; see spec FR-005). Use Azure Resource Graph, which spans every subscription the current identity can read:

```powershell
az graph query `
  -q "Resources | where type == 'microsoft.web/sites' and name == 'yh5ax4ws5lr3k-func-movie-tracker' | project id, name, subscriptionId, resourceGroup, location" `
  -o json
```

If the query returns at least one row, record the `id` (the full resource ID). Use it as the single scope argument on every subsequent command:

```powershell
$functionAppId = "<the id from the az graph query result>"

az functionapp show --ids $functionAppId --query state
```

Expected: `"Running"`. If the value is already `"Stopped"`, the stop was already performed by prior work; record "already stopped" per FR-005 and skip the stop command. Otherwise:

```powershell
az functionapp stop --ids $functionAppId

az functionapp show --ids $functionAppId --query state
```

Expected after the stop: `"Stopped"`. Then confirm the public hostname no longer serves chat:

```powershell
curl.exe -i https://yh5ax4ws5lr3k-func-movie-tracker.azurewebsites.net/api/chat/start
```

Expected: 403, 503, or a connection error — not a `200` with a `chatId`.

Then re-run step 3 to confirm the SWA still works end to end (because it already targets the Container App).

If the `az graph query` returns zero rows, the Function App is not visible under any subscription the current identity can read. Record that outcome and surface it to the slice owner (per FR-005). Do **not** treat "not found" as "already stopped."

### A.7 Rollback (if any step fails)

Full rollback (every step applied in reverse of what has run so far):

1. If step 5 ran and the Function App is `Stopped`: `az functionapp start --ids $functionAppId`.
2. If step 2 ran and the frontend `chatBaseUrl` was changed: in `movie-tracker-app`, `git revert` the URL commit on `main` and push. The push fires the same SWA workflow (build → staging → E2E → promote) and restores the Function App URL in the deployed bundle. Verify in DevTools that the browser is hitting the Function App again.
3. If step 1 ran and the Container App CORS was tightened: open a revert PR on `006-cutover` (or on `main` if the CORS change already merged) that puts `allowedOrigins` back to `['*']`. Merging the revert PR fires the existing Bicep deploy workflow, which re-applies the wildcard allowlist. Verify with `az containerapp show ... --query 'properties.configuration.ingress.corsPolicy.allowedOrigins'`.

Each rollback step is an independent revert; apply only the ones whose forward step has run.

---

## B. Local-run guide (ng serve against the deployed Container App)

Per FR-012: run the Angular frontend locally against the deployed Container App. The authoring of this guide (the Markdown you are reading) is entirely inside `specs/006-cutover/**`; the runbook below requires one edit to the frontend repo (the same `chatBaseUrl` swap FR-001 already specifies), not a new one.

### B.1 Prerequisites

- Node.js 24.x (the SWA workflow uses `actions/setup-node@v4` with `node-version: '24.x'`).
- `movie-tracker-app` repo cloned.
- Container App CORS already deployed per section A.2 (so `http://localhost:4200` is on the allowlist).

### B.2 The single frontend edit

Same edit as A.3, applied on your local branch of `movie-tracker-app` — change the one property in `src/enviroments/enviroments.ts`:

```ts
chatBaseUrl: 'https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat',
```

If you want to run locally **and** keep `main` on the Function App until the cutover ships, do this edit on a feature branch of the frontend repo. If `main` has already landed the swap, no further edit is needed.

### B.3 Run the dev server

From the `movie-tracker-app` directory, install once (the `--legacy-peer-deps` flag matches the SWA workflow's install step), then start the dev server:

```powershell
npm ci --legacy-peer-deps
npm start
```

`npm start` runs `ng serve` (equivalent to `npx ng serve` if you prefer calling it directly), which binds to `http://localhost:4200` — Angular's default, confirmed in `package.json`'s `e2e:local` script and in the absence of any port override in `angular.json`.

### B.4 Verify the local app is hitting the Container App

Open `http://localhost:4200` in a browser with DevTools' Network tab open.

1. Start a chat. The Network tab shows a request to `movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat/start`. The browser sends `Origin: http://localhost:4200` on the preflight. The response includes `Access-Control-Allow-Origin: http://localhost:4200`. Status is `200`.
2. Send a message. Same host, status `200`, UI renders a response.

If the preflight is rejected (`Access-Control-Allow-Origin` absent), the Container App's CORS parity set was not deployed. Go back to section A.2.

### B.5 Why no proxy, no CORS workaround, no local env file

- The Container App's CORS allowlist already includes `http://localhost:4200` (FR-003), so the browser's preflight passes without any workaround.
- No `proxy.conf.*` is introduced (confirmed to not exist today; FR-002 forbids introducing one).
- No per-environment file split is introduced (confirmed single `enviroments.ts`; FR-002 forbids introducing one).

The slice's design intent is: the same `enviroments.ts` the frontend ships, run from `ng serve`, is enough to drive the deployed Container App locally.
