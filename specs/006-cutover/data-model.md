# Phase 1 Data Model: Function App to Container App Cutover

**Feature**: `specs/006-cutover` · **Branch**: `006-cutover` · **Date**: 2026-10-26

## Entities introduced by this slice

**None.** This is an operational cutover slice; no new persisted state, no new in-memory model, no new interface payload. The four entities the slice *touches* are already enumerated in `spec.md#Key Entities` and are reproduced below only as a cross-reference so a reviewer does not need to flip tabs.

| Entity (from `spec.md`) | What it is | Pre-cutover value | Post-cutover value | Where it lives |
|---|---|---|---|---|
| Chat API base URL (`chatBaseUrl`) | Hardcoded string the frontend's chat service builds URLs on | `https://yh5ax4ws5lr3k-func-movie-tracker.azurewebsites.net/api/chat` | `https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat` | `movie-tracker-app/src/enviroments/enviroments.ts` |
| Container App ingress CORS allowlist | The `allowedOrigins` array under `properties.configuration.ingress.corsPolicy` on `Microsoft.App/containerApps/movie-tracker-api` | `['*']` | `['https://green-mud-037bbdc1e.5.azurestaticapps.net', 'http://localhost:4200']` | `infrastructure/container-app.bicep` |
| Static Web App production host | The `defaultHostname` of resource `movie-tracker-app` | `green-mud-037bbdc1e.5.azurestaticapps.net` | unchanged (read-only input) | `Rg-Movie-Tracker` under sub `46c049aa-…` |
| Function App to retire | A `Microsoft.Web/sites` resource whose state is this slice's one lifecycle target | `state: Running` (presumed) | `state: Stopped` | Resource group / subscription resolved at User Story 3 time |

## Validation rules

The spec's FR-003, FR-004, and FR-005 are the validation rules on the two state-changing entities. There is no new validation to invent; the plan simply references them.

## State transitions

| Entity | Transition | Mechanism | When |
|---|---|---|---|
| Container App ingress CORS allowlist | `['*']` → parity set | Bicep edit + GitHub Actions deploy workflow | Step 1 of FR-014's ordering |
| Chat API base URL | FA URL → CA URL | TypeScript edit + SWA workflow redeploy | Step 2 of FR-014's ordering |
| Function App | `Running` → `Stopped` | `az functionapp stop` with resolved scope | Step 5 of FR-014's ordering (last) |

No other state changes.
