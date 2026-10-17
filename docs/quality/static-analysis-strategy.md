# Static analysis strategy

## Responsibility split

| Tool | Role | Execution |
| --- | --- | --- |
| SonarQube Cloud | Repository-wide quality view across C#, JavaScript, TypeScript, HTML, CSS and SCSS | Protected CI scanner on `main` pushes, after successful same-repository PR CI, and manual dispatch; SARIF published to Code Scanning |
| ESLint + angular-eslint | Legacy Angular/JavaScript/TypeScript policy | **CI enforcement suspended during the Avalonia migration.** `Frontend Static Analysis` now publishes an explicit successful SKIPPED marker only. |
| Stylelint | Legacy CSS and SCSS policy | **CI enforcement suspended during the Avalonia migration.** It shares the same no-op `Frontend Static Analysis` marker. |
| ReSharper InspectCode CLI | Fast JetBrains inspection lane for .NET pull-request feedback | Every PR; runs only when .NET/config inputs changed, scopes ordinary changes to affected projects, reports `WARNING` or higher with solution-wide analysis and duplicate Roslyn analyzer execution disabled, and fails on findings in files changed by the PR |
| Qodana Community for .NET | Deep JetBrains/ReSharper repository inspection and project-model validation | Trusted `main` pushes and manual dispatch; full repository scan with strict Critical/unresolved/project-model guards |
| CodeQL | Security-oriented semantic/data-flow analysis | Every PR targeting `main`, trusted `main` pushes and weekly schedule |

The active convergence policy prioritizes the .NET/security/performance path needed before Avalonia/ProjectIDE implementation. Repository-managed ESLint/angular-eslint/Stylelint enforcement is intentionally suspended so legacy Angular/JavaScript lint debt does not block that convergence. The lint toolchain and baseline remain in the repository for historical/reference use; this change does not delete them or reinterpret prior evidence.

SonarQube Cloud remains an independently managed cross-stack quality view, CodeQL owns security analysis, ReSharper InspectCode supplies fast pull-request feedback for .NET, and Qodana supplies the deeper trusted-main JetBrains/ReSharper repository lane.

## Frontend lint debt baseline

`tools/frontend-inspections/baseline.json`, the ESLint/angular-eslint configuration, and Stylelint-related inspection tooling remain retained but are **not enforced by GitHub Actions during the Avalonia migration**.

`Frontend Static Analysis` intentionally does not install Node/npm dependencies or execute ESLint, angular-eslint, or Stylelint. It runs a small no-op job that emits a successful SKIPPED/audit marker so an existing status-context consumer does not become ambiguous or missing.

Do not spend Pre-Avalonia convergence work reducing the legacy lint baseline. Do not modify the baseline merely to produce a green status. If the legacy frontend is ever reactivated as a supported implementation surface, re-enabling lint enforcement requires a separate reviewed policy change.

## SonarQube Cloud mode

`.github/workflows/sonarcloud.yml` defines **CI-based analysis** using pinned
SonarScanner for .NET 12.0.0, a full Release build and multi-language analysis.
This candidate requires the service-side setup below and hosted execution before
it establishes that scanning or Code Scanning publication works.

The publication policy still forbids secrets in `pull_request` workflows. A
`workflow_run` consumer resolves the current merge commit after successful
`PR CI`, rejects forks and stale heads, then waits for `sonarcloud-analysis`
environment approval. MSBuild executes repository code: reviewers must inspect
the selected revision, including build inputs, before releasing the token. The
preflight refuses an environment without required reviewers. No PR artifacts or
PR scripts are used as privileged workflow tooling.

The scanner receives the shared exclusions in `.sonarcloud.properties`
explicitly. Generated files, dependencies and inactive frontend scaffolding stay
excluded. This workflow does not reactivate ESLint or Stylelint enforcement.

### One-time SonarQube Cloud setup

1. Import `NYGsatoshi/Coglatas` into SonarQube Cloud through the GitHub integration.
2. Open **Administration > Analysis Method** and disable **Automatic Analysis**;
   automatic and CI-based analysis must not run together.
3. Create the protected `sonarcloud-analysis` GitHub environment with required
   reviewers. Restrict deployment branches to
   reviewed workflow refs. Add `SONAR_TOKEN` only as an environment secret; it needs
   analysis and Browse access for issue export. Prefer a token limited to this
   project when available. A personal token inherits account permissions and needs
   an explicit owner decision. Do not put it in repository or organization secrets.
