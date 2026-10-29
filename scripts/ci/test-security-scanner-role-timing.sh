#!/usr/bin/env bash
# Exercise the actual SEC-04 and SEC-06 role functions with no network/Docker.
# Timings must remain advisory and never turn scanner/processor failure green.
set -Eeuo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

bash -n scripts/security/schemathesis-runner.sh
bash -n scripts/security/zap-runner.sh

tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
unset GITHUB_STEP_SUMMARY || true
export SECURITY_SCAN_TARGET="http://app:8080"
export SECURITY_SCAN_STATE_DIR="$tmp"
export SECURITY_SCAN_HTTP_STATE_DIR="/state"
export SECURITY_ZAP_ADDON_LIST_SHA256="$(printf '%064d' 0)"

# Source code under test, then replace only its external I/O boundaries.
source scripts/security/schemathesis-runner.sh
source scripts/security/zap-runner.sh

fail() {
  printf 'Security scanner role timing regression failed: %s\n' "$*" >&2
  exit 1
}

security_schemathesis_role_seed() { printf '574042\n'; }
security_scan_host_path() { printf '%s/%s\n' "$tmp" "$1"; }
security_schemathesis_prepare_auth_file() { printf '{}\n' > "$2"; }
security_scan_redact_stream() { cat; }
security_zap_redact_stream() { cat; }
security_scan_verify_context() { return 0; }
security_scan_fetch_csrf() {
  SECURITY_SCAN_CSRF_TOKEN="synthetic-private-canary"
  SECURITY_SCAN_CSRF_HEADER_NAME="X-CSRF-Token"
}
security_scan_role_tenant() { printf 'security-alpha\n'; }
security_zap_cookie_header() { printf 'auth=synthetic-private-canary\n'; }
security_zap_target_regex() { printf 'http://app:8080\n'; }
security_zap_forbidden_values_json() { printf '["synthetic-private-canary"]\n'; }
security_scan_health() { return 0; }

# Never launch any Docker or scanner binary; every process exit is synthetic.
docker() {
  if [[ "$1" == rm && "$2" == -f ]]; then
    return 0
  fi
  if [[ "$1" == run ]]; then
    # Intercept only CLI argument structure; no Docker/network/real secrets.
    local previous="" observed=""
    for argument in "$@"; do
      if [[ "$previous" == "--workers" ]]; then
        observed="$argument"
        break
      fi
      previous="$argument"
    done
    if [[ -n "$observed" && "$observed" != "$FAKE_EXPECTED_WORKERS" ]]; then
      fail "SEC-04 worker CLI mismatch (observed vs shadow policy)"
    fi
    return "$FAKE_SCANNER_EXIT"
  fi
  fail "unexpected Docker invocation"
}
timeout() {
  [[ "$1" == "--signal=TERM" && "$2" == "--kill-after=30s" ]] ||
    fail "unexpected ZAP timeout controls"
  return "$FAKE_SCANNER_EXIT"
}
python3() {
  case "$1" in
    scripts/security/render-zap-plan.py)
      printf '%s\n' '# synthetic plan only' > "$3"
      return 0
      ;;
    scripts/security/process-schemathesis-report.py|scripts/security/process-zap-report.py)
      return "$FAKE_PROCESS_EXIT"
      ;;
    *) fail "unexpected Python invocation" ;;
  esac
}

run_one() {
  local tool="$1" role="$2" scanner_status="$3" processor_status="$4" expect_success="$5"
  local output result marker count
  export FAKE_SCANNER_EXIT="$scanner_status" FAKE_PROCESS_EXIT="$processor_status"
  export FAKE_EXPECTED_WORKERS="${6:-1}"

  set +e
  if [[ "$tool" == "schemathesis" ]]; then
    output="$(security_schemathesis_run_role pr "$role" 574042 fake-network "$tmp" 2>&1)"
    result=$?
  else
    output="$(security_zap_run_role "$role" fake-network "$tmp" 2>&1)"
    result=$?
  fi
  set -e

  if [[ "$expect_success" == "yes" ]]; then
    (( result == 0 )) || fail "$tool unexpectedly rejected successful synthetic scan"
  else
    (( result != 0 )) || fail "$tool masked scanner/processor failure"
  fi

  marker="CI security scanner timing: tool=$tool role=$role "
  count="$(grep -Fc "$marker" <<< "$output" || true)"
  [[ "$count" == "1" ]] || fail "$tool printed $count role timing records (expected one)"
  grep -Eq "^CI security scanner timing: tool=$tool role=$role elapsed_seconds=[0-9]+ scanner_exit=$scanner_status$" <<< "$output" ||
    fail "$tool role timing did not preserve native scanner exit $scanner_status"
  [[ "$output" != *"synthetic-private-canary"* ]] ||
    fail "$tool leaked synthetic authorization material into timing/log output"
}

# A genuine dual-worker trial is reachable only in one synthetic, trusted
# PR/branch/role combination. Every other lane and role stays at one worker.
for changed in push workflow_dispatch pull_request_target; do
  [[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME="$changed" GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 security_schemathesis_worker_count pr anonymous)" == 1 ]] ||
    fail "SEC-04 accepted untrusted event"
done
for changed in main securitytestimplimation SecurityTestImplementation ""; do
  [[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF="$changed" COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 security_schemathesis_worker_count pr anonymous)" == 1 ]] ||
    fail "SEC-04 accepted untrusted base"
done
for changed in "" 0 true 3; do
  [[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW="$changed" security_schemathesis_worker_count pr anonymous)" == 1 ]] ||
    fail "SEC-04 accepted invalid worker opt-in"
done
for changed in alpha-owner alpha-restricted alpha-member beta-owner; do
  [[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 security_schemathesis_worker_count pr "$changed")" == 1 ]] ||
    fail "SEC-04 authenticated workers must remain one"
done
[[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 security_schemathesis_worker_count deep anonymous)" == 1 ]] ||
  fail "SEC-04 deep lane must remain one worker"
[[ "$(GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 security_schemathesis_worker_count pr anonymous)" == 2 ]] ||
  fail "SEC-04 isolated anonymous trial unavailable"

run_one schemathesis anonymous 0 0 yes
GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SEC04_ANON_WORKERS_SHADOW=1 \
  run_one schemathesis anonymous 0 0 yes 2
run_one schemathesis alpha-restricted 19 0 no
run_one schemathesis alpha-owner 0 23 no

run_one zap alpha-owner 0 0 yes
run_one zap alpha-restricted 19 0 no
run_one zap beta-owner 0 23 no

printf 'SEC-04/SEC-06 per-role timing, fail-closed statuses and non-secret logging PASS.\n'
