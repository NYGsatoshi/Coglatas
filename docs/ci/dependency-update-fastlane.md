# Prompt dependency update PRs — Dependabot-only fast lane

## Objective and risk boundary

When supported upstream dependencies publish a stable version, propose a PR
**as soon as the next Dependabot scan permits**, without waiting for an
artificial cooldown. This is *not* auto-merge, production deployment, or proof
that a just-published release is safe. A GitHub schedule is best-effort, not
an upstream release webhook or a guaranteed publication-to-PR SLA.

Dependabot is the only update bot. It supports `dotnet-sdk` for the SDK version
in root `global.json`. Existing npm, NuGet, GitHub Actions, Docker and Compose
manifests remain monitored. Security updates use their independent advisory
path and are not delayed by a version-update cooldown.

## Poll schedule and PR behavior

Every configured ecosystem uses `interval: cron` **once every 24 hours**,
including weekends. Dependabot rejects cron expressions whose minimum interval
is shorter than 24 hours. Starts are staggered in Asia/Tokyo:

| Ecosystem | Daily check time (JST) | PR limit |
| --- | --- | --- |
| npm (4 directories) | 03:00 | 20 |
| NuGet (4 project directories) | 03:10 | 20 |
| dotnet-sdk (root `global.json`) | 03:20 | 5 |
| GitHub Actions | 03:30 | 10 |
| Docker | 03:40 | 10 |
| Docker Compose | 03:50 | 10 |

For example, the npm schedule is:

```yaml
schedule:
  interval: cron
  cronjob: "0 3 * * *"
  timezone: Asia/Tokyo
```

This is the fastest valid **native Dependabot cron cadence**, not continuous
release-event monitoring. Runs may start later due to GitHub scheduling.
To check an update earlier, the repository operator may manually run
**Check for updates** in GitHub's dependency update interface.

All six entries explicitly set:

```yaml
cooldown:
  exclude:
    - "*"
```

GitHub's default version-update cooldown is three days; the wildcard
exclusion overrides that default for **all** versions, including patch, minor
and major updates. Dependabot will still respect manifest constraints,
registry availability, supported versions, existing open PRs, updater errors,
and the configured PR limits. Therefore a new upstream release may generate
no new PR at a given scan. Already-open PRs may be refreshed instead.

Updates are **proposals only**, with a deliberate exception for Node:
the reviewed production build baseline is **Node 24 LTS**. Docker and Compose
Dependabot entries ignore only Node's `version-update:semver-major` candidates,
so updates on the existing Node 24 image tag (including digest-only changes)
remain eligible. Dependabot does not offer an automatic LTS-status filter;
the owner must explicitly lift this hold when the next Node major becomes LTS,
after toolchain/Angular/CI compatibility review. This does not prohibit normal
major update proposals for unrelated dependencies.

An explicit Playwright image exception remains: it must match the root locked
`@playwright/test` browser runtime and is upgraded as part of a coordinated
Playwright change, not as an unrelated Docker image PR.

## Related version grouping

- npm: Angular/DevKit patch and minor updates, NgRx patch and minor updates,
  and remaining npm patch/minor updates are separate groups. Major updates
  outside those groups are separate reviewable PRs.
- NuGet: EF Core/Npgsql are grouped separately from other patch/minor
  packages. Major updates are reviewable individually.
- Docker and Compose: `dotnet/sdk` image updates are grouped separately from
  unrelated Docker images to keep the SDK coordination work small. Other
  images keep their existing grouped review, but a Node major that is not yet
  an approved LTS baseline cannot be added to that group.
- GitHub Actions: existing external Action SHA allowlist stays independently
  reviewed. Proposing a newer Action never automatically trusts its SHA.

An upstream package may have no new version, an incompatible lockfile, or
an unresolved vulnerability. PR creation is not proof of safety.

### Manifest discovery limitation

Dependabot monitors the configured Dockerfile/Compose directories, including
`/infra/docker`, `/infra/compose/performance`, and `/deploy/onprem`.
Its Docker Compose file fetcher currently recognizes Compose-style filenames,
not arbitrary `pr.yml`, `environment.yml`, `local.yml` or
`real-backend-smoke.yml` names. Listing a directory therefore **does not
prove those files will receive automatic image PRs**. They remain explicit
SDK-coordination candidates; the exact-version/image validation rejects
inconsistent PRs until each affected manifest is synchronized. Check actual
Dependabot output before claiming complete automatic image coverage.