4. Set environment variables `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` to the
   actual service-side keys. Configure the project's new-code Quality Gate.
5. Merge the workflow normally so `workflow_run` and manual dispatch are registered
   on `main`. Before merge, the exact `codex/restore-sonarcloud-ci-20261017` branch
   has a push bootstrap trigger that resolves PR `1235` and rejects a moved head.
   Explicitly review that revision and permit its ref in the protected environment.
6. Verify the SonarCloud check and the `SonarCloud` tool in GitHub **Security >
   Code scanning**, including the exact commit/ref and completed SARIF processing.
   Only then consider requiring the SonarCloud integration status in the ruleset.

The consumer scans open same-repository PRs targeting `main`; forks are skipped
because they cannot enter the token-bearing boundary. It scans the merge SHA and
publishes to `refs/pull/<number>/merge`; it refuses publication if the PR has moved
or closed. Main results use `refs/heads/main`. Manual dispatch with an empty PR
input scans the current main revision. No branch protection is changed by this PR.

Service setup observed through Edge on 2026-10-17: organization `nygsatoshi`,
project `nygsatoshi_coglatas`, and GitHub environment `sonarcloud-analysis` exist.
The environment requires NYGsatoshi review, disables administrator bypass, permits
only `main` and the named PR bootstrap branch, and contains both project variables.
The owner approved a 30-day personal token, stored only as its `SONAR_TOKEN`
environment secret, expiring 2026-11-16. This plan does not offer scoped organization
tokens; the approved personal token inherits account permissions. Rotate it before
expiry and prefer a scoped token when the plan permits one. The Analysis method
page offers CI setup and no Automatic Analysis control; the project had no analysis
at inspection. These settings are not yet proof of a successful scan/SARIF upload.

After the server compute task succeeds, the bridge pages through unresolved issues
for that PR or main and converts primary locations, rule IDs, severities and stable
issue identities to SARIF 2.1.0. The exporter rejects incomplete/duplicate results
and totals above the API's 10,000-issue window. It does not export Security Hotspots,
secondary flows, test coverage or duplication metrics. SonarCloud remains the
authoritative view for those. Issue queries reflect the latest branch/PR state,
not an immutable analysis-ID snapshot; keep other scanners disabled and serialize
scans for a given PR/main. Concurrent external analysis or issue triage can still
change results during export; changing totals are rejected.

A fresh runner publishes only the SARIF artifact using `security-events: write`;
the scanner job has no GitHub write permissions. The bridge waits for GitHub's
SARIF processing, then enforces the exact analysis's Quality Gate. A failed gate
therefore still publishes findings. API errors, missing configuration, server
failure, incomplete export and upload rejection fail the workflow. SARIF artifacts
expire after seven days. Workflow configuration alone is not execution evidence.

`governance/workflow-trust-policy.json` grants only the SonarCloud `publish` job
the Code Scanning write scope for push, workflow-run and manual events. The trust
validator recognizes this specific protected-revision/data-only boundary: changing
the environment, adding cross-run artifact options or publisher commands, or
removing bridge revision checks removes its exception. Other dynamic privileged
checkouts and artifact consumers remain rejected. Regression tests exercise these
negative controls.

## Pull-request merge gates

Repository-defined pull-request checks must not gain general repository mutation authority. The CodeQL workflow is the single narrow exception: GitHub's advanced CodeQL setup requires `security-events: write` so SARIF can be published to Code Scanning, while fork `pull_request` runs still receive a read-only `GITHUB_TOKEN` and GitHub explicitly permits Code Scanning result upload for that event.

The active PR-stage gates include:

- backend build/test
- frontend build/test
- security scan
- publication readiness
- ReSharper InspectCode / PR
- CodeQL semantic/data-flow analysis

`Frontend Static Analysis` still emits its named status context but is intentionally a successful no-op marker; ESLint/angular-eslint/Stylelint findings are not merge-blocking during the Avalonia migration.

The SonarQube Quality Gate is supplied by the SonarQube Cloud GitHub integration
and enforced again after SARIF publication by the protected CI consumer.

## Main build parallelism

Main CI intentionally has no workflow-level concurrency gate. Build producers receive their own cancel-in-progress concurrency groups so a new main push can start building immediately even while an older main run is still finishing long-running analysis or acceptance consumers.

Main CI runs the authoritative .NET and licensed frontend producer jobs concurrently. The .NET producer also enables MSBuild project-graph parallelism instead of forcing `-m:1`. A small assembler job waits for both producer artifacts, verifies exact source-SHA stamps, builds the final runtime image once, and republishes the existing combined `main-build-artifacts` contract for downstream consumers.

