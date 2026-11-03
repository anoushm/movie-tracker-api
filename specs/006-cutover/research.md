# Phase 0 Research: Function App to Container App Cutover

**Feature**: `specs/006-cutover` · **Branch**: `006-cutover` · **Date**: 2026-10-26

The spec contained no `[NEEDS CLARIFICATION]` markers after its Codex-approved round 4. The user's `/speckit-plan` input introduced three unknowns that planning resolves below.

---

## Unknown 1: What is the Function App's actual CORS allowed-origins list?

**Decision**: Use the user-mandated two-origin set — `['https://green-mud-037bbdc1e.5.azurestaticapps.net', 'http://localhost:4200']` — as the Container App ingress `corsPolicy.allowedOrigins` value. The set is user-specified for parity with the Function App's historical behaviour and is not independently verified against the Function App's live runtime configuration (see rationale 1–2 below).

**Rationale**:

1. **The Function App's Bicep declares no CORS at all.** `C:\projects\dev\movie-tracker-backend\infrastructure\func.bicep` defines the Function App's `siteConfig` with `appSettings` but no `cors` property. Azure's default for an undeclared `siteConfig.cors.allowedOrigins` is an empty list — i.e. the Bicep source of truth says no origins are allowed. If the frontend ever actually worked against the Function App (which the user's prior art implies it did, since the current `enviroments.ts` carries the Function App URL and the SWA has been live for months), then CORS must have been enabled via portal or `az functionapp cors add` outside Bicep — i.e. drift, with no reproducible declarative record.

2. **The Function App is not reachable from the operator's authenticated subscriptions.** `az resource list --name yh5ax4ws5lr3k-func-movie-tracker --resource-type Microsoft.Web/sites` and an `az graph query` against both the Pay-As-You-Go sub (`46c049aa-…`) and the Development subscription (`ba298da6-…`) under `AZURE_CONFIG_DIR=$env:USERPROFILE\.azure-personal` both return zero results on 2026-10-26. The Function App may have been retired by prior work, or it may live under a tenant the current session is not logged into. Either way, its runtime CORS cannot be read live in this session, so no "copy exactly what the live resource has" path exists.

3. **The empirical parity set is what the frontend actually needed.** The frontend consists of a production SWA at `https://green-mud-037bbdc1e.5.azurestaticapps.net` and an `ng serve` dev loop at `http://localhost:4200` (confirmed from `movie-tracker-app/package.json`'s `e2e:local` script, which hard-codes `PLAYWRIGHT_BASE_URL=http://localhost:4200`, and from Angular 22's `@angular/build:dev-server` defaulting to `:4200`). No other origin has ever served this frontend. The user's `/speckit-plan` input confirms this expectation ("the Static Web App and any localhost dev origin it allows").

4. **The user's directive is explicit on scope.** "No wildcard. No new origins." The parity set has exactly these two origins and no third speculative entry (no preview SWA pattern, no `127.0.0.1`, no `0.0.0.0`, no other front-end domain). Preview SWA environments remain blocked by CORS and that is documented as accepted in the spec's Edge Cases.

**Alternatives considered**:

- **Leave CORS at `['*']` until the Function App is reachable to read its runtime value**: rejected. The user's P2 user story is explicit — the wildcard goes. Blocking the slice on an un-readable resource that may already be stopped would hold the cutover hostage to a dead system.
- **Materialize CORS to the single production SWA origin and leave `http://localhost:4200` out**: this is what the original spec said. Rejected under the user's refined `/speckit-plan` input which explicitly asked for FA parity "including the Static Web App and any localhost dev origin it allows." A local-dev origin is practically required for a functioning dev loop (US4-style, now folded into FR-012's local-run guide); without it, every `ng serve` developer must either disable browser CORS or add an ingress exception, both of which defeat the "parity, no new origins" posture.
- **Materialize CORS to a wildcard subdomain pattern (`https://*.5.azurestaticapps.net`)**: rejected per FR-004.
- **Pin the actual Function App Bicep's declared value (empty) and declare that FA parity means empty**: rejected. Materialising an empty allowlist would block both the production SWA and localhost from calling the API; it would reproduce a broken state, not parity with the historically working one.

---

## Unknown 2: Is the frontend's API base URL baked at build time, or evaluated at runtime?

**Decision**: Baked at build time. A `chatBaseUrl` edit requires a Static Web App redeploy via the existing `.github/workflows/azure-static-web-apps-green-mud-037bbdc1e.yml` workflow before User Story 1 can be verified against the deployed site. FR-011 captures this as a slice requirement; the operator triggers the redeploy by pushing the edit to `main` in the frontend repo.

**Rationale**:

1. The URL lives in `movie-tracker-app/src/enviroments/enviroments.ts`, a TypeScript module imported directly by `src/app/services/chat.service.ts`. Angular's `@angular/build:application` builder (seen in `angular.json`'s `architect.build.builder`) bundles source modules into static assets at build time; there is no server-side runtime indirection on an SWA.

2. There is no `proxy.conf.*` or `src/proxy*.ts` (confirmed by Glob on 2026-10-26). The browser issues a direct cross-origin request to whatever host `enviroments.ts` compiled into the bundle.

3. The SWA workflow runs `npm run build` which runs `ng build`, producing `dist/movie-tracker-app/browser` which the `Azure/static-web-apps-deploy@v1` action uploads. The URL constant is in the bundle; a redeploy is the only way to change it for end users.

4. The workflow already includes a staging-slot deploy + Playwright E2E on `playwright.config.ci.ts` + production promotion. The URL change rides those existing gates untouched. If the existing E2E suite's assumptions turn out to be Function-App-shaped (e.g., it checks a specific error string the Function App produced), promotion is blocked and the operator surfaces that — see FR-011.

**Alternatives considered**:

- **Introduce a runtime-config mechanism** (fetch `/config.json`, `window.__env`, inject via SWA app settings): rejected explicitly in the spec's Out-of-Scope section and the user's "no new config" rule.
- **Edit the dist directly and manual-upload**: rejected — bypasses the SWA workflow's E2E gate.

---

## Unknown 3: What Azure resource / subscription hosts the Function App, and how is User Story 3's `az functionapp stop` targeted?

**Decision**: Deferred to User Story 3 execution time. The slice specifies that the operator resolve the Function App's hosting subscription and resource group through an authenticated search at the moment User Story 3 is executed, rather than hard-coding one here. If resolution fails across every available login, the operator surfaces that outcome to the slice owner and does not treat "not found" as "already stopped." Both behaviours are already codified in the spec's FR-005 and User Story 3's Independent Test; this plan does not pre-resolve the location.

**Rationale**:

1. **The Function App is not visible in the sessions currently authenticated.** Confirmed on 2026-10-26 via (a) `az resource list` under both `46c049aa-…` (Pay-As-You-Go) and `ba298da6-…` (Development subscription), and (b) an `az graph query` against both. Both return zero `Microsoft.Web/sites` whose name matches. The user may have another tenant / another login that holds the Function App; the slice does not pre-commit to one.

2. **Hard-coding a subscription here would re-introduce the exact bug Codex round 1 flagged.** The spec's prior wording hard-coded Pay-As-You-Go; round 1 rejected it on the grounds that "not found there" cannot be re-interpreted as "already stopped." The spec is now resolution-first and the plan preserves that.

3. **No new tooling is introduced to find the Function App.** The spec's `az resource list` / `az graph query` suggestions, run under the operator's `AZURE_CONFIG_DIR`, are the recovery path. If the Function App has in fact been deleted by prior cleanup (which looks plausible given its invisibility in both reachable subs), User Story 3 reduces to a verification-only step and the Function App stays deleted; the slice does not undo that nor pretend to.

**Alternatives considered**:

- **Attempt to discover the Function App via DNS**: `https://yh5ax4ws5lr3k-func-movie-tracker.azurewebsites.net/` either resolves or does not. If it resolves and returns a non-stopped response, the hosting is somewhere; however, the Function App could reach the public DNS even in a tenant the operator cannot see, so DNS presence is informational rather than directive. Rejected as the primary resolution mechanism; the operator may use it as a smoke test.
- **Issue a cross-tenant resource query**: requires additional login scope the slice does not take. The operator can do this ad-hoc under their own identity.

---

## Side observations (do not change the plan; recorded so the next slice does not re-discover them)

- **The SWA workflow deploys to a staging slot first and runs Playwright E2E before promoting to production.** This slice does not add E2E tests, but the existing suite will run on staging after the URL commit. If a Function-App-shaped assertion fails there, promotion is blocked. Not this slice's problem to patch, but worth knowing before the operator starts the redeploy.
- **The frontend has a known typo in the directory name**: `src/enviroments/` (missing the second `n`), confirmed by `Glob`. The spec and plan reference the real path; no rename is attempted.
- **The SWA workflow strips a region segment from the staging URL** (`.westus2.5.azurestaticapps.net` → `.5.azurestaticapps.net`) because the Azure action still emits a deprecated form. This has no effect on the cutover but confirms that `*.5.azurestaticapps.net` (not `*.<region>.5.azurestaticapps.net`) is the current SWA hostname shape.
- **The Function App's declared Bicep has `WEBSITE_RUN_FROM_PACKAGE=1`, `use32BitWorkerProcess: false`, `netFrameworkVersion: 'v10.0'`, Y1 Consumption plan.** None of this needs to change for the stop; recorded for the follow-up slice that will eventually retire the Function App's Bicep from the backend repo.
