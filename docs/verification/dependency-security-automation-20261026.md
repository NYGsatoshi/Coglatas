# Dependency automation and independent security scanning

## Scope and operational status

This change migrates **version-update PR generation** from Dependabot to a self-hosted
Renovate workflow. GitHub Dependency graph, Dependabot alerts and existing
Dependency Review, npm audit, SBOM, Grype and image Trivy workflows remain
independent protections. Removing `.github/dependabot.yml` disables the old
version-update schedules; **do not disable Dependabot alerts** in repository
settings. Renovate's vulnerability-alert fix PRs rely on those GitHub alerts.

This is automation tooling only: it does not alter runtime security controls,
formal SEC-ARCH/RLS approval, database migrations, release admission or the
Student Council release hold. Scans detect known or suspicious dependencies;
no single engine establishes complete vulnerability coverage.

## Owner activation steps (not deliverable in a pull request)

1. Create protected GitHub Actions environment `dependency-maintenance`
   restricted to Main. Use environment-secret scope for the Renovate App key;
   schedule automation requires no manual deployment approval. Create the
   separate `socket-security-scans` environment before enabling authenticated
   Socket SCA; scope its API key to that environment.
2. Create a dedicated Renovate GitHub App, install it on `NYGsatoshi/Coglatas`,
   and grant the minimum required repository permissions: Contents, Pull
   requests, Issues and Workflows write; Dependabot alerts read. Include other
   GitHub platform permissions only as documented for Renovate if required by
   enabled features. Restrict installation access to the intended repository.
3. Set Actions variable `RENOVATE_APP_ID` and secret
   `RENOVATE_APP_PRIVATE_KEY`. Never commit the private key. The hourly
   Renovate workflow **fails explicitly** when credentials are absent; no silent
   simulation or fallback to the default GITHUB_TOKEN is permitted.
4. Confirm `Settings > Security` has Dependency graph and Dependabot alerts
   enabled. Run the Renovate workflow once manually on Main; verify the
   Dependency Dashboard, extraction logs and new PR CI execution with the
   short-lived app token. GitHub Actions schedule timing is best-effort, not a
   guaranteed sub-hour detection latency.
5. The repository's workflow trust-policy only permits SARIF Code Scanning
   uploads from explicitly authorized workflows. The new read-only scanners
   upload SARIF as GitHub Actions artifacts, not Code Scanning. Moving these
   results into Code Scanning requires separate Owner approval and narrowly
   scoped trust-registry authorization; this PR does not silently widen
   privileged workflow permissions.
6. Socket Firewall Free runs on npm manifest/lockfile changes without a Socket
   API key. To additionally use authenticated Socket SCA, configure secret
   `SOCKET_SECURITY_API_KEY` and set Actions variable
   `SOCKET_SCA_ENABLED=true`, then run the workflow manually and inspect its
   permissions and findings. Keep this flag unset/false until credentials
   and provider authorization are established. The authenticated CLI sends
   dependency metadata to Socket's service. The read-only workflow does not
   post PR comments; provider-side Socket GitHub App integration must be
   configured separately to enable PR comment/report delivery. The authenticated
   SCA job runs in a separate trusted Main-only workflow because credentials
   may never be exposed to pull_request-triggered jobs.
7. Verify the new OSV PR baseline-diff gate and full Main inventory, Trivy
   repository SARIF artifacts, and existing SEC-09/SEC-10/SEC-11 image scans.
   Review findings before making new checks mandatory. Existing advisories
   must not be relabeled as newly introduced vulnerabilities.

## Coverage and enforcement

| Control | Trigger | Enforcement |
| --- | --- | --- |
| Renovate Self-hosted | hourly schedule; manual | update proposals only; never auto-merges; credential preflight fails closed |
| OSV PR scan | PR and merge queue | blocks on vulnerabilities newly introduced relative to the base |
| OSV full inventory | Main push; daily; manual | reports existing known vulnerabilities as CI artifacts, report-only |
| Trivy fs | PR; Main push; daily; manual | HIGH/CRITICAL dependency findings into SARIF CI artifacts; report-only; scanner errors fail |
| Socket Firewall Free | npm manifest/lockfile PR and Main changes; manual | installs npm lockfile trees using Socket Firewall; blocks on rejected fetches |
| Socket SCA | separate trusted Main workflow with opt-in flag | authenticated Main repository scanning only after external provisioning |
| Existing image Trivy + Grype/SBOM | existing image/release workflow triggers | unchanged: image, not repository filesystem, assurance |

Renovate scans npm workspaces/packages, NuGet projects and SDK pins, Docker,
Compose and GitHub Actions. Application .NET SDK majors, Docker Node major
baseline and Playwright image parity require separate review. Unknown
third-party binary archives and other nonstandard dependencies need explicit
SBOM/catalog integration; this PR does not claim to resolve them.

## Verification requirements

- Validate `.github/renovate.json` against the current Renovate schema and
  run a dry run against the exact branch and an authorized GitHub App before
  considering the updater operational.
- Execute actionlint and zizmor on each changed workflow; verify SARIF
  upload rights and an actual new-advisory fixture before making new gates
  required.
- Check every Socket package directory has a lockfile and that Socket's npm
  proxy handles the repository's approved package sources. Failed network or
  registry access is not evidence of a malicious dependency.
- Do not merge solely because static configuration parses. Host-run results,
  independent security review and Owner release holds remain applicable.
