#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="${COGLATAS_SOURCE_DIR:-/srv/coglatas/app}"
DEPLOY_ENV="${COGLATAS_DEPLOY_ENV:-/srv/coglatas/deploy/.env}"
LICENSE_FILE="${SYNCFUSION_LICENSE_FILE:-/srv/coglatas/app/secrets/syncfusion-license.txt}"
CADDY_FILE="${COGLATAS_CADDYFILE:-/srv/coglatas/deploy/Caddyfile}"
COMPOSE_FILE="${SOURCE_DIR}/deploy/sakura/docker-compose.yml"
TRYCLOUDFLARE_COMPOSE_FILE="${SOURCE_DIR}/deploy/sakura/docker-compose.trycloudflare.yml"
PROCESS_EDGE_MODE="${COGLATAS_EDGE_MODE:-}"
EDGE_MODE=""
EDGE_MODE_SOURCE=""
VALIDATE_ONLY="${COGLATAS_DEPLOY_VALIDATE_ONLY:-false}"

fail() {
  echo "$1" >&2
  exit 1
}

usage() {
  cat <<'EOF'
Usage: deploy.sh [caddy|trycloudflare]

The Sakura edge mode has no implicit default. Configure COGLATAS_EDGE_MODE in the
owner-only deployment environment file (default: /srv/coglatas/deploy/.env), set
it in the invoking process, or pass the mode explicitly as the single positional
argument. Persisting the mode outside the Git worktree prevents unrelated pulls
or deployments from silently switching proxy topology.

Use COGLATAS_DEPLOY_VALIDATE_ONLY=true to validate the rendered deployment
contract without building or starting containers.
EOF
}

