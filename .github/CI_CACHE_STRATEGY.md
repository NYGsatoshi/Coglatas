# Self-hosted CI cache strategy

The self-hosted workflows intentionally prefer persistent caches on each runner user's local disk instead of transferring dependency caches to and from GitHub-hosted cache storage on every run.

## Cache locations

- .NET SDK: `$HOME/.dotnet-ci`
- NuGet packages: `$HOME/.nuget/packages`
- NuGet HTTP cache: `$HOME/.cache/coglatas-ci/nuget/http-cache`
- npm package cache: npm's standard `$HOME/.npm`
- Angular build cache: `$HOME/.cache/coglatas-ci/angular/<toolchain-fingerprint>`
- Docker BuildKit caches: persistent cache mounts managed by the Docker builder
- Playwright container npm and Angular caches: versioned external Docker volumes

## Invalidation

- NuGet and npm caches are content-addressed by their package managers.
- Angular caches use a fingerprint of the frontend lockfile, Angular workspace configuration, and root TypeScript configurations.
- Docker cache mount IDs are versioned in the Dockerfile and can be renamed when a forced reset is required.
- Playwright cache volume names include the Node major version and an explicit schema version.

## Cleanup

Angular cache generations older than 14 days are removed when frontend workflows configure the cache. Docker builder and package-manager caches remain available across jobs and should be pruned only when disk-pressure monitoring indicates a need.
