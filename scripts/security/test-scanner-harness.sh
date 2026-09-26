#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
source "${HARNESS_PATH:-$repo_root/scripts/security/scanner-harness.sh}"

fail() {
  printf 'SEC-03 harness contract test failed: %s\n' "$*" >&2
  exit 1
}

assert_target_allowed() {
  local target=$1
  security_scan_validate_target "$target" >/dev/null || fail "expected target to be allowed: $target"
}

assert_target_rejected() {
  local target=$1
  if security_scan_validate_target "$target" >/dev/null 2>&1; then
    fail "expected target to be rejected before network access: $target"
  fi
}

assert_target_allowed "http://localhost:8080"
assert_target_allowed "https://api.localhost:8443"
assert_target_allowed "http://127.0.0.1:8080"
assert_target_allowed "http://[::1]:8080"
assert_target_rejected "http://app:8080"
(
  export SECURITY_SCAN_TRANSPORT_KIND=compose
  assert_target_allowed "http://app:8080"
)
assert_target_rejected "http://10.23.4.5:8080"
assert_target_rejected "http://prod:8080"
assert_target_rejected "https://example.com"
assert_target_rejected "https://portal.example.org"
assert_target_rejected "http://localhost.evil.example"
assert_target_rejected "http://app:8080/api"
assert_target_rejected "http://user@localhost:8080"
assert_target_rejected "ftp://localhost/resource"

COGLATAS_SECURITY_CI_EPHEMERAL_ORIGIN="https://sec03-run.example.test" GITHUB_ACTIONS=true \
  assert_target_allowed "https://sec03-run.example.test"
COGLATAS_SECURITY_CI_EPHEMERAL_ORIGIN="https://sec03-run.example.test" GITHUB_ACTIONS=false \
  assert_target_rejected "https://sec03-run.example.test"
COGLATAS_SECURITY_CI_EPHEMERAL_ORIGIN="https://public.example.com" GITHUB_ACTIONS=true \
  assert_target_rejected "https://public.example.com"

ASPNETCORE_ENVIRONMENT=Test COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true COGLATAS_SECURITY_CI_PASSWORD=dummy \
  security_scan_require_boundary || fail "valid Test boundary was rejected"
if ASPNETCORE_ENVIRONMENT=Production COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true COGLATAS_SECURITY_CI_PASSWORD=dummy \
  security_scan_require_boundary >/dev/null 2>&1; then
  fail "Production boundary bypassed Test-only guard"
fi
if ASPNETCORE_ENVIRONMENT=Test COGLATAS_SECURITY_CI_FIXTURE_ENABLED=false COGLATAS_SECURITY_CI_PASSWORD=dummy \
  security_scan_require_boundary >/dev/null 2>&1; then
  fail "missing SEC-02 activation marker bypassed guard"
fi

redacted="$(
  COGLATAS_SECURITY_CI_PASSWORD='scanner-password-value' security_scan_redact_stream <<'LOG'
Cookie: auth=secret-cookie
Set-Cookie: auth=secret-cookie
X-CSRF-Token: csrf-secret
Authorization: Bearer authorization-secret
Proxy-Authorization: Basic proxy-secret
{"password":"scanner-password-value","token":"token-secret","accessToken":"access-secret","refreshToken":"refresh-secret","csrfToken":"csrf-json-secret","safe":"kept"}
LOG
)"
for secret in scanner-password-value secret-cookie csrf-secret authorization-secret proxy-secret token-secret access-secret refresh-secret csrf-json-secret; do
  [[ "$redacted" != *"$secret"* ]] || fail "secret '$secret' leaked through redactor"
done
[[ "$redacted" == *'"safe":"kept"'* ]] || fail "redactor removed unrelated log content"

for role in alpha-owner alpha-member alpha-restricted beta-owner; do
  tenant="$(security_scan_role_tenant "$role")"
  email="$(security_scan_role_email "$role")"
  [[ "$email" == *@example.test ]] || fail "$role does not use a synthetic identity"
  [[ "$tenant" == security-alpha || "$tenant" == security-beta ]] || fail "$role mapped to unexpected tenant"
done

workflow="$repo_root/.github/workflows/ci.yml"
if [[ -f "$workflow" ]]; then
  if grep -Fq 'ci_dummy_security_fixture_password' "$workflow"; then
    fail "static SEC-03 synthetic password remains persisted in workflow YAML"
  fi
  grep -Fq "::add-mask::%s" "$workflow" || fail "SEC-03 CI credential is not masked before export"
  grep -Fq "COGLATAS_SECURITY_CI_PASSWORD=%s" "$workflow" || fail "SEC-03 CI credential is not generated into runner-only GITHUB_ENV"
