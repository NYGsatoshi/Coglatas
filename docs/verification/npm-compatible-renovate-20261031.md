# Compatible npm update qualification — 2026-10-31

## Scope and historical failure

Existing Renovate PR #1569 proposed only the following five manifest/lockfile paths: `coglatas-frontend/package-lock.json`, `frontend/package.json`, `frontend/package-lock.json`, `tools/frontend-inspections/package.json`, and `tools/frontend-inspections/package-lock.json`. It does **not** propose a Node.js runtime/major upgrade, .NET/Ubuntu migration, deployment, or a SecurityTestImplimation-branch integration.

The provider HEAD `f561793cae4a5f76cfb647f7e30325bff9fb48ea` (no maintainer changes) had PR CI [41188046808](https://github.com/NYGsatoshi/Coglatas/actions/runs/41188046808): the frontend producer/build and other 18 workflows were successful (three conditional workflows skipped), but PR CI failed because the `frontend-test` job `124499796104` ran the immutable ANG22-06 exact-version guard expecting `@lucide/angular@1.64.0` while the new manifest/lock provided `1.66.0`. Its 63/64 helper tests passed, and the original failure is retained. The existing frontend test output cannot certify the changed current HEAD; do not inherit a prior PASS.

## Exact dependency cohort

| Dependency and surface | Previous | Candidate |
| --- | --- | --- |
| `@lucide/angular` in `frontend` | 1.64.0 | 1.66.0 |
| `tslib` in `frontend` and the sibling frontend lockfile | 2.8.1 | 2.9.1 |
| `eslint` in `tools/frontend-inspections` | 10.13.0 | 10.14.0 |

The provider-generated npm package manifests/lockfiles are not rewritten by this maintainer repair. Its reviewed Lucide lockfile record names `1.66.0`, ISC, `tslib ^2.3.0`, and a SHA-512 integrity; the installed `tslib` lock record names `2.9.1` with SHA-512 integrity. Those are **lockfile declarations**, not independent npm archive/content authentication. The existing producer, dependency review, OSV, Trivy, Socket, audit, lint and Angular browser checks remain required on the new exact HEAD.

## Maintainer change and acceptance boundary

Update only the strict ANG22-06 expected `@lucide/angular` value from `1.64.0` to `1.66.0`, and explicitly add the actual `tslib@2.9.1` to the same strict dependency guard. Keep comparisons against manifest, package-lock root, installed-node version and SHA-512 metadata intact. No test, GitHub check, timeout, package lifecycle approval, exception, reviewer rule, or release gate is disabled. The existing Syncfusion version-specific installer allowlist and the package-wide denial for unreviewed versions remain unchanged.

The original provider-first CI failure is a real mismatch and is not declared flaky or retroactively green. Require new current-source PR CI and actual frontend/build, portable/backend, security and official Quality Gate outcomes before normal merge. A bot approval/old source analysis alone is not sufficient. After merge, verify Main independently and retain Student Council `RELEASE_NO_GO` until its separate Go/No-Go process qualifies it.

The owner's instruction to leave Node.js 26, .NET 11, Ubuntu 26.04 and SecurityTest-related PRs alone remains intact. This ordinary compatible-npm PR does not promote any of those candidates.
