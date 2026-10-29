#!/usr/bin/env bash
# Experimental SEC-02 image build: reuse the authoritative same-revision .NET
# producer outputs, never a foreign PR, stale artifact, Main or deep lane.
set -Eeuo pipefail

security_prebuilt_shadow_allowed() {
  [[ "${GITHUB_ACTIONS:-}" == "true" &&
     "${GITHUB_EVENT_NAME:-}" == "pull_request" &&
     "${GITHUB_BASE_REF:-}" == "SecurityTestImplimation" &&
     "${COGLATAS_SECURITY_PREBUILT_SHADOW:-}" == "1" ]]
}

security_prebuilt_stamp_valid() {
  local checkout="${1:-}" producer="${2:-}" workflow="${3:-}"
  [[ "$checkout" =~ ^[0-9a-f]{40}$ &&
     "$producer" == "$checkout" &&
     "$workflow" == "$checkout" ]]
}

security_prebuilt_required_outputs() {
  local root="$1"
  [[ -s "$root/artifacts/ci/dotnet-build-sha" &&
     -s "$root/src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll" &&
     -s "$root/src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll" &&
     -s "$root/src/Coglatas.Web/obj/project.assets.json" &&
     -s "$root/src/Coglatas.Infrastructure/obj/project.assets.json" ]]
}

security_prebuilt_main() {
  local repo_root image checkout producer
  local -a build_dirs=()
  repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  cd "$repo_root"

  if ! security_prebuilt_shadow_allowed; then
    echo "::error::Prebuilt Security Runtime is restricted to opted-in PRs targeting SecurityTestImplimation." >&2
    return 1
  fi
  image="${1:-}"
  checkout="$(git rev-parse HEAD)" || return 1
  if [[ ! -s artifacts/ci/dotnet-build-sha ]]; then
    echo "::error::Exact-revision .NET producer stamp is missing." >&2
    return 1
  fi
  producer="$(tr -d '\r\n' < artifacts/ci/dotnet-build-sha)"
  if ! security_prebuilt_stamp_valid "$checkout" "$producer" "${GITHUB_SHA:-}"; then
    echo "::error::Security image producer/checkout/workflow SHA mismatch." >&2
    return 1
  fi
  if [[ "$image" != "coglatas-security-runtime:$checkout" ]]; then
    echo "::error::Security image must use the exact checked-out commit SHA." >&2
    return 1
  fi
  if ! security_prebuilt_required_outputs "$PWD"; then
    echo "::error::Security image producer is missing required Release assemblies or MSBuild assets." >&2
    return 1
  fi

  # A separate named build context avoids exposing bin/obj through the ordinary
  # repository context (or weakening .dockerignore for Main/deep images).
  security_prebuilt_stage="$(mktemp -d "${RUNNER_TEMP:-/tmp}/coglatas-security-prebuilt.XXXXXX")"
  trap 'rm -rf -- "$security_prebuilt_stage"' EXIT

  mapfile -d '' build_dirs < <(
    find src -type d \( -path '*/bin/Release' -o -name obj \) -prune -print0
  )
  if (( ${#build_dirs[@]} == 0 )); then
    echo "::error::No producer build directories available for the Security image." >&2
    return 1
  fi
  tar -cf - artifacts/ci/dotnet-build-sha "${build_dirs[@]}" |
    tar -xf - -C "$security_prebuilt_stage"
  if ! security_prebuilt_required_outputs "$security_prebuilt_stage"; then
    echo "::error::Staged Security image build context is incomplete." >&2
    return 1
  fi

  # No PR secrets or arbitrary workspace directories are passed to this context.
  docker build \\
    --build-context "security-prebuilt=$security_prebuilt_stage" \\
    --build-arg "COGLATAS_TESTED_SHA=$checkout" \\
    --file infra/docker/security-runtime-prebuilt.Dockerfile \\
    --tag "$image" .
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  security_prebuilt_main "$@"
fi