fi

# Caller-provided directories are parents only. The harness owns and deletes a
# dedicated mktemp child, never the parent or arbitrary caller path.
(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  touch "$parent/caller-sentinel"
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=contract-dummy-password
  export SECURITY_SCAN_STATE_PARENT="$parent"
  security_scan_init "http://localhost:8080"
  child="$SECURITY_SCAN_STATE_DIR"
  [[ "$child" == "$parent"/coglatas-security-scanner.* ]] || exit 11
  touch "$child/session-material"
  security_scan_cleanup
  [[ ! -e "$child" ]] || exit 12
  [[ -f "$parent/caller-sentinel" ]] || exit 13
) || fail "harness-owned state cleanup removed caller-owned data or leaked its child directory"

# Legacy/direct derived state-dir injection is rejected, so a typo such as /tmp
# cannot become an rm -rf target.
(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=contract-dummy-password
  export SECURITY_SCAN_STATE_DIR="$parent"
  if security_scan_init "http://localhost:8080" >/dev/null 2>&1; then
    exit 21
  fi
  [[ -d "$parent" ]] || exit 22
) || fail "caller-provided SECURITY_SCAN_STATE_DIR bypassed ownership guard"

# A Compose hostname is not sufficient. Init additionally requires a transport
# guard; the production runtime guard proves the app container is attached to the
# isolated Compose network.
(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=contract-dummy-password
  export SECURITY_SCAN_TRANSPORT_KIND=compose
  export SECURITY_SCAN_STATE_PARENT="$parent"
  if security_scan_init "http://app:8080" >/dev/null 2>&1; then
    exit 31
  fi
) || fail "Compose service target initialized without a transport guard"

(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=contract-dummy-password
  export SECURITY_SCAN_TRANSPORT_KIND=compose
  export SECURITY_SCAN_STATE_PARENT="$parent"
  security_scan_transport_guard() { [[ "$1" == "http://app:8080" ]]; }
  security_scan_init "http://app:8080"
  security_scan_cleanup
) || fail "valid transport-bound Compose target was rejected"

# Simulate successful preflight without network I/O, then turn xtrace on. Every
# credential-bearing HTTP entry must reject before the transport is invoked, and
# the trace itself must not contain the synthetic password.
(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  trace="$parent/xtrace.log"
  calls="$parent/http-calls"
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=xtrace-contract-password
  export SECURITY_SCAN_STATE_PARENT="$parent"
  security_scan_init "http://localhost:8080"
  security_scan_health() { return 0; }
  security_scan_bootstrap_role() { return 0; }
  security_scan_verify_wrong_password_rejected() { return 0; }
  security_scan_preflight >/dev/null
  security_scan_curl() { printf 'called\n' >> "$calls"; return 99; }

  set +e
  set -x
  security_scan_fetch_csrf alpha-owner 2>>"$trace"
  csrf_status=$?
  security_scan_logout_role alpha-owner 2>>"$trace"
  logout_status=$?
  set +x
  set -e

  [[ "$csrf_status" -ne 0 && "$logout_status" -ne 0 ]] || exit 41
  [[ ! -s "$calls" ]] || exit 42
  ! grep -Fq "$COGLATAS_SECURITY_CI_PASSWORD" "$trace" || exit 43
  security_scan_cleanup
) || fail "xtrace guard did not reject credential-bearing HTTP before transport execution"

# Teardown must behave like finally: even if context verification, logout, and
# health all fail, it attempts every role and destroys local session material.
(
  parent="$(mktemp -d)"
  trap 'rm -rf "$parent"' EXIT
  logout_calls="$parent/logout-calls"
  export ASPNETCORE_ENVIRONMENT=Test
  export COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true
  export COGLATAS_SECURITY_CI_PASSWORD=teardown-contract-password
  export SECURITY_SCAN_STATE_PARENT="$parent"
  security_scan_init "http://localhost:8080"
  child="$SECURITY_SCAN_STATE_DIR"
  touch "$child/cookie.jar"

  security_scan_verify_context() { return 1; }
  security_scan_logout_role() { printf '%s\n' "$1" >> "$logout_calls"; return 1; }
  security_scan_health() { return 1; }

  if security_scan_teardown >/dev/null 2>&1; then
    exit 51
  fi
  [[ ! -e "$child" ]] || exit 52
  [[ -z "${SECURITY_SCAN_STATE_DIR:-}" ]] || exit 53
  [[ "$(wc -l < "$logout_calls")" -eq 4 ]] || exit 54
) || fail "teardown failure path did not best-effort all roles and always clean ephemeral auth material"

printf '%s\n' 'SEC-03 scanner harness contract tests passed.'
