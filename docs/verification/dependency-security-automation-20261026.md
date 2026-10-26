# Dependency automation and independent security scanning

## Scope

Mend-hosted Renovate is the sole dependency-update bot for this repository.
The official Mend Renovate GitHub App is already installed and demonstrated
its ability to enumerate the repository's npm, NuGet, Docker/Compose, .NET SDK,
and GitHub Actions dependencies in onboarding PR #1418. The audited repository
configuration lives at `.github/renovate.json`, which Renovate recognizes
as a supported configuration path. **Do not merge onboarding PR #1418 as
a second configuration file.**

The former `.github/dependabot.yml` is removed to stop scheduled Dependabot
version-update PRs. Preserve GitHub Dependency graph and Dependabot
**vulnerability alerts**; GitHub Dependabot *security-update PR generation*
must be disabled independently in Settings if enabled, to avoid duplicate
fix PRs. Mend-hosted Renovate can use vulnerability alerts for prioritized
remediation when that feature is provided by the App installation.

**No self-hosted Renovate runner, GitHub Actions App credentials, private key,
GitHub Environment, or hourly Actions schedule is required.** Do not add
`RENOVATE_APP_ID` or `RENOVATE_APP_PRIVATE_KEY`. Mend-hosted Community
Cloud runs on its provider-managed schedule, normally around every four hours
for active repositories, not on a guaranteed hourly deadline.

This change is limited to dependency and security tooling; product/runtime
authorization, SEC-ARCH/RLS, DB schema, formal approvals, release admission,
Student Council release holds, and production deployment remain untouched.

## Update constraints

- No automatic merging, including for security fixes. All updates require
  the existing CI and Owner/reviewer qualification.
- Bound update PR concurrency and hourly PR creation; keep Major updates in
  the Dependency Dashboard until explicitly approved.
- **GitHub Actions SHA pins:** request dashboard approval before opening
  GitHub Actions upgrade PRs. Any such update must also amend the reviewed
  `governance/github-actions-allowlist.json` entry (SHA, version, scope) and
  pass the existing protected workflow pin/permission guards. No bot change
  is automatically trusted to expand the allowlist or merge.
- Keep the approved Node 24 Docker baseline. Exact Playwright image updates
  are held pending synchronized npm lockfile/runtime/browser acceptance.
- Keep .NET SDK majors discoverable but subject to explicit dashboard approval,
  together with the production SDK image-version/digest contract.

## Detection and enforcement

| Control | Trigger | Behavior |
| --- | --- | --- |
| Mend-hosted Renovate | provider-managed schedule and platform events | update PRs + Dependency Dashboard; no automerge |
| OSV Scanner | PR/merge queue; Main push; daily; manual | PR newly introduced advisory gate; full inventory report-only |
| Trivy repository | PR; Main push; daily; manual | HIGH/CRITICAL filesystem/library SARIF to read-only artifacts |
| Socket Firewall Free | npm lockfile/manifest changes and manual | re-fetch and inspect all four npm installation roots |
| Authenticated Socket SCA | Main-only workflow after opt-in | API key + trusted GitHub Environment required only for SCA |
| Existing Dependency Review/npm audit/SBOM/Grype/image Trivy | existing workflows | retained; no gate weakening |

Coglatas' GOV workflow trust policy forbids adding unreviewed
`security-events:write` permissions. OSV/Trivy results therefore remain
Actions SARIF artifacts, not Code Scanning uploads. Enabling direct uploads
requires separately approved, scoped workflow allowlist changes.
Unknown external binary dependencies still require separate SBOM/inventory
coverage; complete vulnerability detection is not claimed.

## Required integration verification

1. Run the dependency policy mutation tests, publication readiness,
   actionlint/zizmor and all relevant exact-HEAD PR CI checks.
2. Merge the integration PR only with the required checks passing on the
   current Main merge candidate and no outstanding separate approval holds.
3. Inspect the Mend-hosted Renovate dashboard/job logs for default-branch
   configuration loading, extraction, security update proposals and new
   update-PR CI execution. Merge of the configuration is not itself proof
   that a later Mend-hosted run succeeded.
4. After confirmed new-run onboarding, close superseded #1418 unmerged.
   Reject any proposal attempting an unaudited Action SHA or unsupported
   Node/SDK migration; do not waive CI.
5. For Socket SCA opt-in, create `socket-security-scans` protected Main-only
   GitHub Environment and store `SOCKET_SECURITY_API_KEY` in its secrets,
   set `SOCKET_SCA_ENABLED=true`, and verify the trusted workflow separately.
   Firewall Free needs no key. The authenticated job may transfer dependency
   metadata to the external provider.

If free Mend hosting's cadence later proves too slow, migrating to self-hosted
Renovate is a separate reviewed change, not a second concurrent updater.
