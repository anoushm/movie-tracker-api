# Implementation Plan: Function App to Container App Cutover

**Branch**: `006-cutover` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-cutover/spec.md`

## Summary

Primary requirement: retire the Function App `yh5ax4ws5lr3k-func-movie-tracker` as the Angular frontend's chat backend by (1) pointing `movie-tracker-app`'s `chatBaseUrl` at the deployed Container App FQDN and letting the Static Web App's existing deploy workflow redeploy it, (2) tightening the Container App ingress CORS allowlist from `['*']` to the Function App's historical origin set `['https://green-mud-037bbdc1e.5.azurestaticapps.net', 'http://localhost:4200']` through a Bicep edit on `infrastructure/container-app.bicep`, (3) producing a local-run guide for `ng serve` against the Container App, and (4) stopping (not deleting) the Function App last, only after end-to-end verification of (1) and (2). The slice adds no new endpoints, config keys, env vars, telemetry, tests, or CI jobs — it is a URL swap, a CORS allowlist swap, a doc page, and an operational `az` command, ordered.

Technical approach: all three in-scope state changes happen through pre-existing deploy paths — the backend's GitHub Actions Bicep workflow for the CORS change, the frontend's existing `azure-static-web-apps-green-mud-037bbdc1e.yml` workflow for the URL change (which builds with `ng build`, deploys to the staging slot, runs its existing Playwright E2E, and promotes to production on green), and the operator's own `az functionapp stop` for the final step. No new code paths are introduced in either repository; the only edits are one line of TypeScript in `movie-tracker-app/src/enviroments/enviroments.ts`, one Bicep array in `infrastructure/container-app.bicep`, and the Spec Kit artefacts under `specs/006-cutover/`.

## Technical Context

**Language/Version**: .NET 10 (API, unchanged by this slice) · TypeScript 6 / Angular 22 (frontend, one-line edit) · Bicep (infrastructure, one-array edit)

**Primary Dependencies**: ASP.NET Core (API, unchanged) · `@angular/*` 22 (frontend, unchanged version) · `Microsoft.App/containerApps@2024-03-01` ingress corsPolicy (Bicep)

**Storage**: N/A for this slice. Cosmos data on either side is explicitly out of scope per the spec.

**Testing**: Manual verification per the spec's three Independent Tests (DevTools Network tab, `curl -X OPTIONS` preflights, `az functionapp show`). The frontend repo's existing Playwright E2E in the SWA workflow runs as-is against the new backend; no test code is added or modified.

**Target Platform**: Azure Container Apps (API, `westus3`) · Azure Static Web Apps (frontend, default region) · Azure Functions Consumption (legacy, being retired).

**Project Type**: Infrastructure + frontend cutover slice. Two repositories touched: this one (`movie-tracker-api`) for Bicep + spec artefacts, and `movie-tracker-app` for the one-line URL swap. The backend repo (`movie-tracker-backend`) is not touched.

**Performance Goals**: N/A for this slice. The Container App's existing probes and scaling rules are unchanged.

**Constraints**:
- No new features, config keys, env vars, endpoints, telemetry, tests, or CI jobs.
- No wildcard in `allowedOrigins` after the cutover; exactly two parity entries.
- No deletion of the Function App or any of its satellite resources.
- No edit to `.gitignore` in either repo.
- No touch of the five known-untracked local files in `movie-tracker-app` (`.claude/`, `.github/copilot-instructions.md`, `copilot-instructions.md`, `movietracker-demo.mp4`, `test-results/`).
- Working-tree line endings on Windows: CRLF per constitution.

**Scale/Scope**: Smallest possible. Three state changes, three evidence collections, one `az` command.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Compliance | Notes |
|---|---|---|
| I. Managed Identity Over Secrets | PASS | Slice touches no identity or secret surface. Container App continues to use its user-assigned managed identity; no new RBAC, no new Key Vault reference, no new secret. |
| II. Infrastructure Is Bicep, Not Clicks | PASS | The CORS change is a Bicep edit (`infrastructure/container-app.bicep`), deployed via the existing GitHub Actions workflow. The Function App `stop` is explicitly an operational lifecycle action, not a topology change — the Function App's Bicep lives in the backend repo and this slice does not touch it. The spec's FR-005 names `az functionapp stop` as the stop mechanism; this is a lifecycle verb (reversible with `az functionapp start`), not an imperative mutation of a declared property. |
| III. Incremental Migration With Declared Boundaries | PASS | spec.md carries an explicit Out-of-Scope list of ~20 items and three user stories whose shapes are tightly bounded. The slice is complete when the frontend talks to the Container App end-to-end, CORS is parity-tightened, and the Function App is stopped. |
| IV. Dependencies Prove Themselves | PASS | No new external dependency is taken. The existing `/health/*` probes already prove Cosmos, TMDb, and OpenAI; this slice does not add a probe for CORS or for the Function App's stopped state (manual verification per the spec covers both). |
| V. No Secret Material In Source | PASS | The two parity origins and the Container App FQDN are not secrets. The frontend's `enviroments.ts` already contains only non-secret URLs. |
| Azure Platform Constraints | PASS | Runtime (.NET 10, port 8080), hosting (ACA Consumption, single active revision), identity (one user-assigned managed identity), Key Vault RBAC-only, resource naming without `uniqueString()`, and the `/health` + `/health/ready` cheap-and-dependency-free contract are all unchanged. |
| Development Workflow — Codex cross-review at each stage | PASS (planned) | FR-010 names all four stages; this plan triggers the plan-stage review at the end, max 6 rounds. |
| Development Workflow — Out-of-scope list on every spec | PASS | spec.md's Out of Scope section lists ~20 explicit exclusions and the reviewer rejects crossings. |
| Development Workflow — Bicep validation & what-if before merge | PASS | SC-006 names both jobs. A what-if showing an unexpected delete or replace blocks the merge. |
| Development Workflow — Deployment ordering as Bicep dependencies | EXCEPTION (justified in Complexity Tracking) | The slice's operational ordering (CORS deploy → frontend commit → SWA workflow → verify US1 & US2 → `az functionapp stop`) crosses three control planes (this repo's Bicep workflow, the frontend repo's SWA workflow, the operator's `az` CLI) and cannot be expressed as a single Bicep dependency chain. The exception is user-directed (FR-014); no deployment-script or orchestration resource is introduced to force a single-plane model. |
| Development Workflow — CRLF on Windows | PASS | All new files written through this plan use repository line-ending defaults; Git's warning `LF will be replaced by CRLF` on the initial spec commit confirms the attribute is in effect. |

**Verdict**: PASS with one explicit, user-directed exception on cross-pipeline ordering, detailed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/006-cutover/
├── spec.md                 # /speckit-specify output, Codex-approved round 4 on 2026-10-26
├── plan.md                 # This file (/speckit-plan output)
├── research.md             # Phase 0: resolved unknowns (/speckit-plan output)
├── data-model.md           # Phase 1: entity-shape notes (/speckit-plan output)
├── quickstart.md           # Phase 1: deployed verification + local-run guide (/speckit-plan output)
├── checklists/
│   └── requirements.md     # /speckit-specify quality checklist
└── tasks.md                # /speckit-tasks output (NOT created by /speckit-plan)
```

No `contracts/` subdirectory is produced: this slice introduces no new API, CLI, or interface. The Container App's existing endpoints are the API contract and they are unchanged.

### Source Code (edits this slice makes, across both repositories)

```text
# Repository 1: movie-tracker-api (this one, branch 006-cutover)
infrastructure/
└── container-app.bicep     # One edit: ingress.corsPolicy.allowedOrigins
                            #   ['*'] -> ['https://green-mud-037bbdc1e.5.azurestaticapps.net',
                            #            'http://localhost:4200']

specs/006-cutover/          # The six Spec Kit artefacts above
├── spec.md                 # Modified during /speckit-plan to add FR-011..FR-014
├── plan.md                 # This file (new)
├── research.md             # New
├── data-model.md           # New
├── quickstart.md           # New
└── checklists/requirements.md  # Unchanged

# Repository 2: movie-tracker-app (frontend; main branch; outside this diff)
src/enviroments/
└── enviroments.ts          # One edit: chatBaseUrl
                            #   'https://yh5ax4ws5lr3k-func-movie-tracker.azurewebsites.net/api/chat'
                            #   -> 'https://movie-tracker-api.politeocean-1c0085f8.westus3.azurecontainerapps.io/api/chat'

# Repository 3: movie-tracker-backend (Function App bicep; NOT touched by this slice)
# Repository 4: engineering-playbook (shared playbook; NOT touched by this slice)
```

**Structure Decision**: Four Spec Kit artefacts (`plan.md`, `research.md`, `data-model.md`, `quickstart.md`) are written to `specs/006-cutover/`. One Bicep file is edited (`infrastructure/container-app.bicep`). One frontend file is edited in the sibling `movie-tracker-app` repository (`src/enviroments/enviroments.ts`). The frontend edit is captured here as a documented step; the actual commit happens in the frontend repository on its `main` branch (per the user's instruction), and its deploy pipeline is unchanged. No files are added or edited outside this list.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| FR-014's operational ordering is a cross-pipeline sequence (CORS Bicep deploy → frontend commit on `main` → SWA workflow → verify US1 & US2 → `az functionapp stop`), not a Bicep dependency chain. | The three state changes live in three different control planes: this repo's Bicep workflow, the frontend repo's SWA workflow, and the operator's local `az` CLI. No single Bicep deployment can express the ordering across them. The constitution's "deployment ordering as Bicep dependencies" rule assumes one deployment plane; the user explicitly directed a cross-plane sequence. | Introducing a deployment-script resource or orchestration pipeline to collapse the ordering into one plane adds surface area (new resource, new secret for the SWA deploy token) that this slice explicitly rules out. The FA stop cannot be a Bicep property change because the FA's Bicep lives in `movie-tracker-backend`, out of this slice's scope. |

## Phase 0: Outline & Research

Research deliverable: `research.md`. The spec contained no `[NEEDS CLARIFICATION]` markers after its Codex-approved round 4, but the user's `/speckit-plan` input introduced three unknowns the planning stage resolves:

1. **What is the Function App's actual CORS allowed-origins list?** (FA parity depends on it.)
2. **Is the frontend's API base URL baked at build time, or evaluated at runtime?** (Determines whether an SWA redeploy is required.)
3. **What Azure resource / subscription hosts the Function App?** (Determines where User Story 3's `az functionapp stop` is targeted.)

Research outcomes are consolidated into `research.md`. Unknowns 1 and 2 are resolved there; Unknown 3 (Function App location) is explicitly deferred to User Story 3 execution time, with the resolution procedure codified in spec FR-005 and quickstart section A.6. No `[NEEDS CLARIFICATION]` markers remain in any artefact.

## Phase 1: Design & Contracts

**Prerequisites**: `research.md` complete.

Deliverables:

1. **`data-model.md`**: this slice does not introduce new entities. The four already-documented in `spec.md#Key Entities` (chat API base URL, Container App ingress CORS allowlist, SWA production host, Function App to retire) carry forward; the data model file names them explicitly as "no new entities" with cross-references.
2. **`contracts/`**: not created. This slice adds no new API, CLI, library, or UI contract. The Container App's existing chat endpoints — `GET /api/chat/start`, `POST /api/chat/{chatId}/ask`, `POST /api/chat/{chatId}/ask/v2` — are the inherited contract and are not modified. The frontend's chat service consumes them unchanged.
3. **`quickstart.md`**: a two-section guide. Section A: deployed verification (the three Independent Tests consolidated into one operator runbook, in the FR-014 ordering). Section B: local-run guide (per FR-012: `ng serve` against the Container App using the same URL and the parity CORS set, with the single frontend edit and verification steps).

Re-evaluated Constitution Check post-Phase 1: unchanged. The one tracked caveat (cross-pipeline ordering) is unchanged by the Phase 1 artefacts, which only document; they do not introduce new resources or interfaces.
