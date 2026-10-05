# Frontend v0.4 PR02 — Syncfusion license activation evidence

Status: implementation evidence recorded; a real-secret activation result is
required only from protected local/CI/deployment execution.

## Acceptance mapping

| Requirement | Evidence | Status |
| --- | --- | --- |
| No Angular runtime registration or browser secret path | `main.ts` no longer bootstraps a license service; the runtime bootstrap service and tests were removed. `test:syncfusion-license` rejects runtime `registerLicense()` calls. | Passed locally |
| Licensed build fails closed | `npm run syncfusion:activate` validates `SYNCFUSION_LICENSE` before invoking the Syncfusion CLI; `build:licensed` chains activation before Angular build. | Passed locally for unset and whitespace-only values |
| Hosted publish path is licensed | `BuildAngularFrontendOnPublish=true` invokes `build:hosted:licensed`, rather than the regular hosted-build script. | Source reviewed |
| Standard fallback build remains usable | `npm run build` remains separate from the licensed-build gate. | Passed locally |
| Secret is outside Git and Docker context | `.gitignore` excludes `.env` and `.env.*` while retaining `.env.example`; `.dockerignore` excludes both patterns; Git tracking check permits only `.env.example`. | Passed locally |
| Docker transfer is non-persistent | Root Dockerfile uses a required BuildKit secret; it has no `ARG` or `ENV` declaration for the license and rejects a raw-secret match in frontend build output without printing it. Compose build definitions map only `syncfusion_license`. CI inspects the final runtime image for secret mounts, runtime environment, `.env*`, and `syncfusion-license.txt`. | Static/Compose validation passed locally; protected CI run pending |
| CI uses only the secret name | CI and the manual real-backend-smoke workflow reference `secrets.SYNCFUSION_LICENSE`; neither prints it. Docker CI uses an owner-only `mktemp` file with an EXIT trap. | Source reviewed |
| Feature flag remains separate | `CoglatasSyncfusionAdapterRegistry` still decides only through the existing rollout flags. It no longer accepts a license value or calls a registrar. | Angular unit tests passed |

## Commands and results

| Command | Result |
| --- | --- |
| `npm --prefix frontend ci` | Passed; lockfile accepted without modification. |
| `npm --prefix frontend run check:architecture` | Passed. |
| `npm --prefix frontend run test:architecture` | Passed (2 tests). |
| `npm --prefix frontend run test:syncfusion-license` | Passed (3 tests). |
| `npm --prefix frontend run syncfusion:activate` with the variable unset | Failed closed with the fixed message; CLI was not invoked. |
| `npm --prefix frontend run syncfusion:activate` with whitespace only | Failed closed with the fixed message; CLI was not invoked. |
| `npm --prefix frontend run build` | Passed; existing initial-bundle budget warning remains. |
| `npm --prefix frontend run test` | Passed (190 tests). |
| `docker compose ... config --quiet` for root, local, on-prem, and real-backend-smoke profiles | Passed. |
| Docker `ARG`/`ENV`, `.dockerignore`, tracked `.env*`, and source scan checks | Passed. |

No real license value was requested, generated, supplied, or recorded for this
evidence. Consequently, real CLI activation and a final-image scan after a
licensed Docker build are intentionally delegated to the protected CI secret
run; the CI workflow makes both mandatory.

## Canonical specification status

`DECISION REQUIRED` — the authoritative specification repository is separate
from this repository. Its current PR02 kickoff document,
`docs/specs/coglatas-core-v4/12-implementation-kickoff/frontend-v0.4-pr02-syncfusion-adapter-foundation-prompt.md`,
requires an approved runtime/bootstrap secret path. That conflicts with this
task's required build-time-only CLI activation. The requested canonical
replacement is:

```text
Secret / environment variable: SYNCFUSION_LICENSE
Registration: Build-time activation using Syncfusion License CLI
Production source: Git-managed external .env or CI secret
Docker transport: BuildKit secret
Runtime exposure: Not permitted unless technically required and explicitly documented
```

This implementation does not modify the separate canonical-spec repository or
claim that its decision has been superseded. A maintainer must apply the
replacement there before treating the canonical document as aligned.

## PR evidence text

Use the following sanitized items in the PR body:

- `SYNCFUSION_LICENSE` is the only secret name; no value is included.
- Licensed builds use `npx syncfusion-license activate` before `ng build` and
  fail closed when the variable is unset or whitespace-only.
- Docker receives the secret only through BuildKit; the runtime image receives
  neither the variable nor environment/secret files.
- Feature flags remain rollout controls and are not license bypasses.
- Record the protected CI run URL and its final-image exclusion check without
  copying CI logs that could contain sensitive diagnostics.
- Include the canonical-spec `DECISION REQUIRED` above until its owner updates
  the source specification.
