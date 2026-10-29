#!/usr/bin/env bash
# No GitHub API, Docker or credentials needed for required-gate mutation cases.
set -Eeuo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
bash -n scripts/ci/verify-frontend-parallel-gate.sh
gate="scripts/ci/verify-frontend-parallel-gate.sh"
passed=0

check_ok() {
  local producer="$1" browser="$2" unit="$3" selected="$4" playwright="$5"
  local output
  output="$(FRONTEND_PRODUCER_RESULT="$producer" FRONTEND_BROWSER_RESULT="$browser" \
    FRONTEND_UNIT_JOB_RESULT="$unit" FRONTEND_UNIT_SELECTED="$selected" \
    FRONTEND_PLAYWRIGHT_SELECTED="$playwright" bash "$gate")" || {
    echo "Expected gate acceptance: $producer/$browser/$unit/$selected/$playwright" >&2
    exit 1
  }
  [[ "$output" == *"Frontend required gate PASS"* ]] ||
    { echo "Missing explicit success evidence" >&2; exit 1; }
  ((passed += 1))
}

check_fail() {
  local producer="$1" browser="$2" unit="$3" selected="$4" playwright="$5"
  local output status
  set +e
  output="$(FRONTEND_PRODUCER_RESULT="$producer" FRONTEND_BROWSER_RESULT="$browser" \
    FRONTEND_UNIT_JOB_RESULT="$unit" FRONTEND_UNIT_SELECTED="$selected" \
    FRONTEND_PLAYWRIGHT_SELECTED="$playwright" bash "$gate" 2>&1)"
  status=$?
  set -e
  (( status != 0 )) || {
    echo "False green: $producer/$browser/$unit/$selected/$playwright" >&2
    exit 1
  }
  [[ "$output" == *"Frontend gate rejected:"* ]] ||
    { echo "Missing explicit rejection diagnostic" >&2; exit 1; }
  [[ "$output" != *"Frontend required gate PASS"* ]] ||
    { echo "Rejected gate also published a passing receipt" >&2; exit 1; }
  ((passed += 1))
}

# The independent unit job is required for BOTH full and unit-only
# routes. The browser/static job is always required. No-unit routes must
# prove that their unit job was conditionally skipped.
check_ok success success success true true
check_ok success success success true false
check_ok success success skipped false true
check_ok success success skipped false false

for result in failure cancelled skipped pending ""; do
  check_fail "$result" success success true true
  check_fail success "$result" success true true
  check_fail success success "$result" true true
  check_fail success success "$result" true false
done

for result in success failure cancelled pending ""; do
  check_fail success success "$result" false true
  check_fail success success "$result" false false
done

# Malformed GitHub output must not silently select a weaker route.
for invalid in "" TRUE 1 yes "true " skipped; do
  check_fail success success skipped "$invalid" true
  check_fail success success skipped true "$invalid"
done

echo "Required frontend aggregation passed $passed positive/negative cases."
