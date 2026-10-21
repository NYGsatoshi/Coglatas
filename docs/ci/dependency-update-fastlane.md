# Rapid dependency updates — native Dependabot policy

## Goal

Shorten the interval from upstream patch publication to a *reviewed* Coglatas
PR without weakening Functional, licensed, RLS, security, supply-chain,
accessibility, performance, Code Owner, or target-release checks. No automatic
merging or production deployment is introduced.

## Single updater

Use **Dependabot only** (no Renovate GitHub App). As supported by GitHub since
November 2024, the `dotnet-sdk` package ecosystem updates the SDK version
in `global.json`. The existing npm, NuGet, Docker, Docker Compose and GitHub
Actions ecosystem entries remain.

| Ecosystem | Schedule | Grouping |
| --- | --- | --- |
| npm | Daily (weekdays) | Angular/DevKit, NgRx, other npm, security |
| NuGet | Daily (weekdays) | EF Core/Npgsql, other NuGet, security |
| dotnet-sdk | Daily (weekdays), **no cooldown** | Root `global.json` only; no unreviewed major upgrade |
| Docker / Docker Compose | Daily (weekdays) | Existing image update groups; SDK images remain monitored |
| GitHub Actions | Weekly | Existing SHA allowlist review policy |

Normal npm/NuGet patch updates have a one-day cooldown; minor/major updates
use two/seven days respectively. Docker image updates have a one-day cooldown.
Security update PRs are independent of normal version-update cooldowns.
GitHub Dependabot does **not** generate .NET SDK security alerts through
`dotnet-sdk`, so urgent SDK advisories still need external advisory monitoring.

## Coordinating SDK and images

The SDK pin in `global.json` is the review source of truth. Dockerfile,
Compose and development/security/performance SDK image tags must match the
same SDK patch and retain full SHA-256 digests.

Dependabot may create the `dotnet-sdk`, `docker` and `docker-compose`
update proposals independently. This change deliberately **does not**
unconditionally group all Docker images with SDK updates: doing so would
produce overly broad dependency PRs. An SDK-only or image-only update is
**not mergeable** while the SDK/image parity check fails. The reviewer should
combine the matching Dependabot proposals (or update the exact image tag
and registry-verified digest) into a coordinated candidate, then rerun CI.

`ruby scripts/ci/test-dependency-update-policy.rb` runs in the required
`publication-readiness` workflow. It checks the native SDK updater,
cadence, no disabled SDK image coverage, root SDK exact pin, immutable image
references, and matching SDK versions, with fail-closed negative controls.
`scripts/ci/verify-performance-environment.py` also derives the expected
SDK image tag from `global.json` rather than hardcoding a duplicated SDK
patch version.

Do not replace an image digest with a guess, or remove the SHA requirement
to pass a bot PR. An independent candidate may later use Dependabot
multi-ecosystem groups once the exact SDK-only grouping has been validated
against the real repo, without weakening coverage of *other* Docker updates.

## Mandatory compatibility and release review

1. Confirm the upstream release provenance and supported version band. Review
   manifests, lockfiles, transitive packages, SBOM, advisory severity, license
   changes, and pinned image digests.
2. For Angular patches, preserve compatible `@angular/*`, Angular CLI and
   DevKit versions, reviewed Syncfusion integration, frontend unit, Storybook,
   licensing and relevant Playwright checks. Do not erase exact-version
   compatibility assertions solely to silence a failing test.
3. For .NET, review `global.json`, production ASP.NET Runtime (distinct from
   the SDK), all SDK images, migrations, PostgreSQL/EF integration and security
   test owners. Historical Qodana packet proof hashes and old SDK baselines
   are intentionally immutable; never silently rewrite them.
4. Require all PR-head checks, required independent review and resulting-Main
   applicable CI. A green earlier head never qualifies a later one.
5. Treat release, host, database migrations, rollback and school-user access
   as separate approval gates. Updating an SDK is **not** production deployment.

Measure these intervals independently: release publication -> detection ->
PR opened -> required CI passed -> merged -> deployed image identity recorded.

References:
- https://github.blog/changelog/2024-11-19-dependabot-can-now-perform-version-updates-for-the-net-sdk/
- https://docs.github.com/en/code-security/reference/supply-chain-security/supported-ecosystems-and-repositories
