# Static analysis strategy

## Responsibility split

| Tool | Role | Execution |
| --- | --- | --- |
| SonarQube Cloud | Build-aware C# and multi-language quality view across C#, JavaScript, TypeScript, HTML, CSS and SCSS | CI-based SonarScanner for .NET on `main` pushes and verified same-repository PR branch pushes; official App Quality Gate |
| ESLint + angular-eslint | Maintained Angular Web / TypeScript quality checks | **Current CI enforcement remains suspended.** `Frontend Static Analysis` publishes a successful SKIPPED marker, not an ESLint PASS. |
| Stylelint | Maintained Web CSS and SCSS quality checks | **Current CI enforcement remains suspended.** The no-op `Frontend Static Analysis` marker is not a Stylelint PASS. |
| ReSharper InspectCode CLI | Fast JetBrains inspection lane for .NET pull-request feedback | Every PR; runs only when .NET/config inputs changed, scopes ordinary changes to affected projects, reports `WARNING` or higher with solution-wide analysis and duplicate Roslyn analyzer execution disabled, and fails on findings in files changed by the PR |
| Qodana Community for .NET | Deep JetBrains/ReSharper repository inspection and project-model validation | Trusted `main` pushes and manual dispatch; full repository scan with strict Critical/unresolved/project-model guards |
| CodeQL | Security-oriented semantic/data-flow analysis | Every PR targeting `main`, trusted `main` pushes and weekly schedule |

The historical convergence policy prioritized .NET/security/performance work before
Avalonia/ProjectIDE implementation and suspended repository-managed
ESLint/angular-eslint/Stylelint enforcement to manage existing lint debt.
**That suspension does not deprecate the Angular Web product.** Angular Web
is an actively maintained frontend alongside a separately gated Avalonia
Desktop target. The lint toolchain and baseline remain in the repository; their
retention does not constitute an executed quality gate. Restoring enforcement
requires a separate reviewed change with scoped debt triage and real CI evidence.

SonarQube Cloud remains an independently managed cross-stack quality view, CodeQL owns security analysis, ReSharper InspectCode supplies fast pull-request feedback for .NET, and Qodana supplies the deeper trusted-main JetBrains/ReSharper repository lane.

## Frontend lint debt baseline

`tools/frontend-inspections/baseline.json`, the ESLint/angular-eslint
configuration, and Stylelint-related inspection tooling remain retained, but
**GitHub Actions does not currently enforce them**.

`Frontend Static Analysis` intentionally does not install Node/npm dependencies
or execute ESLint, angular-eslint, or Stylelint. It emits a successful
SKIPPED/audit marker solely to preserve the existing CI status-context
contract. Neither that marker nor unrelated required-check success proves the
maintained Web client passes these lint analyzers.

The frontend debt baseline must not be increased, suppressed, or silently
renamed to manufacture a green result. Re-enabling enforcement should be
addressed through a separate, reviewed plan with an explicit owner, debt
inventory, negative/positive tests, and real CI qualification. No workflows,
required status checks, thresholds, or debt budgets change in this document.

## SonarQube Cloud mode: build-aware CI analysis with native GitHub App reporting

### Existing project and desired checks

The historical September SonarCloud project was deleted; its measurements
cannot be restored. Keep the **existing, GitHub-bound** SonarQube Cloud project
the project identified by `SONAR_PROJECT_KEY` and the official `sonarqubecloud[bot]` integration.
The desired result remains a fresh App-owned `SonarCloud Code Analysis`
Quality Gate check, PR Conversation summary, and applicable inline issues.
A custom SARIF upload or synthetic GitHub check is not equivalent.

The repository no longer uses Automatic Analysis. The canonical, single
scanner is `.github/workflows/sonarcloud-ci.yml`: pinned SonarScanner for
.NET 12.0.0 executes `begin` / .NET 10 restore+build / `end` against
`https://sonarcloud.io` using the *same* project and its organization
key supplied by `SONAR_ORGANIZATION`. The source SHA is checked out with complete Git history.
Multi-language `sonar.scanner.scanAll=true` includes maintained first-party
Web JavaScript/TypeScript/HTML/CSS/SCSS alongside build-aware C#.
The workflow explicitly imports only the existing source encoding,
exclusions, and duplication scope from `.sonarcloud.properties`, which the
.NET scanner does **not** otherwise read automatically. Generated and
dependency output exclusions remain unchanged.

### Workflow trust and coverage boundaries

