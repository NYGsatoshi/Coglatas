#!/usr/bin/env bash
# No network, Docker or privileged CI permissions; test the orchestration only.
set -Eeuo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
lib="$root/scripts/ci/parallel-lane-lib.sh"
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT

# Successful workers run in parallel and produce independent finite timings.
(
  export CI_PARALLEL_LOG_DIR="$tmp/ok-logs"
  export GITHUB_STEP_SUMMARY="$tmp/ok-summary.md"
  source "$lib"
  ci_parallel_start security-core bash -c 'sleep 1; echo core-output'
  ci_parallel_start security-zap bash -c 'sleep 1; echo zap-output'
  ci_parallel_wait
) > "$tmp/ok-stdout" 2>&1

grep -Fq 'All selected parallel CI lanes passed.' "$tmp/ok-stdout"
grep -Fq 'core-output' "$tmp/ok-stdout"
grep -Fq 'zap-output' "$tmp/ok-stdout"
grep -Fq '| security-core | PASS |' "$tmp/ok-summary.md"
grep -Fq '| security-zap | PASS |' "$tmp/ok-summary.md"
for name in security-core security-zap; do
  grep -Eq '^[0-9]{1,8}$' "$tmp/ok-logs/$name.seconds"
done

# Native failure must propagate without discarding other workers' evidence.
if (
  export CI_PARALLEL_LOG_DIR="$tmp/fail-logs"
  export GITHUB_STEP_SUMMARY="$tmp/fail-summary.md"
  source "$lib"
  ci_parallel_start failure-case bash -c 'echo fail-output; exit 17'
  ci_parallel_start passing-case bash -c 'echo pass-output'
  ci_parallel_wait
) > "$tmp/fail-stdout" 2>&1; then
  echo 'The failing parallel lane incorrectly passed.' >&2
  exit 1
fi

grep -Fq 'One or more parallel CI lanes failed.' "$tmp/fail-stdout"
grep -Fq '| failure-case | FAIL (exit 17) |' "$tmp/fail-summary.md"
grep -Fq '| passing-case | PASS |' "$tmp/fail-summary.md"
grep -Fq 'fail-output' "$tmp/fail-stdout"
grep -Fq 'pass-output' "$tmp/fail-stdout"

# Reject path/summary injection, duplicate names and zero-argument workers.
(
  export CI_PARALLEL_LOG_DIR="$tmp/reject-logs"
  unset GITHUB_STEP_SUMMARY || true
  source "$lib"
  if ci_parallel_start '../escape' bash -c ':'; then exit 1; fi
  if ci_parallel_start 'unsafe|name' bash -c ':'; then exit 1; fi
  if ci_parallel_start empty-lane; then exit 1; fi
  ci_parallel_start safe-lane bash -c 'echo sanitized'
  if ci_parallel_start safe-lane bash -c 'echo duplicate'; then exit 1; fi
  ci_parallel_wait
) > "$tmp/reject-stdout" 2>&1

grep -Fq 'sanitized' "$tmp/reject-stdout"
if [[ -e "$tmp/escape.log" ]]; then
  echo 'Invalid lane escaped the log root.' >&2
  exit 1
fi

# Unavailable duration remains explicitly unknown, not fabricated zero.
(
  export CI_PARALLEL_LOG_DIR="$tmp/missing-logs"
  export GITHUB_STEP_SUMMARY="$tmp/missing-summary.md"
  source "$lib"
  ci_parallel_start missing-time bash -c 'sleep 0.1; echo finished'
  wait "${CI_PARALLEL_PIDS[0]}"
  rm -f -- "${CI_PARALLEL_TIMINGS[0]}"
  ci_parallel_wait
) > "$tmp/missing-stdout" 2>&1

grep -Fq '| missing-time | PASS | unavailable |' "$tmp/missing-summary.md"

# A real security lane installs an EXIT cleanup trap *inside the worker*
# process. The outer timer must survive it; a false exit must still fail.
# The previous timer implementation was overwritten by this nested trap.
(
  export CI_PARALLEL_LOG_DIR="$tmp/trap-logs"
  export GITHUB_STEP_SUMMARY="$tmp/trap-summary.md"
  source "$lib"
  cleanup_success() {
    trap 'echo cleanup-success-executed' EXIT
    sleep 1
    echo native-success
  }
  ci_parallel_start real-lane cleanup_success
  ci_parallel_wait
) > "$tmp/trap-success-stdout" 2>&1

grep -Fq 'native-success' "$tmp/trap-success-stdout"
grep -Fq 'cleanup-success-executed' "$tmp/trap-success-stdout"
duration="$(cat "$tmp/trap-logs/real-lane.seconds")"
if [[ ! "$duration" =~ ^[0-9]{1,8}$ ]]; then
  echo 'Missing bounded lane timing despite cleanup trap.' >&2
  exit 1
fi
grep -Fq '| real-lane | PASS |' "$tmp/trap-summary.md"
if grep -Fq 'elapsed_seconds=unavailable' "$tmp/trap-success-stdout"; then
  echo 'Nested cleanup trap suppressed real lane timing.' >&2
  exit 1
fi

# A conditional subshell suppresses Bash errexit inside called functions.
# Capture the aggregate status explicitly instead of executing under "if (".
set +e
(
  export CI_PARALLEL_LOG_DIR="$tmp/trap-fail-logs"
  export GITHUB_STEP_SUMMARY="$tmp/trap-fail-summary.md"
  source "$lib"
  cleanup_failure() {
    trap 'echo cleanup-failure-executed' EXIT
    false
    echo FALSE_GREEN
  }
  ci_parallel_start failure-with-cleanup cleanup_failure
  ci_parallel_wait
) > "$tmp/trap-fail-stdout" 2>&1
nested_status=$?
set -e
if ((nested_status == 0)); then
  echo 'Nested-trap lane incorrectly masked a native command failure.' >&2
  exit 1
fi

grep -Fq 'cleanup-failure-executed' "$tmp/trap-fail-stdout"
duration="$(cat "$tmp/trap-fail-logs/failure-with-cleanup.seconds")"
if [[ ! "$duration" =~ ^[0-9]{1,8}$ ]]; then
  echo 'Missing failure-lane timing despite cleanup trap.' >&2
  exit 1
fi
grep -Fq '| failure-with-cleanup | FAIL (exit 1) |' "$tmp/trap-fail-summary.md"
if grep -Fq 'FALSE_GREEN' "$tmp/trap-fail-stdout"; then
  echo 'The failing command proceeded beyond errexit.' >&2
  exit 1
fi

echo 'Parallel CI lane timing, failure propagation and nested cleanup controls PASS.'

