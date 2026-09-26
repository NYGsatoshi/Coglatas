#!/usr/bin/env bash
# Shared SEC-03 scanner identity/session harness.
#
# Source this file from Schemathesis/ZAP wrappers and CI smoke tests. Scanner
# credentials are never written to repository files. Cookie/CSRF material lives
# only in a harness-owned temporary child directory and is destroyed on failure
# and teardown.

SECURITY_SCAN_ALPHA_OWNER_EMAIL="security-alpha-owner@example.test"
SECURITY_SCAN_ALPHA_MEMBER_EMAIL="security-alpha-member@example.test"
SECURITY_SCAN_ALPHA_RESTRICTED_EMAIL="security-alpha-restricted@example.test"
SECURITY_SCAN_BETA_OWNER_EMAIL="security-beta-owner@example.test"
SECURITY_SCAN_ALPHA_WORKSPACE_CANARY="SEC-02 Alpha Workspace Canary"
SECURITY_SCAN_BETA_WORKSPACE_CANARY="SEC-02 Beta Workspace Canary"

security_scan_fail() {
  printf 'SEC-03 scanner harness failed: %s\n' "$*" >&2
  return 1
}

security_scan_require_no_xtrace() {
  case "$-" in
    *x*) security_scan_fail "shell xtrace must be disabled while scanner auth material is in scope" ;;
    *) return 0 ;;
  esac
}

security_scan_require_boundary() {
  security_scan_require_no_xtrace || return 1
  [[ "${ASPNETCORE_ENVIRONMENT:-}" == "Test" ]] ||
    security_scan_fail "ASPNETCORE_ENVIRONMENT must be exactly Test" || return 1
  case "${COGLATAS_SECURITY_CI_FIXTURE_ENABLED:-}" in
    true|TRUE|True|1) ;;
    *) security_scan_fail "SEC-02 activation marker COGLATAS_SECURITY_CI_FIXTURE_ENABLED=true is required"; return 1 ;;
  esac
  [[ -n "${COGLATAS_SECURITY_CI_PASSWORD:-}" ]] ||
    security_scan_fail "COGLATAS_SECURITY_CI_PASSWORD is required from a test-only source" || return 1
}

security_scan_validate_target() {
  local target=${1:-}
  [[ -n "$target" ]] || {
    security_scan_fail "scan target origin is required"
    return 1
  }

  python3 - \
    "$target" \
    "${COGLATAS_SECURITY_CI_EPHEMERAL_ORIGIN:-}" \
    "${GITHUB_ACTIONS:-}" \
    "${SECURITY_SCAN_TRANSPORT_KIND:-}" <<'PY'
from __future__ import annotations

import ipaddress
import sys
from urllib.parse import urlsplit, urlunsplit

raw, ephemeral_raw, github_actions, transport_kind = sys.argv[1:5]


def reject(message: str) -> None:
    raise SystemExit(f"SEC-03 target rejected before network access: {message}")


def parse_origin(value: str):
    try:
        parsed = urlsplit(value)
        port = parsed.port
    except ValueError as exc:
        reject(f"invalid origin: {exc}")
    if parsed.scheme not in {"http", "https"}:
        reject("scheme must be http or https")
    if parsed.username is not None or parsed.password is not None:
        reject("userinfo is forbidden")
    if not parsed.hostname:
        reject("hostname is required")
    if parsed.path not in {"", "/"} or parsed.query or parsed.fragment:
        reject("target must be an origin without path, query, or fragment")

    host = parsed.hostname.rstrip(".").lower()
    if not host:
        reject("hostname is empty")
    if any(character.isspace() for character in host):
        reject("hostname contains whitespace")

    display_host = f"[{host}]" if ":" in host else host
    netloc = display_host if port is None else f"{display_host}:{port}"
    return host, urlunsplit((parsed.scheme.lower(), netloc, "", "", ""))


def is_loopback_literal(host: str) -> bool:
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        return False
    return address.is_loopback


host, normalized = parse_origin(raw)
local_allowed = (
    host == "localhost"
    or host.endswith(".localhost")
    or is_loopback_literal(host)
)
compose_allowed = host == "app" and transport_kind == "compose"

explicit_ephemeral = False
if ephemeral_raw:
    ephemeral_host, ephemeral_normalized = parse_origin(ephemeral_raw)
    explicit_ephemeral = (
        github_actions.lower() == "true"
        and ephemeral_host.endswith(".test")
        and normalized == ephemeral_normalized
    )

if not (local_allowed or compose_allowed or explicit_ephemeral):
    reject(
        "only localhost/loopback, a transport-bound SEC-02 Compose service 'app', "
        "or an exact reserved .test GitHub Actions ephemeral origin are allowed"
    )

print(normalized)
PY
}