## PR versus main CI

`.github/workflows/ci.yml` is pull-request-only and contains the ReSharper fast lane. It does not run on `main` pushes.

`.github/workflows/main-build-artifacts.yml` is the trusted main-only `Main CI` build/artifact hub. It has no ReSharper job. Main validation fans the trusted build out to Main Test, Main Frontend, Main Security, Qodana Community/Cloud, Performance, real-backend E2E, and image SBOM consumers.

## Pull-request routing and build redistribution

`.github/workflows/ci.yml` evaluates the changed-file graph exactly once in the `CI preflight + route` job. Its routing outputs are passed to the backend and frontend producer jobs, which proxy the relevant values to their dependent required checks. Downstream jobs do not rerun `scripts/ci/route-main-ci-changes.sh`.

The PR workflow centralizes reusable build outputs before downstream checks consume them:

- `dotnet-build` produces the authoritative Release `bin/Release` and `obj` trees once, plus a SHA stamp. `build-test`, ReSharper, and PR security contract validation consume that exact artifact instead of recompiling the ordinary backend graph.
- When SEC-01 / AV-MIG contract validation is routed, the producer runs `dotnet-getdocument` against the already-built `Coglatas.Web.dll` under the synthetic Test-only boundary and publishes the generated OpenAPI document as a separate SHA-stamped artifact; it does not recompile the solution. The PR `security-scan` verifies and executes against the redistributed backend/OpenAPI outputs instead of regenerating the contract. Main keeps the two-pass build-aware deterministic contract generation.
- `frontend-build` installs the Angular dependency graph once for build production, creates the production `frontend/dist/coglatas-web` output and routed Storybook static output, and publishes them as one SHA-stamped artifact. `frontend-test` restores those outputs; production-build and Storybook rebuild workers are disabled there, and Playwright is told to reuse the redistributed host build.

Runner-local package stores are intentionally not redistributed. Consumers may still perform lightweight NuGet/npm installs needed by test or scanner tooling; the expensive compilation outputs are the artifacts being shared.

## ReSharper pull-request policy

`.github/workflows/resharper_pr.yml` is the fast .NET inspection lane. It runs on every pull request targeting `main`, but skips the expensive analysis when the diff contains no .NET source, project, solution, SDK, NuGet, ReSharper, or MSBuild configuration inputs.

The PR workflow builds the Release .NET graph once in the `dotnet-build` producer, packages the resulting `bin/Release` and `obj` trees with an exact-revision SHA stamp, and publishes that archive once per workflow run. `build-test` and the ReSharper reusable workflow consume the same artifact rather than repeating compilation; consumers may perform a lightweight NuGet restore when runner-local package files are required. The ReSharper workflow pins `JetBrains.ReSharper.GlobalTools` to version `2026.2.2`; ordinary C# changes are mapped to their owning projects and passed to `InspectCode` with `--project`, while repository-wide MSBuild/ReSharper configuration changes fall back to the full solution. InspectCode runs with `--severity=WARNING`, `--no-swea`, `--no-build`, and `RunAnalyzers=false`. Compile correctness and Roslyn analyzers remain covered by the authoritative producer/build-test path, while trusted `main` Qodana retains the full build-aware deep inspection. The SARIF guard fails only when a reported issue is located in a file changed by the pull request. The full SARIF report is retained as a short-lived workflow artifact. The lane uses read-only repository permissions and no JetBrains license secret.

## Qodana policy

Qodana uses the `qodana.recommended` profile and additionally enables all inspections whose default JetBrains severity is `ERROR`, `WARNING`, or `WEAK WARNING`. Generated output, dependency directories, test artifacts, runtime data and the inactive legacy frontend scaffold remain excluded; first-party source and tests remain in scope.

Qodana is the trusted-main deep lane rather than the pull-request fast lane. Every main push enters through `Main CI` (`.github/workflows/main-build-artifacts.yml`), which restores the trusted build artifact into both the Community and Cloud Qodana lanes; the standalone Qodana workflows remain manual fallbacks. The temporarily disabled schedules remain disabled.

Historical non-critical debt remains visible rather than making the lane permanently red. The post-processing guard still fails on any Critical finding, any unresolved-symbol finding, project-model/restore/build/SDK/package-resolution failure, missing SARIF output, or Qodana execution failure.
