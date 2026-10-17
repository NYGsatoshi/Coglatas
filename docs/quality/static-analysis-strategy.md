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

## SonarQube Cloud mode: restore native GitHub App reporting on the active project

### Project identity and desired behavior

The original September SonarCloud project, evidenced by the
[official bot's report on PR #808](https://github.com/NYGsatoshi/Coglatas/pull/808#issuecomment-5845351369),
was **deleted**. It is not a restoration target; its past analyses,
issues and measurements are not recoverable by repository changes.

The current SonarQube Cloud project, identified by `SONAR_PROJECT_KEY`,
is a **separate** project. The desired result is equivalent *new*
pull-request reporting by the official `sonarqubecloud[bot]` /
SonarCloud GitHub App: an App-owned Quality Gate check, a report in
Conversation, and inline annotations in Files changed. Neither
GitHub Actions status nor a custom Code Scanning SARIF upload is a
substitute for the official App's PR decoration.

### Repository-side mode

The intended setup is the historical **Automatic Analysis** workflow:
SonarQube Cloud reads source directly from a *bound* GitHub repository
rather than using a token-bearing GitHub Actions scanner.
`.sonarcloud.properties` defines repository-side scope and
exclusions. The new scanner, API bridge and SARIF publisher previously
introduced in PR #1235 were removed, and no Sonar-specific scanner
workflow remains.

This describes the intended mode, **not a confirmed active service
configuration**. A manually created unbound SonarQube Cloud project
cannot perform automatic analysis or publish native PR decoration until
the correct GitHub integration and repository binding are established.
Automatic Analysis may be offered after binding if the project is
eligible.

**C# analysis trade-off:** SonarQube Cloud documents only partial C#
support in Automatic Analysis. Unlike CI-based SonarScanner for .NET,
it does not execute the solution's MSBuild/.NET 10 build and cannot
import test coverage. The native App's PR comments and check are
independent of which analysis method sends results. If full C# 14 /
.NET 10 build-aware analysis, coverage or external reports are a
requirement, use one reviewed CI-based SonarScanner for .NET against the
**same bound project**, instead of Automatic Analysis; never run
both methods on the same project simultaneously.

### One-time configuration in SonarQube Cloud

1. Open the **current** project identified by `SONAR_PROJECT_KEY`,
   not the deleted project. Preserve this project's new analysis
   history and Quality Gate.
2. Ensure the **official SonarQube Cloud GitHub App** has access to
   `NYGsatoshi/Coglatas`. If the SonarQube Cloud organization itself
   is unbound, bind it to the corresponding GitHub account first.
3. Under **Administration > General Settings > Repository binding**,
   choose `NYGsatoshi/Coglatas` and save. Do not manually insert a
   repository URL or create another competing project.
4. If the project is eligible, activate **Automatic Analysis** under
   **Administration > Analysis Method**. Keep CI-based scanning
   disabled. If this option is unavailable even after binding, resolve
   the project eligibility/binding reason; if needed, restore a
   single secured CI scanner while retaining the official App
   for GitHub reporting.
5. Under **Administration > General Settings > Pull Requests >
   Integration with GitHub**, ensure `Enable summary comment` is
   selected. Under **Pull Requests > Issue Annotations**, ensure
   `Enable Issue Annotations` is selected.
6. After the *current* project produces a new scan for an open PR,
   verify that the GitHub check is owned by the SonarCloud App and
   that `sonarqubecloud[bot]` reports the current project's Quality
   Gate. Check that inline annotations are present when applicable.
   A prior comment on a historical PR is not new execution evidence.
   Only then consider adding the confirmed App check to a required
   status-check ruleset.

**Activation boundary:** this repository change does not control
SonarQube Cloud project binding, GitHub App installation or analysis
method. They require access to the external service. Keep the PR
unmerged until native App reporting has been verified. Passing this
repository's CI checks cannot prove SonarCloud service integration.

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