security_scan_target_host() {
  python3 - "$1" <<'PY'
import sys
from urllib.parse import urlsplit
print((urlsplit(sys.argv[1]).hostname or "").rstrip(".").lower())
PY
}

security_scan_require_transport_binding() {
  local target=${1:-${SECURITY_SCAN_TARGET:-}}
  local host
  host="$(security_scan_target_host "$target")" || return 1
  if [[ "$host" != "app" ]]; then
    return 0
  fi

  [[ "${SECURITY_SCAN_TRANSPORT_KIND:-}" == "compose" ]] ||
    security_scan_fail "Compose service target requires SECURITY_SCAN_TRANSPORT_KIND=compose" || return 1
  declare -F security_scan_transport_guard >/dev/null 2>&1 || {
    security_scan_fail "Compose service target requires a transport guard that proves isolated network attachment"
    return 1
  }
  security_scan_transport_guard "$target" ||
    security_scan_fail "Compose transport guard rejected scanner target '$target'" || return 1
}

security_scan_role_tenant() {
  case "$1" in
    alpha-owner|alpha-member|alpha-restricted) printf '%s\n' 'security-alpha' ;;
    beta-owner) printf '%s\n' 'security-beta' ;;
    *) security_scan_fail "unknown synthetic scanner role '$1'"; return 1 ;;
  esac
}

security_scan_role_email() {
  case "$1" in
    alpha-owner) printf '%s\n' "$SECURITY_SCAN_ALPHA_OWNER_EMAIL" ;;
    alpha-member) printf '%s\n' "$SECURITY_SCAN_ALPHA_MEMBER_EMAIL" ;;
    alpha-restricted) printf '%s\n' "$SECURITY_SCAN_ALPHA_RESTRICTED_EMAIL" ;;
    beta-owner) printf '%s\n' "$SECURITY_SCAN_BETA_OWNER_EMAIL" ;;
    *) security_scan_fail "unknown synthetic scanner role '$1'"; return 1 ;;
  esac
}

security_scan_role_workspace_canary() {
  case "$1" in
    alpha-owner|alpha-member|alpha-restricted) printf '%s\n' "$SECURITY_SCAN_ALPHA_WORKSPACE_CANARY" ;;
    beta-owner) printf '%s\n' "$SECURITY_SCAN_BETA_WORKSPACE_CANARY" ;;
    *) security_scan_fail "unknown synthetic scanner role '$1'"; return 1 ;;
  esac
}

security_scan_role_forbidden_canary() {
  case "$1" in
    alpha-owner|alpha-member|alpha-restricted) printf '%s\n' "$SECURITY_SCAN_BETA_WORKSPACE_CANARY" ;;
    beta-owner) printf '%s\n' "$SECURITY_SCAN_ALPHA_WORKSPACE_CANARY" ;;
    *) security_scan_fail "unknown synthetic scanner role '$1'"; return 1 ;;
  esac
}

security_scan_redact_stream() {
  python3 -c '
import os
import re
import sys

text = sys.stdin.read()
secret = os.environ.get("COGLATAS_SECURITY_CI_PASSWORD", "")
if secret:
    text = text.replace(secret, "[REDACTED]")
patterns = (
    r"(?im)^(cookie|set-cookie|x-csrf-token|authorization|proxy-authorization)\s*:\s*.*$",
    r"(?i)(\"(?:password|token|accessToken|refreshToken|csrfToken|csrf|cookie)\"\s*:\s*\")[^\"]*(\")",
)
text = re.sub(patterns[0], lambda m: f"{m.group(1)}: [REDACTED]", text)
text = re.sub(patterns[1], r"\1[REDACTED]\2", text)
sys.stdout.write(text)
'
}

