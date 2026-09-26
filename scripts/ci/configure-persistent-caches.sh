#!/usr/bin/env bash
set -Eeuo pipefail

cache_root="${COGLATAS_CI_CACHE_ROOT:-$HOME/.cache/coglatas-ci}"
dotnet_install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet-ci}"
nuget_packages="$HOME/.nuget/packages"
nuget_http_cache="$cache_root/nuget/http-cache"
angular_cache_root="$cache_root/angular"
job_identity="${GITHUB_RUN_ID:-local}-${GITHUB_JOB:-job}-${GITHUB_RUN_ATTEMPT:-1}"

if [[ -n "${RUNNER_TEMP:-}" ]]; then
  npm_cache="$RUNNER_TEMP/npm-cache-$job_identity"
  npm_userconfig="$RUNNER_TEMP/npm-userconfig-$job_identity.npmrc"
else
  npm_cache="${NPM_CONFIG_CACHE:-$HOME/.npm}"
  npm_userconfig="${NPM_CONFIG_USERCONFIG:-$HOME/.npmrc}"
fi

mkdir -p \
  "$dotnet_install_dir" \
  "$nuget_packages" \
  "$nuget_http_cache" \
  "$angular_cache_root" \
  "$npm_cache"

if [[ -n "${RUNNER_TEMP:-}" ]]; then
  : > "$npm_userconfig"
  chmod 600 "$npm_userconfig"
fi

if [[ -n "${GITHUB_ENV:-}" ]]; then
  {
    echo "COGLATAS_CI_CACHE_ROOT=$cache_root"
    echo "DOTNET_INSTALL_DIR=$dotnet_install_dir"
    echo "DOTNET_ROOT=$dotnet_install_dir"
    echo "NUGET_PACKAGES=$nuget_packages"
    echo "NUGET_HTTP_CACHE_PATH=$nuget_http_cache"
    echo "NUGET_XMLDOC_MODE=skip"
    echo "NPM_CONFIG_CACHE=$npm_cache"
    echo "NPM_CONFIG_USERCONFIG=$npm_userconfig"
    echo "NPM_CONFIG_REGISTRY=https://registry.npmjs.org/"
    echo "NPM_CONFIG_STRICT_ALLOW_SCRIPTS=true"
    echo "NPM_CONFIG_ALLOW_GIT=none"
    echo "NPM_CONFIG_ALLOW_REMOTE=none"
    echo "NPM_CONFIG_PREFER_OFFLINE=false"
    echo "NPM_CONFIG_AUDIT=false"
    echo "NPM_CONFIG_FUND=false"
  } >> "$GITHUB_ENV"
fi

if [[ -n "${GITHUB_PATH:-}" ]]; then
  echo "$dotnet_install_dir" >> "$GITHUB_PATH"
fi

if [[ "${CI_ENABLE_ANGULAR_CACHE:-0}" == "1" && -d frontend && -f frontend/package-lock.json && -f frontend/angular.json ]]; then
  angular_key="$({
    sha256sum frontend/package-lock.json
    sha256sum frontend/angular.json
    find frontend -maxdepth 1 -type f -name 'tsconfig*.json' -print0 \
      | sort -z \
      | xargs -0 -r sha256sum
  } | sha256sum | cut -d' ' -f1)"

  angular_cache="$angular_cache_root/$angular_key"
  mkdir -p "$angular_cache" frontend/.angular
  rm -rf frontend/.angular/cache
  ln -s "$angular_cache" frontend/.angular/cache

  find "$angular_cache_root" \
    -mindepth 1 \
    -maxdepth 1 \
    -type d \
    -mtime +14 \
    -exec rm -rf {} + 2>/dev/null || true
fi

printf 'Persistent CI cache root: %s\n' "$cache_root"
printf '.NET SDK install cache: %s\n' "$dotnet_install_dir"
printf 'NuGet packages cache: %s\n' "$nuget_packages"
printf 'NuGet HTTP cache: %s\n' "$nuget_http_cache"
printf 'Per-job npm cache: %s\n' "$npm_cache"
if [[ -L frontend/.angular/cache ]]; then
  printf 'Angular build cache: %s\n' "$(readlink frontend/.angular/cache)"
fi
