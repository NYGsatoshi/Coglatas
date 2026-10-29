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
echo 'Parallel CI lane timing, failure propagation and injection controls PASS.'