security_scan_init() {
  local target=${1:-}
  local state_parent http_parent child_name
  security_scan_require_boundary || return 1
  [[ -z "${SECURITY_SCAN_STATE_DIR:-}" && -z "${SECURITY_SCAN_HTTP_STATE_DIR:-}" ]] || {
    security_scan_fail "SECURITY_SCAN_STATE_DIR/HTTP_STATE_DIR are derived; configure *_STATE_PARENT instead"
    return 1
  }

  SECURITY_SCAN_TARGET="$(security_scan_validate_target "$target")" || return 1
  security_scan_require_transport_binding "$SECURITY_SCAN_TARGET" || return 1

  state_parent="${SECURITY_SCAN_STATE_PARENT:-${TMPDIR:-/tmp}}"
  http_parent="${SECURITY_SCAN_HTTP_STATE_PARENT:-$state_parent}"
  [[ -d "$state_parent" && -w "$state_parent" ]] || {
    security_scan_fail "scanner state parent must already exist and be writable: $state_parent"
    return 1
  }

  umask 077
  SECURITY_SCAN_STATE_DIR="$(mktemp -d "$state_parent/coglatas-security-scanner.XXXXXX")" || return 1
  child_name="$(basename "$SECURITY_SCAN_STATE_DIR")"
  SECURITY_SCAN_HTTP_STATE_DIR="${http_parent%/}/$child_name"
  SECURITY_SCAN_STATE_OWNED=1
  chmod 700 "$SECURITY_SCAN_STATE_DIR"
  export SECURITY_SCAN_TARGET SECURITY_SCAN_STATE_DIR SECURITY_SCAN_HTTP_STATE_DIR SECURITY_SCAN_STATE_OWNED
}

security_scan_cleanup() {
  local status=0 state_dir="${SECURITY_SCAN_STATE_DIR:-}" base_name
  if [[ "${SECURITY_SCAN_STATE_OWNED:-}" == "1" && -n "$state_dir" && -d "$state_dir" ]]; then
    base_name="$(basename "$state_dir")"
    if [[ "$base_name" == coglatas-security-scanner.* ]]; then
      rm -rf -- "$state_dir" || status=1
    else
      security_scan_fail "refusing to remove non-owned scanner state path '$state_dir'" || true
      status=1
    fi
  fi
  unset SECURITY_SCAN_STATE_DIR SECURITY_SCAN_HTTP_STATE_DIR SECURITY_SCAN_STATE_OWNED SECURITY_SCAN_TARGET
  unset SECURITY_SCAN_CSRF_TOKEN SECURITY_SCAN_CSRF_HEADER_NAME SECURITY_SCAN_LAST_LOGIN_STATUS
  unset SECURITY_SCAN_WRONG_PASSWORD
  return "$status"
}

security_scan_curl_default() {
  curl "$@"
}

security_scan_http() {
  security_scan_require_no_xtrace || return 1
  if declare -F security_scan_curl >/dev/null 2>&1; then
    security_scan_curl "$@"
  else
    security_scan_curl_default "$@"
  fi
}

security_scan_host_path() {
  printf '%s/%s\n' "$SECURITY_SCAN_STATE_DIR" "$1"
}

security_scan_http_path() {
  printf '%s/%s\n' "$SECURITY_SCAN_HTTP_STATE_DIR" "$1"
}

security_scan_cookie_jar() {
  local role=$1
  security_scan_role_tenant "$role" >/dev/null || return 1
  security_scan_http_path "${role}.cookies"
}

security_scan_fetch_csrf() {
  local role=$1
  local tenant jar payload
  security_scan_require_no_xtrace || return 1
  tenant="$(security_scan_role_tenant "$role")" || return 1
  jar="$(security_scan_cookie_jar "$role")" || return 1
  payload="$(security_scan_http \
    --fail --silent --show-error \
    -c "$jar" -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    "$SECURITY_SCAN_TARGET/api/security/csrf-token")" ||
    security_scan_fail "CSRF bootstrap failed for role '$role'" || return 1

  IFS=$'\t' read -r SECURITY_SCAN_CSRF_HEADER_NAME SECURITY_SCAN_CSRF_TOKEN < <(
    python3 -c '
import json
import sys
payload = json.load(sys.stdin)
header = payload.get("headerName")
token = payload.get("token")
if header != "X-CSRF-Token" or not isinstance(token, str) or not token:
    raise SystemExit("invalid CSRF response")
print(f"{header}\t{token}")
' <<<"$payload"
  ) || security_scan_fail "invalid CSRF payload for role '$role'" || return 1
  export SECURITY_SCAN_CSRF_HEADER_NAME SECURITY_SCAN_CSRF_TOKEN
}

