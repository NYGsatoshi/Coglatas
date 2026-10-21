# Dependency update fast lane (review only)

## Objectives and ownership

Reduce release-to-PR latency for routine patches without bypassing the existing
review, Functional, security, accessibility, licensing, performance, RLS,
specification, or production release gates.

| Updater | Scope | Cadence | Merge |
| --- | --- | --- | --- |
| Dependabot | npm, NuGet, GitHub Actions, non-SDK Docker/Compose | npm/NuGet/Docker/Compose daily (weekdays), Actions weekly | Manual, after existing required checks |
| Renovate | Only `global.json` SDK pin and SDK Docker/Compose images | When the owner has installed/enabled the Renovate GitHub App | Manual, after existing required checks |

Renovate is **configuration only until the repository owner enables the app**;
adding `renovate.json` does not itself run Renovate. SDK Docker images are
excluded from Dependabot to avoid competing PRs. Renovate has no automatic
merging, credential-bearing workflow or deployment permission in this change.
The .NET major version remains 10 until a separately reviewed migration.

Dependabot's release cooldown is one day for patches, two days for minors, and
seven days for majors in npm/NuGet. The daily schedule normally runs on
weekdays. Security update PRs bypass normal version-update cooldowns; a
published security fix is not guaranteed a zero-latency PR or immediate
installation. Docker/Compose have a one-day release cooldown. Grouped Angular,
NgRx and EF Core/PostgreSQL patches should be reviewed within their own
compatibility boundaries rather than bundled with unrelated dependencies.

## First-time activation

1. Review `renovate.json` and enable the official Renovate GitHub App on
   `NYGsatoshi/Coglatas` with the minimum necessary installation scope. Do
   not put a Renovate token in public PR workflows or register persistent
   self-hosted runners for untrusted dependency PRs.
2. Confirm the app recognizes `global.json` as `nuget`/`dotnet-sdk` and
   updates `mcr.microsoft.com/dotnet/sdk` images (including SHA-256 digests)
   in one PR. If a file/manager is missing or a digest remains stale, **do not
   merge**; fix the update rule or coordinate the image manually.
3. Preserve `rollForward: disable`, exact Docker image digests, the
   required check topology, and the independent reviewed `main` verification.

## Review/qualification

1. Confirm the release is authentic, supported, non-prerelease, and has a
   reviewed upstream changelog/advisory. Identify whether it affects the
   build SDK, shipped ASP.NET runtime, browser bundle or deployment images.
2. Review manifest and lockfile changes, image digests, licenses, transitive
   dependency changes, SBOM/vulnerability findings, and update-script
   permissions. **Do not auto-update trust allowlists to make CI pass.**
3. For Angular, synchronize `@angular/*`, `@angular-devkit/*` and related
   compiler/build/CLI compatibility using the same candidate PR. A third-party
   exact-version assertion (including
   `frontend/scripts/angular22-third-party-compatibility.node-test.mjs`)
   must be reviewed against the updated lockfile; never delete the assertion
   just to silence a failure. Run frontend unit, architecture, licensed
   production build and relevant Playwright checks.
4. For .NET, verify `global.json`, production/dev/security Dockerfiles,
   migration and performance Compose images, Qodana tools and their approved
   historical baselines. Some packet verifiers intentionally retain historical
   exact SDK requirements; do not rewrite frozen proof artifacts or loosen
   those comparisons automatically. Run build, PostgreSQL/EF migration and
   authorization tests, security scans and all required checks.
5. Require the original PR head and resulting Main's own applicable checks,
   and any licensed acceptance/independent reviewer evidence demanded by
   existing governance. No CI failures, configured SKIPs or old-head passes
   may be silently counted as current successes.
6. Merge only after approval. Production deployment, database migration,
   rollout and school-user access remain **separate** gated decisions.
   Record detection time, PR creation, CI completion, merge and deployed-image
   identity separately to measure where release-to-adoption latency occurs.

The policy test `ruby scripts/ci/test-dependency-update-policy.rb` runs in
`publication-readiness` and rejects accidental bot overlap, weekly patch
polling, an unrestricted Renovate manager or automatic merging. It does not
replace a live Renovate configuration validation or the mandatory CI suite.
