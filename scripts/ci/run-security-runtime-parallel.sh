#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

: "${COGLATAS_SECURITY_CI_PASSWORD:?COGLATAS_SECURITY_CI_PASSWORD is required}"

# shellcheck source=scripts/ci/parallel-lane-lib.sh
source scripts/ci/parallel-lane-lib.sh

run_security_stage() {
  local stage="$1"
  local began="$SECONDS"
  local rc=0
  if bash scripts/ci/run-security-runtime-smoke.sh "$stage"; then
    :
  else
    rc=$?
  fi
  # Only trusted stage names, elapsed time and native result are published.
  # Failed stages retain their real exit code and trigger the existing cleanup.
  printf 'CI security stage: %s exit=%d elapsed_seconds=%d\n' \
    "$stage" "$rc" "$((SECONDS - began))"
  return "$rc"
}

run_runtime_lane() {
  local kind="$1"
  export COGLATAS_SECURITY_CI_PROJECT="coglatas-security-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-${kind}"
  export COGLATAS_SECURITY_RUNTIME_STATE_DIR="${RUNNER_TEMP:-/tmp}/coglatas-security-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-${kind}"

  local failed=true
  cleanup() {
    COGLATAS_SECURITY_RUNTIME_FAILED="$failed" \
      bash scripts/ci/run-security-runtime-smoke.sh cleanup || true
  }
  trap cleanup EXIT

  run_security_stage prepare
  run_security_stage sec03

  case "$kind" in
    core)
      run_security_stage sec05
      run_security_stage aud02
      ;;
    schemathesis)
      run_security_stage sec04
      ;;
    zap)
      run_security_stage sec06
      ;;
    *)
      echo "Unknown security runtime lane '$kind'." >&2
      return 2
      ;;
  esac

  failed=false
  cleanup
  trap - EXIT
}

if [[ "$#" -gt 1 ]]; then
  echo 'Expected at most one security runtime lane selection.' >&2
  exit 2
fi
case "${1-all}" in
  all) lanes=(core schemathesis zap) ;;
  core-schemathesis) lanes=(core schemathesis) ;;
  zap) lanes=(zap) ;;
  *) echo 'Unsupported security runtime lane selection.' >&2; exit 2 ;;
esac
for kind in "${lanes[@]}"; do
  ci_parallel_start "security-$kind" run_runtime_lane "$kind"
done

ci_parallel_wait