# The password is referenced by variable name rather than passed as a raw function
# argument. If a caller accidentally enables xtrace before invoking this helper,
# the call site can expose only the variable name; the value is not expanded until
# after the xtrace guard succeeds inside the function.
security_scan_login_with_password_variable() {
  local role=$1
  local password_variable=$2
  local password tenant email jar response_http_path status
  security_scan_require_no_xtrace || return 1
  [[ "$password_variable" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] ||
    security_scan_fail "invalid password variable name" || return 1
  password="${!password_variable:-}"
  [[ -n "$password" ]] || security_scan_fail "scanner login password variable is empty" || return 1

  tenant="$(security_scan_role_tenant "$role")" || return 1
  email="$(security_scan_role_email "$role")" || return 1
  jar="$(security_scan_cookie_jar "$role")" || return 1
  response_http_path="$(security_scan_http_path "${role}-login.json")"

  security_scan_fetch_csrf "$role" || return 1
  status="$(
    SECURITY_SCAN_LOGIN_EMAIL="$email" SECURITY_SCAN_LOGIN_PASSWORD="$password" \
      python3 -c '
import json
import os
import sys
json.dump({"email": os.environ["SECURITY_SCAN_LOGIN_EMAIL"], "password": os.environ["SECURITY_SCAN_LOGIN_PASSWORD"]}, sys.stdout, separators=(",", ":"))
' | security_scan_http \
      --silent --show-error \
      -o "$response_http_path" \
      -w '%{http_code}' \
      -c "$jar" -b "$jar" \
      -H "X-Tenant-Slug: $tenant" \
      -H "$SECURITY_SCAN_CSRF_HEADER_NAME: $SECURITY_SCAN_CSRF_TOKEN" \
      -H "Content-Type: application/json" \
      --data-binary @- \
      "$SECURITY_SCAN_TARGET/api/auth/login"
  )" || security_scan_fail "login request failed for role '$role'" || return 1

  SECURITY_SCAN_LAST_LOGIN_STATUS="$status"
  export SECURITY_SCAN_LAST_LOGIN_STATUS
}

security_scan_verify_context() {
  local role=$1
  local tenant email jar me_path me_http_path workspaces_path workspaces_http_path
  local own_canary forbidden_canary other_tenant cross_path cross_http_path cross_status
  security_scan_require_no_xtrace || return 1
  tenant="$(security_scan_role_tenant "$role")" || return 1
  email="$(security_scan_role_email "$role")" || return 1
  own_canary="$(security_scan_role_workspace_canary "$role")" || return 1
  forbidden_canary="$(security_scan_role_forbidden_canary "$role")" || return 1
  jar="$(security_scan_cookie_jar "$role")" || return 1
  me_path="$(security_scan_host_path "${role}-me.json")"
  me_http_path="$(security_scan_http_path "${role}-me.json")"
  workspaces_path="$(security_scan_host_path "${role}-workspaces.json")"
  workspaces_http_path="$(security_scan_http_path "${role}-workspaces.json")"

  security_scan_http \
    --fail --silent --show-error \
    -o "$me_http_path" \
    -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    "$SECURITY_SCAN_TARGET/api/auth/me" ||
    security_scan_fail "current-user probe failed for role '$role'" || return 1

  python3 - "$email" "$me_path" <<'PY' || return 1
import json
import sys
expected, path = sys.argv[1:3]
with open(path, encoding="utf-8") as handle:
    payload = json.load(handle)
if payload.get("email") != expected:
    raise SystemExit(f"current-user email mismatch: {payload.get('email')!r}")
PY

  security_scan_http \
    --fail --silent --show-error \
    -o "$workspaces_http_path" \
    -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    "$SECURITY_SCAN_TARGET/api/workspaces" ||
    security_scan_fail "workspace context probe failed for role '$role'" || return 1

  grep -Fq "$own_canary" "$workspaces_path" ||
    security_scan_fail "expected workspace canary missing for role '$role'" || return 1
  if grep -Fq "$forbidden_canary" "$workspaces_path"; then
    security_scan_fail "cross-tenant workspace canary disclosed for role '$role'"
    return 1
  fi

  if [[ "$tenant" == "security-alpha" ]]; then
    other_tenant="security-beta"
  else
    other_tenant="security-alpha"
  fi
  cross_path="$(security_scan_host_path "${role}-cross-tenant.json")"
  cross_http_path="$(security_scan_http_path "${role}-cross-tenant.json")"
  cross_status="$(security_scan_http \
    --silent --show-error \
    -o "$cross_http_path" \
    -w '%{http_code}' \
    -b "$jar" \
    -H "X-Tenant-Slug: $other_tenant" \
    "$SECURITY_SCAN_TARGET/api/workspaces")" ||
    security_scan_fail "cross-tenant context probe transport failed for role '$role'" || return 1
  case "$cross_status" in
    200|401|403|404) ;;
    *) security_scan_fail "cross-tenant context probe for '$role' returned HTTP $cross_status"; return 1 ;;
  esac
  if grep -Fq "$SECURITY_SCAN_ALPHA_WORKSPACE_CANARY" "$cross_path" ||
     grep -Fq "$SECURITY_SCAN_BETA_WORKSPACE_CANARY" "$cross_path"; then
    security_scan_fail "tenant-switch probe disclosed a security fixture canary for role '$role'"
    return 1
  fi
}

