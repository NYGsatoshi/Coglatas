# Static analysis strategy

## Responsibility split

| Tool | Role | Execution |
| --- | --- | --- |
| SonarQube Cloud | Repository-wide quality view across C#, JavaScript, TypeScript, HTML, CSS and SCSS | Automatic Analysis on every PR update and every push to `main` |
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

This repository uses **SonarQube Cloud Automatic Analysis**, not a token-bearing GitHub Actions scanner.

The repository publication policy forbids secrets in `pull_request` workflows. Automatic Analysis reads the bound GitHub repository directly, so PR analysis does not require `SONAR_TOKEN` in an untrusted PR workflow.

Repository-side scope configuration is stored in `.sonarcloud.properties`.

### One-time SonarQube Cloud setup

1. Import `NYGsatoshi/Coglatas` into SonarQube Cloud through the GitHub integration.
2. In the project, open **Administration > Analysis Method** and enable **Automatic Analysis**.
3. Keep CI-based Sonar scanning disabled for this project; Automatic Analysis and CI-based analysis must not run together.
4. Configure the project Quality Gate for new code.
5. In the GitHub `main` ruleset/branch protection, require the SonarQube Quality Gate status after its first successful report.

Automatic Analysis should then run on each push to `main` and on each update to a pull-request branch.

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

The SonarQube Quality Gate is supplied by the SonarQube Cloud GitHub integration rather than by a secret-bearing workflow in this repository.

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
