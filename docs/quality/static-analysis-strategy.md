# Static analysis strategy

## Responsibility split

| Tool | Role | Execution |
| --- | --- | --- |
| SonarQube Cloud | Repository-wide quality view across C#, JavaScript, TypeScript, HTML, CSS and SCSS | CI-based SonarScanner for .NET on trusted `main` pushes/manual dispatch and same-repository PRs after protected-environment approval; fork PRs are skipped |
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

## SonarQube Cloud CI mode

The repository uses **CI-based SonarScanner for .NET 11.3.0** in
`.github/workflows/sonarcloud.yml`. It analyzes the trusted `main` commit
after every push, or through a manual dispatch from `main`. The analysis builds
the actual .NET 10 solution and submits an OpenCover report collected from the
existing backend tests, while keeping the supported first-party frontend source
in cross-stack analysis scope. Existing generated-output and inactive-scaffold
exclusions are passed directly to the scanner; `.sonarcloud.properties` was
specific to Automatic Analysis and is removed.

The main workflow has no pull-request trigger. A separate
`.github/workflows/sonarcloud-pr.yml` runs on PRs to `main` from branches in
the same repository. It uses the same pinned .NET SDK and SonarScanner, checks
out the merge commit with full history, builds/tests, imports OpenCover, and
waits for the Quality Gate. Fork PRs are skipped because GitHub withholds
repository secrets from fork workflows.

The PR job uses the dedicated `sonarcloud-pr-analysis` environment and
read-only `contents` permission; checkout credentials are not persisted. The
Sonar token is passed only to validation and scanner begin/end steps, not the
build or test steps. Configure a required environment reviewer and inspect the
PR before approving its scanner run. Both workflows fail rather than issuing a
misleading SKIPPED success when project settings or credentials are missing.

### Required one-time project setup

1. Confirm that the existing SonarQube Cloud project is bound to
   `NYGsatoshi/Coglatas` using the GitHub integration; import it if absent.
2. Keep Automatic Analysis disabled; it is already disabled for this
   project. SonarQube Cloud does not permit Automatic Analysis and CI-based
   analysis on the same project.
3. Provide `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` as GitHub Actions
   variables, using the **actual existing Cloud organization and project keys**,
   not an unverified guessed default.
4. Keep the existing project-scoped `SONAR_TOKEN` available to the
   `syncfusion-licensed-build` environment for trusted `main` scans.
5. Create a separate GitHub Environment named `sonarcloud-pr-analysis`.
   Require a reviewer, allow deployment from `refs/pull/*/merge`, and add the
   project-scoped `SONAR_TOKEN` as an environment secret. Never put tokens in
   YAML, repository-level secrets, pull request comments, or diagnostic reports.
6. After setting the environment, variables and secret, re-run PR #1235 and
   inspect its diff before approving the environment deployment. Confirm the
   PR's SonarCloud decoration, OpenCover import and Quality Gate. Also inspect
   the first exact-`main` `SonarCloud / trusted main` run after merge.
   Do not add the Sonar status as a required check until a successful PR result
   is visible and its check name is verified.

### Pull request analysis and enforcement

`.github/workflows/sonarcloud-pr.yml` creates PR-head analysis for same-repo
pull requests after approval in the protected `sonarcloud-pr-analysis`
environment. Fork PRs do not receive this scanner because GitHub does not pass
repository secrets to those workflows. The analysis reports the SonarCloud PR
Quality Gate and includes backend OpenCover evidence. Do not require the
SonarCloud PR status in branch rulesets until the first successful result is
visible and its exact check-run name has been verified. The scanner does not
replace CodeQL, ReSharper, Qodana or any SEC-ARCH/Required Check decision.

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

The new SonarQube Cloud scanner enforces the Quality Gate on trusted `main` runs. PR-head Quality Gate reporting remains unavailable in this mode and is not a required PR check; the existing protected PR gates remain unchanged.

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