security_scan_bootstrap_role() {
  local role=$1
  security_scan_require_no_xtrace || return 1
  security_scan_login_with_password_variable "$role" COGLATAS_SECURITY_CI_PASSWORD || return 1
  [[ "$SECURITY_SCAN_LAST_LOGIN_STATUS" == "200" ]] || {
    security_scan_fail "synthetic role '$role' login returned HTTP $SECURITY_SCAN_LAST_LOGIN_STATUS"
    return 1
  }
  security_scan_verify_context "$role" || return 1
}

security_scan_verify_wrong_password_rejected() {
  local role=${1:-alpha-owner}
  local tenant jar me_http_path status login_status
  security_scan_require_no_xtrace || return 1
  tenant="$(security_scan_role_tenant "$role")" || return 1
  jar="$(security_scan_cookie_jar "$role")" || return 1
  rm -f -- "$(security_scan_host_path "${role}.cookies")" "$(security_scan_host_path "${role}-login.json")"

  SECURITY_SCAN_WRONG_PASSWORD="${COGLATAS_SECURITY_CI_PASSWORD}__invalid"
  if ! security_scan_login_with_password_variable "$role" SECURITY_SCAN_WRONG_PASSWORD; then
    unset SECURITY_SCAN_WRONG_PASSWORD
    return 1
  fi
  login_status="$SECURITY_SCAN_LAST_LOGIN_STATUS"
  unset SECURITY_SCAN_WRONG_PASSWORD
  [[ "$login_status" == "401" ]] || {
    security_scan_fail "wrong password for '$role' returned HTTP $login_status instead of 401"
    return 1
  }

  me_http_path="$(security_scan_http_path "${role}-wrong-password-me.json")"
  status="$(security_scan_http \
    --silent --show-error \
    -o "$me_http_path" \
    -w '%{http_code}' \
    -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    "$SECURITY_SCAN_TARGET/api/auth/me")" ||
    security_scan_fail "wrong-password current-user probe transport failed" || return 1
  [[ "$status" == "401" ]] || {
    security_scan_fail "wrong-password flow emitted an authenticated context (HTTP $status)"
    return 1
  }
  rm -f -- "$(security_scan_host_path "${role}.cookies")"
}

security_scan_health() {
  local output_http_path output_path
  security_scan_require_no_xtrace || return 1
  output_path="$(security_scan_host_path 'health-ready.json')"
  output_http_path="$(security_scan_http_path 'health-ready.json')"
  security_scan_http \
    --fail --silent --show-error \
    -o "$output_http_path" \
    -H "X-Tenant-Slug: security-alpha" \
    "$SECURITY_SCAN_TARGET/health/ready" ||
    security_scan_fail "application readiness probe failed" || return 1
  grep -Fq '"status":"OK"' "$output_path" ||
    security_scan_fail "readiness endpoint returned an unexpected payload" || return 1
}

