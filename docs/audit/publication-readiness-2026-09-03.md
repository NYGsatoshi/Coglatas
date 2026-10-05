# Public-visibility readiness audit — 2026-09-03

Repository: `NYGsatoshi/Coglatas`\
Base commit: `2b9adeacd5aaf31ebcdc2972976e7e81f3373755`  
Target posture: publicly visible source, **not** open source; unsolicited
contributions rejected; GitHub-hosted pull-request CI; licensed vendor
verification isolated behind owner approval.

## Executive result

**Status: BLOCKED pending completion of the manual cutover gates in
`docs/PUBLICATION_RUNBOOK.md`.**

The source-controlled remediations and automated checks described below are
implemented. This change set does not change repository visibility, rotate
credentials, rewrite Git history, delete historical Actions data, configure
GitHub rulesets, or make a legal or patent determination.

## Findings and remediation

| ID | Severity | Finding at audit start | Remediation in this change |
| --- | --- | --- | --- |
| PUB-01 | Critical | Active workflows routed repository-controlled code to persistent self-hosted runners. | All active workflow runner labels are migrated to GitHub-hosted `ubuntu-latest`; the guard rejects future `self-hosted` routing. |
| PUB-02 | Critical | Pull-request-capable workflows could reach `SYNCFUSION_LICENSE` for same-repository branches. | Licensed real-backend and image verification are consolidated into manual, protected-environment workflows. Public PR CI contains no secret references. |
| PUB-03 | High | Existing CI secret scanning used a working-tree-only Gitleaks invocation rather than a publication-specific full-history gate. | `Publication Readiness` checks out full history and runs redacted Gitleaks without `--no-git`. |
| PUB-04 | High | No repository-owned source-availability notice or explicit contribution policy existed. | Added `COPYRIGHT.md`, `CONTRIBUTING.md`, README notice, `UNLICENSED` package metadata, and third-party notices. |
| PUB-05 | High | No public security-reporting policy existed. | Added `.github/SECURITY.md` with private-reporting and authorized-testing boundaries. |
| PUB-06 | Medium | No Code Owner declaration existed for public governance files. | Added `.github/CODEOWNERS` assigning the repository owner. |
| PUB-07 | Medium | Publicization controls were not mechanically checked. | Added `scripts/ci/check-publication-readiness.py`, expanded regression tests, and a required workflow. |
| PUB-08 | Manual blocker | Historical commits, PRs, issues, logs, artifacts, releases, branches, tags, and external systems cannot be conclusively audited from a source-only pull request. | Documented mandatory manual inspection, deletion, and credential-rotation steps. |
| PUB-09 | Manual blocker | GitHub pull-request creation policy, rulesets, security features, environment reviewers, and default token permissions are repository settings. | Documented exact cutover configuration. |
| PUB-10 | Manual blocker | Patentability, ownership, school/contest obligations, and Syncfusion entitlement require owner confirmation. | Added a hard stop before visibility changes. |
| PUB-11 | Critical manual blocker | Existing Git commit metadata includes school-domain author e-mail addresses. Public visibility would expose that metadata even when source files are clean. | The cutover runbook requires an explicit privacy decision, GitHub e-mail privacy configuration, inspection of every published ref, and history rewriting before publication when exposure is not accepted. No raw address is copied into this report. |
| PUB-12 | Medium | Full-history Gitleaks initially reported three `generic-api-key` matches in one historical documentation commit. | Each location was inspected and confirmed to be ordinary slash-separated test-matrix prose. Only the three complete finding fingerprints are ignored in `.gitleaksignore`; path-wide, rule-wide, and commit-wide exclusions are prohibited by the policy guard. The full-history scan then completed with zero unignored findings. |
| PUB-13 | High | npm audit reported High-severity transitive findings involving `fast-uri`, `undici`, and `nanoid`; the existing baseline retained those findings after remediation. | Both Angular dependency trees pin patched releases, lock files were regenerated, and the stale High-severity baseline was cleared after zero High/Critical findings were verified. |
| PUB-14 | Medium | npm audit also reported Moderate `qs` findings through development tooling. | Both Angular dependency trees pin `qs` `6.16.0`; lock files were regenerated and both trees passed `npm audit --audit-level=low` with zero findings. |
| PUB-15 | High | Required pull-request workflows used path filters that could leave required checks pending when a PR changed only ignored paths. | `CI` and `Publication Readiness` now trigger on every pull request; path filtering is retained only for non-required push runs. |
| PUB-16 | High | The initial publication guard could miss multi-line `runs-on` labels, bracket-form secret references, `secrets: inherit`, cross-job environment protection, and inline write permissions. | The guard now evaluates job-local trust boundaries and regression coverage includes these bypass forms. |

## Automated validation completed

- publication-policy parser tests: 13 passed in the hardened regression suite;
- generated active workflow set: 19 workflows checked before final cleanup;
- active persistent self-hosted runner routing: 0;
- pull-request workflows referencing Actions secrets: 0;
- full reachable Git-history Gitleaks findings after exact false-positive
  fingerprint handling: 0;
- npm audit findings at Low, Moderate, High, or Critical severity in
  `frontend`: 0;
- npm audit findings at Low, Moderate, High, or Critical severity in
  `coglatas-frontend`: 0; and
- npm lockfile policy verification for both Angular trees: passed.

These results establish the automated repository-source baseline. The current
pull-request checks remain authoritative for merge readiness.

## Evidence reviewed

- repository visibility and default branch metadata;
- root and frontend package manifests and npm lock files;
- `.gitignore` coverage for environment files, secrets directories, and
  Syncfusion license files;
- all workflow files found by repository code search for `pull_request`,
  `self-hosted`, and `SYNCFUSION_LICENSE`;
- the existing Syncfusion license activation runbook and build boundary;
- the existing A-05 sensitive-data audit, which reported no confirmed committed
  raw secret in that pass but explicitly left full scanner reproduction and
  historical evidence review unresolved;
- current README and governance-file inventory;
- the three historical Gitleaks matches and their exact source context;
- npm audit output and the regenerated `frontend` and `coglatas-frontend` lock
  files;
- publication-guard regression cases for multi-line runner labels, bracket-form
  secret references, inherited secrets, job-local protected environments, and
  write-permission encodings; and
- sampled commit objects and workflow-run metadata, which confirmed the
  school-domain author-address exposure recorded as PUB-11.

## Scope limitations

Repository code search indexes the current default branch and cannot prove that
deleted, dangling, unindexed, or externally copied history is clean. This audit
also cannot inspect secret values, third-party account configuration, every
historical artifact, or legal ownership records.

Therefore, a green pull request is necessary but not sufficient to authorize
public visibility. The repository owner must complete and record every runbook
gate after this change is merged and before changing visibility.