The publication policy forbids accessing repository secrets in a
`pull_request` workflow or running untrusted fork code with privileged
credentials. Do not use `pull_request_target`, `workflow_run` plus
unchecked PR checkout, or a custom token-bearing SARIF bridge.

The new Sonar workflow uses only **`push` events in this repository**.
A read-only routing job first distinguishes:

- `main`: scan the exact pushed Main SHA.
- Another branch: query GitHub for an open PR targeting `main` and require
  the exact same upstream repository, head branch and pushed SHA. Only one
  unique verified PR is eligible; explicit Sonar PR identifiers are then
  supplied by the workflow.
- No matching PR (including fork PRs and obsolete heads): skip analysis,
  without accessing `SONAR_TOKEN`.

This covers ordinary updates to PR branches inside `NYGsatoshi/Coglatas`,
not arbitrary external contributor forks. It treats upstream repository
push permissions as a trusted author boundary, consistent with the current
GOV-04 policy. Because SonarScanner for .NET builds source code, the token is
not safe with untrusted branch contents even if its environment variable is
set only in the scanner begin/end steps. Project-scoped, short-lived
credentials and restricted push access are important. To support fork PR
analysis in the future, design a separately reviewed isolation model rather
than weakening the existing trust validator.

This is an event-scoped trade-off: merely opening a PR from a previously
pushed branch does not trigger the `push` scanner again. The next branch
push (or re-running its push workflow after PR creation) will qualify it.
A first run can be marked skipped if the matching PR does not yet exist.

### One-time SonarCloud/GitHub activation

1. Keep the current SonarCloud project bound to `NYGsatoshi/Coglatas`
   and retain the official SonarQube Cloud GitHub App integration.
2. In **Administration > Analysis Method**, **disable Automatic Analysis**
   before enabling the CI-based GitHub Actions method. Do not run both
   methods concurrently against this project.
3. Set GitHub Actions **repository variables** `SONAR_PROJECT_KEY` and
   `SONAR_ORGANIZATION` to the exact project and organization keys shown in
   SonarCloud. The scanner fails closed if either variable is missing.
4. Create a dedicated GitHub Actions environment named
   `sonarcloud-analysis` under **Settings > Environments**. Configure
   suitable trusted deployment-branch restrictions and any required reviewer
   approvals for your write-access model. Generate an analysis token with
   the minimum scope and practical expiry, and save it as the environment
   **secret** `SONAR_TOKEN` (not a general-purpose repository secret).
   This protects the credentialed scan job; enabling reviewers means each
   environment deployment must be approved before the scan proceeds.
   No token or personal credentials belong in source, pull-request payloads,
   logs, artifacts, or issue comments.
5. Merge only after repository workflow security checks pass and the
   SonarCloud owner has confirmed the analysis-method switch and secret
   are ready. Once merged, a fresh `main` push (and a fresh
   same-repository PR branch push) starts the CI scanner.
6. Check the scanner build's `begin` / `build` / `end` logs, the
   SonarCloud analysis revision and the official App-owned GitHub
   Quality Gate. Enable project PR summaries/annotations in SonarCloud
   if those optional decoration settings are not already active.
7. Only require the SonarCloud App check in the repository ruleset after
   it has actually reported on fresh `main` and PR revisions. Do not
   mistake successful repository CI for confirmation of SonarCloud
   processing or decoration.

**Activation boundary:** the repository workflow cannot change SonarCloud's
analysis mode, issue-annotation settings or GitHub Actions secret values.
The service-side changes and first successful hosted scanner run remain
external acceptance conditions. Source changes alone are not a completed
scanner restoration.


## Pull-request merge gates

Repository-defined pull-request checks must not gain general repository mutation authority. The CodeQL workflow is the single narrow exception: GitHub's advanced CodeQL setup requires `security-events: write` so SARIF can be published to Code Scanning, while fork `pull_request` runs still receive a read-only `GITHUB_TOKEN` and GitHub explicitly permits Code Scanning result upload for that event.

The active PR-stage gates include:

- backend build/test
- frontend build/test
- security scan
- publication readiness
- ReSharper InspectCode / PR
- CodeQL semantic/data-flow analysis

`Frontend Static Analysis` still emits its named status context but is intentionally a successful no-op marker; ESLint/angular-eslint/Stylelint findings are not merge-blocking under the current suspended policy, including for the maintained Angular Web client.

The SonarQube Quality Gate is supplied by the bound SonarQube Cloud GitHub App after the trusted CI-based scanner uploads an analysis, not by an untrusted pull-request secret workflow or a custom SARIF bridge.

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