security_scan_preflight_fail() {
  local message=$1
  security_scan_cleanup || true
  security_scan_fail "$message"
}

security_scan_preflight() {
  local role
  security_scan_require_boundary || {
    security_scan_cleanup || true
    return 1
  }
  security_scan_validate_target "$SECURITY_SCAN_TARGET" >/dev/null || {
    security_scan_cleanup || true
    return 1
  }
  security_scan_require_transport_binding "$SECURITY_SCAN_TARGET" || {
    security_scan_cleanup || true
    return 1
  }
  security_scan_health || {
    security_scan_preflight_fail "preflight health verification failed; ephemeral auth material was destroyed"
    return 1
  }

  for role in alpha-owner alpha-member alpha-restricted beta-owner; do
    security_scan_bootstrap_role "$role" || {
      security_scan_preflight_fail "preflight role bootstrap failed; ephemeral auth material was destroyed"
      return 1
    }
  done

  security_scan_verify_wrong_password_rejected alpha-owner || {
    security_scan_preflight_fail "preflight negative-auth verification failed; ephemeral auth material was destroyed"
    return 1
  }
  security_scan_bootstrap_role alpha-owner || {
    security_scan_preflight_fail "preflight owner re-bootstrap failed; ephemeral auth material was destroyed"
    return 1
  }

  printf '%s\n' 'SEC-03 scanner preflight passed: isolated target, SEC-02 fixture topology, four synthetic roles, and negative auth are verified.'
}

security_scan_logout_role() {
  local role=$1
  local tenant jar response_http_path status
  security_scan_require_no_xtrace || return 1
  tenant="$(security_scan_role_tenant "$role")" || return 1
  jar="$(security_scan_cookie_jar "$role")" || return 1
  response_http_path="$(security_scan_http_path "${role}-logout.json")"

  security_scan_fetch_csrf "$role" || return 1
  status="$(printf '{}' | security_scan_http \
    --silent --show-error \
    -o "$response_http_path" \
    -w '%{http_code}' \
    -c "$jar" -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    -H "$SECURITY_SCAN_CSRF_HEADER_NAME: $SECURITY_SCAN_CSRF_TOKEN" \
    -H "Content-Type: application/json" \
    --data-binary @- \
    "$SECURITY_SCAN_TARGET/api/auth/logout")" ||
    security_scan_fail "logout request failed for role '$role'" || return 1
  [[ "$status" == "200" ]] || {
    security_scan_fail "logout for '$role' returned HTTP $status"
    return 1
  }

  status="$(security_scan_http \
    --silent --show-error \
    -o "$response_http_path" \
    -w '%{http_code}' \
    -b "$jar" \
    -H "X-Tenant-Slug: $tenant" \
    "$SECURITY_SCAN_TARGET/api/auth/me")" ||
    security_scan_fail "post-logout probe failed for role '$role'" || return 1
  [[ "$status" == "401" ]] || {
    security_scan_fail "logout did not invalidate '$role' session (HTTP $status)"
    return 1
  }
}

security_scan_teardown() {
  local role status=0 xtrace_safe=1

  # Teardown is a finally-style boundary. If xtrace was re-enabled after
  # preflight, do not issue any credential-bearing HTTP request; still destroy
  # local auth material before returning failure.
  if ! security_scan_require_no_xtrace; then
    status=1
    xtrace_safe=0
  fi

  if (( xtrace_safe == 1 )); then
    # Preserve the first failure semantically but continue every check/logout so
    # one broken role cannot prevent invalidation attempts for the others.
    for role in alpha-owner alpha-member alpha-restricted beta-owner; do
      if ! security_scan_verify_context "$role"; then
        status=1
      fi
    done
    for role in alpha-owner alpha-member alpha-restricted beta-owner; do
      if ! security_scan_logout_role "$role"; then
        status=1
      fi
    done
    if ! security_scan_health; then
      status=1
    fi
  fi

  if ! security_scan_cleanup; then
    status=1
  fi

  if (( status != 0 )); then
    security_scan_fail "scanner teardown encountered errors; ephemeral auth material was destroyed"
    return 1
  fi

  printf '%s\n' 'SEC-03 scanner teardown passed: sessions invalidated, fixture isolation intact, application remains healthy, and ephemeral auth material was destroyed.'
}
