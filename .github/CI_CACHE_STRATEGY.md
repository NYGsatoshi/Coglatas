# CI cache strategy

## Scope and history

Active GitHub Actions workflows use GitHub-hosted runners. Paths on those runners provide storage within a job; reuse across jobs depends on the cache restore/save behavior configured by each workflow and its actions. The former persistent self-hosted arrangement is retained as [historical private-operation context](../docs/ci/self-hosted-runner-pool.md). Its warning still applies: do not attach a persistent self-hosted runner to the public repository.

This guide was checked against documentation baseline `74ab2a603b499fcd9319046bb21664f5f9b33aa3` and observed Main `ad77fd995ac4d01d892183708964ea00c86c24c0`. The inspected cache consumers use hosted Ubuntu runners at both commits; their labels change from `ubuntu-latest` in the documentation baseline to `ubuntu-24.04` in observed Main. The cache helper, root Dockerfile and Playwright Compose cache definitions are identical at these snapshots.

## Cache locations

### Host cache locations

The [cache helper](../scripts/ci/configure-persistent-caches.sh) configures local paths and environment variables; it does not restore or upload caches. It is invoked by the [cloud Qodana](workflows/qodana_cloud_quality.yml), [trusted Qodana](workflows/qodana_trusted_gate.yml), [main build artifacts](workflows/main-build-artifacts.yml) and [integration acceptance](workflows/wpc-final01-integration-acceptance.yml) workflows. These paths describe jobs that invoke the helper, rather than every workflow in the repository.

| Cache | Default location and condition |
| --- | --- |
| .NET SDK | `$HOME/.dotnet-ci`, unless `DOTNET_INSTALL_DIR` is set |
| NuGet packages | `$HOME/.nuget/packages` |
| NuGet HTTP cache | `$HOME/.cache/coglatas-ci/nuget/http-cache` |
| npm with `RUNNER_TEMP` set | `$RUNNER_TEMP/npm-cache-<run>-<job>-<attempt>`; a separate directory for the job identity |
| npm without `RUNNER_TEMP` | `NPM_CONFIG_CACHE`, falling back to `$HOME/.npm` |
| Angular build cache | `$HOME/.cache/coglatas-ci/angular/<fingerprint>`, linked from `frontend/.angular/cache` when Angular caching is enabled |

`COGLATAS_CI_CACHE_ROOT` overrides the shared root used for the NuGet HTTP and Angular caches. Angular caching requires `CI_ENABLE_ANGULAR_CACHE=1`, the `frontend` directory, `frontend/package-lock.json` and `frontend/angular.json`.

### Docker cache locations

- The root [Dockerfile](../Dockerfile) uses BuildKit cache mount IDs `coglatas-docker-npm`, `coglatas-docker-angular` and `coglatas-docker-nuget`. These IDs are unversioned. Their cache storage belongs to the Docker builder and is reusable while that storage is retained.
- The [Playwright Compose runner](../infra/compose/dev/playwright.yml) mounts external npm and Angular cache volumes. Their default names are `coglatas-playwright-npm-cache-node24-v1` and `coglatas-playwright-angular-cache-node24-v1`, overridden by `PLAYWRIGHT_NPM_CACHE_VOLUME` and `PLAYWRIGHT_ANGULAR_CACHE_VOLUME`. The [mobile baseline refresh workflow](workflows/refresh-pr445-mobile-baseline.yml) creates its configured volumes before running Compose. The `node24-v1` namespace is part of the volume names; runtime versions are selected separately by workflows and container images.

External volumes can be reused on a retained Docker host. Their names alone do not provide persistence between GitHub-hosted jobs.

## Invalidation

- NuGet and npm manage their own package-cache contents; jobs still run their restore or install commands.
- Angular caches use a fingerprint of `frontend/package-lock.json`, `frontend/angular.json` and the `tsconfig*.json` files directly under `frontend/`.
- Renaming a Docker cache mount ID selects a new cache namespace when a forced reset is required.
- Playwright cache volume names provide explicit cache namespaces. The configured external volumes must exist before Compose runs; changing their names does not remove the previous volumes.

## Cleanup

When Angular caching is enabled and the required frontend files exist, the helper removes Angular cache generation directories matching `find ... -mtime +14`. It does not prune package-manager caches, BuildKit caches or external Docker volumes.

For private or local hosts that retain package-manager or Docker storage, the historical disk-pressure monitoring and pruning guidance remains relevant. Retention on those hosts must not be assumed for GitHub-hosted jobs.