### Immutable image pins and SonarCloud S8431

Keep Docker image references in `name:human-readable-tag@sha256:<digest>`
format. The digest fixes the fetched image; the tag preserves reviewable
version intent and allows Dependabot's normal version/digest updates.

SonarSource documented in September 2026 that `docker:S8431` should **not**
report tag-plus-digest references for projects with supported Dependabot or
Renovate configuration. Coglatas uses `.github/dependabot.yml`.
A separate October 2026 report describes S8431 false positives in PR
analysis even when dependency-bot configuration exists.

Keep the intentional **project-level** `Ignore Issues on Multiple Criteria`
entry for this narrow exception while upstream false positives remain:

- **Rule Key Pattern:** `docker:S8431`
- **File Path Pattern:** `**/*Dockerfile`

The glob matches the root Dockerfile and `infra/docker/*.Dockerfile`
without suppressing other Docker rules or excluding the files from analysis.
Every criterion requires both fields. An S8431 entry with the file pattern
missing causes the CI-based scanner's `end` phase to fail **before uploading
the report**, as confirmed in PR #1470's October 28 hosted log.

Retain this reviewed scope rather than resetting all issue exclusions,
turning off S8431 globally in the Quality Profile, broadening the rule to
`*`, removing image digests, or excluding Dockerfiles from analysis.
The .NET scanner does not automatically ingest repository
`.sonarcloud.properties`: repair the criterion in the existing
SonarQube Cloud project's `Administration > General Settings >
Analysis Scope > Issues > Ignore Issues on Multiple Criteria`, and
verify new PR/Main scans afterward. Any future removal of the exception
requires confirmed upstream PR parity and an independently reviewed scan.

- SonarSource: https://community.sonarsource.com/t/sonar-community-roundup-sept-5-sept-11/188147
- Node release/LTS schedule: https://github.com/nodejs/Release/blob/main/README.md

## Coordinated .NET SDK process

The exact stable .NET 10 SDK in `global.json` remains the *merge baseline*.
The Dockerfile, Compose, development, security and performance images should
use the same SDK patch with full reviewed SHA-256 digests. Dependabot's SDK,
Docker and Compose proposals may be generated separately. The resulting
candidate cannot merge until it passes the SDK-image parity guard; combine
the related proposals with registry-verified image digests and rerun CI.

`scripts/ci/test-dependency-update-policy.rb` is enforced in the existing
required `publication-readiness` job. It rejects missing update entries, unsupported subdaily cron expressions,
weekend omissions, cooldowns, missing updater coverage and SDK-image drift,
using negative mutation tests. `scripts/ci/verify-performance-environment.py` reads the
current SDK patch from `global.json` rather than embedding a stale constant.

The `dotnet-sdk` Dependabot version-update ecosystem does **not** issue
SDK-specific security alerts. Monitor security advisories separately.
Additionally, SDK updates and the shipped ASP.NET runtime are distinct:
a green SDK upgrade cannot substitute for a patched runtime deployment.

## Merge and release remain gated

1. Review upstream release authenticity, provenance, licenses, SBOM and
   advisories. Updates discovered quickly may introduce unvetted code.
2. Enforce existing CI, CodeQL, trust boundaries, security and functional
   checks. Untrusted Dependabot PRs must not receive credentials or writable
   production authority.
3. Angular: verify Core/CLI/DevKit alignment, Syncfusion license constraints,
   reviewed exact-version compatibility contracts, frontend units and browser
   tests. Do not bypass tests to make a fresh patch pass.
4. .NET: verify `global.json`, all image SHA pins, EF/PostgreSQL integration,
   runtime security owners and relevant Qodana tools. Historical verification
   packet SHA evidence remains immutable and must not be silently rewritten.
5. Require exact-head CI, independent review and new Main CI after merging.
   Production deployment, database migration, rollback and school access
   remain independently authorized.
6. If PR limits are exhausted or failing candidates accumulate, triage stale
   PRs without auto-merging. Track detection → opened → validated → merged →
   deployed timestamps independently.

## Official references

- https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference
- https://github.blog/changelog/2024-11-19-dependabot-can-now-perform-version-updates-for-the-net-sdk/
- https://github.blog/changelog/2025-04-22-dependabot-now-lets-you-schedule-update-frequencies-with-cron-expressions/