require_owner_only_file() {
  local file="$1"
  local label="$2"
  local mode

  test -s "$file" || fail "${label} is missing or empty."
  mode="$(stat -c '%a' "$file")"
  (( (8#${mode} & 8#077) == 0 )) || fail "${label} must not be readable or writable by group/other users."
}

read_deploy_env_value() {
  local key="$1"
  python3 - "$DEPLOY_ENV" "$key" <<'PY'
import sys

path, target = sys.argv[1:]
value = None
with open(path, encoding="utf-8") as handle:
    for raw in handle:
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, candidate = line.split("=", 1)
        if key.strip() != target:
            continue
        candidate = candidate.strip()
        if len(candidate) >= 2 and candidate[0] == candidate[-1] and candidate[0] in {"'", '"'}:
            candidate = candidate[1:-1]
        value = candidate.strip()

if value is not None:
    print(value)
PY
}

if (( $# > 1 )); then
  usage >&2
  exit 2
fi

case "$VALIDATE_ONLY" in
  true|false|1|0)
    ;;
  *)
    fail "COGLATAS_DEPLOY_VALIDATE_ONLY must be true, false, 1, or 0."
    ;;
esac

require_owner_only_file "$DEPLOY_ENV" "Deployment environment file"
require_owner_only_file "$LICENSE_FILE" "Syncfusion license file"

PERSISTED_EDGE_MODE="$(read_deploy_env_value COGLATAS_EDGE_MODE)"
if (( $# == 1 )); then
  EDGE_MODE="$1"
  EDGE_MODE_SOURCE="command line"
elif [[ -n "$PROCESS_EDGE_MODE" ]]; then
  EDGE_MODE="$PROCESS_EDGE_MODE"
  EDGE_MODE_SOURCE="process environment"
elif [[ -n "$PERSISTED_EDGE_MODE" ]]; then
  EDGE_MODE="$PERSISTED_EDGE_MODE"
  EDGE_MODE_SOURCE="$DEPLOY_ENV"
else
  usage >&2
  fail "Sakura edge mode is not configured. Set COGLATAS_EDGE_MODE=caddy or COGLATAS_EDGE_MODE=trycloudflare in ${DEPLOY_ENV} before deploying."
fi

case "$EDGE_MODE" in
  caddy|trycloudflare)
    ;;
  -h|--help)
    usage
    exit 0
    ;;
  *)
    usage >&2
    fail "Unsupported Sakura edge mode from ${EDGE_MODE_SOURCE}: ${EDGE_MODE}"
    ;;
esac

echo "Using Sakura edge mode: ${EDGE_MODE} (${EDGE_MODE_SOURCE})"

test -f "$COMPOSE_FILE" || fail "Missing tracked Sakura Compose file: ${COMPOSE_FILE}"
if [[ "$EDGE_MODE" == "trycloudflare" ]]; then
  test -f "$TRYCLOUDFLARE_COMPOSE_FILE" || fail "Missing tracked TryCloudflare Compose overlay: ${TRYCLOUDFLARE_COMPOSE_FILE}"
else
  test -f "$CADDY_FILE" || fail "Missing Caddyfile: ${CADDY_FILE}"
fi

test -d "${SOURCE_DIR}/.git" || test -f "${SOURCE_DIR}/.git" || fail "Source directory is not a Git worktree."
if [[ "$VALIDATE_ONLY" != "true" && "$VALIDATE_ONLY" != "1" ]]; then
  test -z "$(git -C "$SOURCE_DIR" status --porcelain)" || fail "Source worktree is not clean; deploy from a separate clean worktree."
fi

export COGLATAS_SOURCE_DIR="$SOURCE_DIR"
export COGLATAS_CADDYFILE="$CADDY_FILE"
export SYNCFUSION_LICENSE_FILE="$LICENSE_FILE"

compose=(docker compose --env-file "$DEPLOY_ENV" --project-name deploy -f "$COMPOSE_FILE")
if [[ "$EDGE_MODE" == "trycloudflare" ]]; then
  compose+=(-f "$TRYCLOUDFLARE_COMPOSE_FILE")
fi

validate_rendered_contract() {
  local rendered
  rendered="$(mktemp)"
  trap 'rm -f "$rendered"' RETURN

  "${compose[@]}" config --format json > "$rendered"

  python3 - "$EDGE_MODE" "$rendered" <<'PY'
import json
import sys

mode, path = sys.argv[1:]
with open(path, encoding="utf-8") as handle:
    config = json.load(handle)

services = config["services"]
web = services["web"]
env = web.get("environment", {})

required = {
    "Security__RequireHttps": "true",
    "Security__EnableHsts": "true",
    "Security__CookieSecurePolicy": "Always",
    "ReverseProxy__TrustForwardedHeaders": "true",
}
for key, expected in required.items():
    actual = env.get(key)
    if actual != expected:
        raise SystemExit(f"{mode}: expected {key}={expected!r}, got {actual!r}")

if not env.get("ReverseProxy__TrustedNetworks__0"):
    raise SystemExit(f"{mode}: trusted proxy network must be explicit")

symmetry = env.get("ReverseProxy__RequireHeaderSymmetry")
if mode == "caddy":
    if symmetry != "true":
        raise SystemExit(f"caddy: forwarded-header symmetry must remain enabled, got {symmetry!r}")
    if "caddy" not in services:
        raise SystemExit("caddy: bundled Caddy service must be present")
else:
    if symmetry != "false":
        raise SystemExit(f"trycloudflare: forwarded-header symmetry must be disabled, got {symmetry!r}")
    if "caddy" in services:
        raise SystemExit("trycloudflare: bundled Caddy must be disabled unless its profile is explicitly requested")

    origin_ports = [
        port for port in web.get("ports", [])
        if int(port.get("target", -1)) == 8080
    ]
    if not origin_ports:
        raise SystemExit("trycloudflare: web:8080 must be published to the loopback origin")
    if any(port.get("host_ip") != "127.0.0.1" for port in origin_ports):
        raise SystemExit(f"trycloudflare: origin must be loopback-only, got {origin_ports!r}")
PY
}

validate_rendered_contract

if [[ "$VALIDATE_ONLY" == "true" || "$VALIDATE_ONLY" == "1" ]]; then
  echo "Sakura ${EDGE_MODE} deployment contract is valid."
  exit 0
fi

"${compose[@]}" build web
"${compose[@]}" up -d postgres
"${compose[@]}" run --rm migrate

if [[ "$EDGE_MODE" == "trycloudflare" ]]; then
  # A previous Caddy-mode deployment can leave the project Caddy container
  # running. Stop/remove only that Compose service before exposing the
  # loopback-only Quick Tunnel origin.
  "${compose[@]}" --profile caddy stop caddy >/dev/null 2>&1 || true
  "${compose[@]}" --profile caddy rm -f caddy >/dev/null 2>&1 || true
  "${compose[@]}" up -d --no-build web
else
  "${compose[@]}" up -d --no-build web caddy
fi

"${compose[@]}" ps

check_ready() {
  "${compose[@]}" exec -T web curl --fail --silent --show-error \
    -H 'X-Forwarded-Proto: https' \
    -H 'X-Forwarded-Host: localhost' \
    http://localhost:8080/health/ready >/dev/null
}

check_trycloudflare_security_probe() {
  local headers
  headers="$(
    "${compose[@]}" exec -T web curl --fail --silent --show-error \
      -D - -o /dev/null \
      -H 'X-Forwarded-For: 198.51.100.42, 203.0.113.20' \
      -H 'X-Forwarded-Proto: https' \
      -H 'X-Forwarded-Host: portal.example.com' \
      http://localhost:8080/api/security/csrf-token
  )" || return 1

  grep -qi '^Strict-Transport-Security:' <<<"$headers" || return 1
  grep -qiE '^Set-Cookie: .*secure' <<<"$headers" || return 1
}

for attempt in $(seq 1 30); do
  if check_ready; then
    if [[ "$EDGE_MODE" != "trycloudflare" ]] || check_trycloudflare_security_probe; then
      echo "Sakura VPS deployment is ready in ${EDGE_MODE} mode."
      exit 0
    fi
  fi
  sleep 2
done

"${compose[@]}" logs --tail=200 web
fail "Sakura VPS readiness/security checks did not succeed."
