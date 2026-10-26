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

## npm audit evidence validation — 2026-10-26

Source reviewed: Product Main `42ba000d9d4fc682165cb65de0a1bb61ad407481`.
The baseline checker previously treated a parsed npm error response or `{}` as
an empty vulnerability map. The workflow intentionally continues after the
audit command's nonzero exit so it can enforce the reviewed findings baseline;
therefore that continuation must require a complete audit report. A clean
report from another installation root must not offset unavailable evidence.

A deterministic control supplied a clean root report and a synthetic frontend
`{"error":{"code":"E503"}}`. The original checker exited zero. The original
checker also accepted `{}`. The first executable regression run had two passed
and two failed tests, preserving the false-green result (local RED log SHA-256
`fc4341b9703f7dfcd051753dd6ed742224f4a23013a58c0dd2a1e8e4d3b478a3`).
An earlier sandbox subprocess-launch failure provided no behavioral evidence.

The checker now requires npm report version 2, no error member, an object
vulnerability map, valid severity/advisory entries and matching nonnegative
integer per-severity/total counts. Incomplete or inconsistent evidence fails
before baseline comparison. Four test cases cover clean evidence, an unavailable
root alongside a successful root, eleven malformed-report variants, and the
unchanged reviewed/unreviewed High advisory identity behavior. All four pass
after the repair, with no skips or retries. The existing audit workflow runs
these controls before its registry requests. No baseline, severity, action pin,
permission, retry or required-check identity changes.

### Current-source dependency observations

Using Node `24.19.0` and repository-pinned npm `11.17.0`, all four unmodified
Main installation roots passed `verify-npm-lockfile.mjs`. Fresh
`npm audit --package-lock-only --json` executions all exited zero and returned
complete version-2 reports with zero findings. The repaired checker accepted
all four reports together, retaining warnings for the 25 existing baseline
entries. These are local observations, not hosted CI qualification or proof
that an earlier advisory was withdrawn.

| Installation root | Packages checked | Audit report SHA-256 |
| --- | ---: | --- |
| Root | 10 | `fe29c32f2c0c30f7303bf3ad2e75f3991707e0499bd784bb98a7e32c43dda81a` |
| `frontend` | 1,329 | `bf939cbc1e9f61631cfebb5a5298626344d4c8594fd748889ba067fa85dc15cb` |
| `coglatas-frontend` | 1,319 | `afd1c2286370cb2b858df57a7761665fd32b1629c9f350782afc3b3454eff372` |
| `tools/frontend-inspections` | 298 | `71b76903db53062026b73bb331d93367a0c2d2bf286f63f5a60f90a9e1988762` |

For #1374, both frontend graphs currently resolve `body-parser` `1.20.9` and
`undici` `8.12.3`; their previously named advisories were not reported in these
fresh executions. This candidate changes neither dependency. Independent review,
exact-head hosted evidence and resulting Main qualification remain necessary
before claiming that issue's acceptance. For #1306, `npm view braces version`
still returned `3.0.3`. No upstream remediation or advisory withdrawal is
established by the transient zero-findings reports; keep #1306 and its historical
updater/advisory evidence open. SEC-ARCH/RLS, held PRs and release gates receive
no approval or completion credit from these controls.
