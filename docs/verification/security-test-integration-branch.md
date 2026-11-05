# Security test integration branch — GitHub Actions-only roadmap

## Branch scope and authority

- Integration branch: `SecurityTestImplimation` (existing spelling is intentional).
- Baseline: `main` commit `a308fca5c43a810026b903c499550642a1439de4`.
- Work is staged on this branch and tested before a final reviewed PR to `main`.
- No production deployment, release admission, Student Council launch approval, RLS activation, security exception approval, or existing Required Check waiver is implied.
- `main`, branch protection, and the existing security tool configurations remain authoritative until each proposed change is independently verified.

## Implementation phases

| Phase | Scope | Acceptance evidence | State |
| --- | --- | --- | --- |
| SEC-CI-01 | Existing workflow, routing, trust and evidence inventory | Reviewed job and trigger matrix; test-branch exact-SHA evidence | In progress |
| SEC-CI-02 | Separate Trivy `misconfig` coverage | Report-only SARIF, validator mutation tests, GitHub-hosted scan | Candidate implemented; hosted verification pending |
| SEC-CI-03 | SCA/SBOM delta and coverage audit | Existing OSV/Trivy/Grype/Syft comparisons; no new false-green | Not started |
| SEC-CI-04 | Isolated authenticated API/tenant authorization coverage | Existing ZAP/Schemathesis contracts; verified negative tests | Not started |
| SEC-CI-05 | CodeQL/SAST delta and workflow guard optimization | Measurable scanner benefit, scan time, stable gate identity | Not started |
| SEC-CI-06 | Unified safe evidence and supply-chain verification | SHA-bound artifacts, sanitized summary, final Main PR evidence | Not started |

## Initial shadow control: SEC-CI-02

The new `security-misconfiguration.yml` complements rather than replaces `trivy-repository.yml`, which scans `vuln`. The new workflow scans `misconfig` for HIGH/CRITICAL configuration findings and intentionally does **not** fail because of finding counts while the baseline is being qualified. It runs on changes to IaC, Docker and workflow definitions on the integration branch, on PRs targeting it or `main`, and on future `main` pushes after merging.

The workflow uses only GitHub-hosted runners, read-only permissions, already approved pinned Actions, no repository secrets, no production network scanning, and no Code Scanning write permission. Scanner failures, missing files and malformed SARIF must fail closed. The summary shows a count and explicitly identifies the report-only status. A successful or empty report cannot be used as an ASVS-compliance or release-clearance claim.

### Required verification before promoting the scan

1. Pass `python3 -B tests/ci/test_security_misconfiguration_sarif.py` and parse/validate the new workflow.
2. Run existing publication, trust, required-check, and Actions pin validators on the candidate revision.
3. Confirm the exact integration-branch SHA and inspect the real GitHub Actions `security-misconfiguration-advisory` job and artifact.
4. Triage the actual findings; establish documented narrowly scoped exclusions rather than hiding them through a lowered severity threshold.
5. Evaluate PR scan runtime, artifact retention, and fork PR safety. Promote findings to blocking in a separate reviewed change only after false-positive and coverage qualification.

## Final merge conditions

Maintain source/target freshness, exact-SHA checks, no suppressed failure history, and all pre-existing owner holds. Merge the integration branch to `main` only after the agreed Security work is implemented, the candidate is reviewed, all required checks pass on the exact merge candidate, and the user authorizes the final merge.

## Security-integration scan trigger parity (SEC-CI-01 candidate)

The existing `SecurityTestImplimation` branch is the **test integration target**, not a release branch. On proposed security/CI changes, the following independent scans should run against pull requests targeting either `main` or `SecurityTestImplimation`, as well as integration-branch pushes: CodeQL (four existing languages), OSV baseline-versus-candidate scanning, Trivy repository vulnerability advisory, and actionlint/zizmor workflow analysis. The existing Trivy infrastructure misconfiguration shadow workflow already covers both branches.

The optimized Socket Firewall and the independent LegacyNameGuard also cover `SecurityTestImplimation` without weakening their fail-closed behavior.

The separate parity regression tests are kept in `tests/ci/test_security_integration_workflow_triggers.py` and are invoked by the existing GitHub Actions Static Analysis workflow. They guard against silent branch-filter regression without changing scan severities, token permissions, immutable Action pins, or release status checks.

This is only a candidate until the exact security-branch PR SHA passes GitHub-hosted CI. No Main deployment or automatic integration into Main is authorized.
