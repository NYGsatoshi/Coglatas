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

Every configured ecosystem uses `interval: cron` and four daily checks,
including weekends. Windows are staggered across ecosystems, in Asia/Tokyo:

| Ecosystem | Minutes past 00:00, 06:00, 12:00, 18:00 JST | PR limit |
| --- | --- | --- |
| npm (4 directories) | :00 | 20 |
| NuGet (4 project directories) | :10 | 20 |
| dotnet-sdk (root `global.json`) | :20 | 5 |
| GitHub Actions | :30 | 10 |
| Docker | :40 | 10 |
| Docker Compose | :50 | 10 |

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

Updates are **proposals only**. Major versions and new Node major versions can
be proposed, but require a separate compatibility/migration decision before
merging. An explicit Playwright image exception remains: it must match the
root locked `@playwright/test` browser runtime and is upgraded as part of a
coordinated Playwright change, not as an unrelated Docker image PR.

## Related version grouping

- npm: Angular/DevKit patch and minor updates, NgRx patch and minor updates,
  and remaining npm patch/minor updates are separate groups. Major updates
  outside those groups are separate reviewable PRs.
- NuGet: EF Core/Npgsql are grouped separately from other patch/minor
  packages. Major updates are reviewable individually.
- Docker and Compose: `dotnet/sdk` image updates are grouped separately from
  unrelated Docker images to keep the SDK coordination work small. Other
  images keep their existing grouped review.
- GitHub Actions: existing external Action SHA allowlist stays independently
  reviewed. Proposing a newer Action never automatically trusts its SHA.

An upstream package may have no new version, an incompatible lockfile, or
an unresolved vulnerability. PR creation is not proof of safety.

## Coordinated .NET SDK process

The exact stable .NET 10 SDK in `global.json` remains the *merge baseline*.
The Dockerfile, Compose, development, security and performance images should
use the same SDK patch with full reviewed SHA-256 digests. Dependabot's SDK,
Docker and Compose proposals may be generated separately. The resulting
candidate cannot merge until it passes the SDK-image parity guard; combine
the related proposals with registry-verified image digests and rerun CI.

`scripts/ci/test-dependency-update-policy.rb` is enforced in the existing
required `publication-readiness` job. It rejects missing or less frequent
polls, cooldowns, missing updater coverage and SDK-image drift, using negative
mutation tests. `scripts/ci/verify-performance-environment.py` reads the
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
