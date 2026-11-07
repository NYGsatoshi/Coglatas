# Ubuntu 26.04 hosted-runner qualification (Draft-only)

## Intent and change boundary

This is an isolated Ubuntu LTS compatibility experiment for Coglatas. It **does not migrate production or change the canonical Main CI runner selection**, production Docker base images, package-manager versions, dependency pins, deployment configurations, or required checks. Do not merge this Draft PR until the Owner separately authorizes an implementation plan and verifies complete evidence.

GitHub announced that `ubuntu-26.04` is generally available for GitHub-hosted x64/arm64 workflows as of 2026-09-17, and `ubuntu-latest` will gradually switch from 24.04 to 26.04 between 2026-10-19 and 2026-11-19. An explicit `ubuntu-24.04` baseline is necessary because `ubuntu-latest` is not a stable comparison control.

Sources:
- https://github.blog/changelog/2026-09-17-ubuntu-26-generally-available-and-latest-migration/
- https://github.com/actions/runner-images

## Experiment design

The new workflow `.github/workflows/ubuntu26-qualification.yml` executes on the Main-targeted Draft pull request, when its small allowlisted change set is present. It also declares `workflow_dispatch`, but a new workflow generally cannot be manually dispatched from the Actions UI until it exists on the default branch. There is no `push` hook and the job receives only `contents: read`.

The matrix has exactly two hosted Linux x64 legs, run against the same PR merge revision with `fail-fast: false`:

| Control | GitHub runner label | Purpose |
| --- | --- | --- |
| Baseline | `ubuntu-24.04` | Current known OS line, pinned to avoid `ubuntu-latest` migration |
| Candidate | `ubuntu-26.04` | New Ubuntu 26.04 LTS hosted image |

Both legs use the identical repository-pinned .NET SDK from `global.json`, Node 24, npm 11.17.0, and reviewed immutable GitHub Action SHAs. This intentionally isolates the OS change from the separate Node 26 and .NET 11 investigations.

Each leg performs:
1. Root `npm ci`, existing OS-portability static/negative tests, new Ubuntu-qualification negative tests, and runtime toolchain contract verification.
2. Frontend `npm ci` with lifecycle scripts disabled, solution restore/build in Release, `Portability=CrossPlatform` .NET tests with strict TRX verification (minimum 25, zero skipped or failed).
3. Production Angular build, Angular unit tests, frontend architecture tests, and COMPAT-04 browser *discovery* validation.
4. A Docker Engine/Compose availability probe (**not** a Docker image build or PostgreSQL integration test).
5. Best-effort sanitized identity, image-version, toolchain, step-outcome and TRX evidence capture, plus per-OS attempt-specific upload even following failures. If checkout or runtime setup fails, missing evidence is a failure, not a pass.

Each execution is bounded to 75 minutes per OS and two parallel jobs. The workflow is non-required and does not replace any protected PR check.

## Runner-security and lint interpretation

The runner expression can produce only the two explicitly allowlisted hosted runner labels. Its *exact* value is registered in `governance/workflow-trust-policy.json`, with negative-control tests rejecting arbitrary runner selection, omitted tests, unlocked actions, persisted checkout credentials, write permissions, and suppressed evidence. It does **not** use a self-hosted runner.

The repository-pinned actionlint v1.7.12 may not recognize a literal `ubuntu-26.04` label even though GitHub now supports it. The bounded static expression avoids adding a misleading `self-hosted-runner` label workaround; trust policy and tests verify its only possible outputs. The existing actionlint and workflow-trust checks must still pass.

## Evidence and completion criteria

For each of the two OS legs, inspect the GitHub Actions outcome, image identity/version, SHA, run attempt, first failing step, .NET TRX, and `ubuntu26-qualification-<label>-<attempt>` artifact. No job result is pre-judged. Classify differences as new Ubuntu compatibility failures, flaky/nonreproducible infrastructure issues, or pre-existing failures only after independent comparison. Missing evidence and skipped checks are unresolved, not PASS.

The Draft PR may be considered an initial **hosted-runner compatibility signal** only after both legs pass at the same tested revision. Full Ubuntu 26.04 migration readiness additionally requires separate, exact-head qualification of canonical PostgreSQL/service tests, real-backend browser tests, security tooling (including Schemathesis/ZAP), container image builds, actual deployment/rollback, and any OS-specific apt/native library dependencies. This experiment does not establish those properties.

Existing `ubuntu-latest` jobs are left untouched in this Draft. A separate reviewed migration PR must handle any required pinning or broader runner replacements and preserve all mandatory checks. No release or Main merge is authorized by this document.
