#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/coglatas}"
ARCHIVE_PATH="${ARCHIVE_PATH:-/tmp/coglatas-deploy.tar}"
COMPOSE="${COMPOSE:-docker compose}"

run_compose() {
  if docker info >/dev/null 2>&1; then
    ${COMPOSE} "$@"
  else
    sudo ${COMPOSE} "$@"
  fi
}

wait_for_postgres() {
  echo "Waiting for PostgreSQL health..."
  for attempt in $(seq 1 60); do
    if run_compose exec -T postgres sh -lc 'pg_isready -U "${DB_USER}" -d "${DB_NAME}"' >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done

  echo "PostgreSQL did not become ready in time."
  run_compose logs --tail=100 postgres || true
  return 1
}

random_secret() {
  openssl rand -base64 36 | tr -d '\n'
}

if [ ! -f "${ARCHIVE_PATH}" ]; then
  echo "Missing archive: ${ARCHIVE_PATH}"
  exit 1
fi

echo "Deploying uploaded archive ${ARCHIVE_PATH} to ${APP_DIR}"
sudo mkdir -p "${APP_DIR}"
sudo chown "${USER}:${USER}" "${APP_DIR}"
tar --no-same-owner --no-same-permissions -xf "${ARCHIVE_PATH}" -C "${APP_DIR}"

cd "${APP_DIR}"

if [ ! -f .env ]; then
  echo "Creating .env with generated development secrets..."
  POSTGRES_PASSWORD_VALUE="$(random_secret)"
  LOCAL_ADMIN_PASSWORD_VALUE="$(random_secret)"
  cat > .env <<EOF_ENV
DB_HOST=db
DB_NAME=coglatas
DB_USER=coglatas
DB_PASSWORD=${POSTGRES_PASSWORD_VALUE}
COGLATAS_PORT=8080
FILE_STORAGE_MAX_FILE_SIZE_BYTES=52428800
ASPNETCORE_ENVIRONMENT=Development
TENANCY_APP_MODE=OnPremSingleTenant
TENANCY_DEFAULT_TENANT_SLUG=default
TENANCY_RESOLUTION_STRATEGY=ConfigDefault
TENANCY_ALLOW_SWITCHING=false
SECURITY_COOKIE_SECURE_POLICY=SameAsRequest
SECURITY_REQUIRE_HTTPS=false
SECURITY_ENABLE_HSTS=false
PLATFORM_ADMIN_SETUP_MODE=true
LOCAL_ADMIN_SEED_ON_STARTUP=true
LOCAL_ADMIN_EMAIL=admin@example.com
LOCAL_ADMIN_PASSWORD=${LOCAL_ADMIN_PASSWORD_VALUE}
LOCAL_ADMIN_DISPLAY_NAME=Local Admin
SYNCFUSION_LICENSE=
EOF_ENV
  chmod 600 .env
  echo "Generated LOCAL_ADMIN_EMAIL=admin@example.com"
  echo "Generated LOCAL_ADMIN_PASSWORD=${LOCAL_ADMIN_PASSWORD_VALUE}"
  echo "Store this password now. It is saved only in ${APP_DIR}/.env on the VM."
  echo "Set SYNCFUSION_LICENSE in ${APP_DIR}/.env before building the application."
else
  echo ".env already exists; leaving secrets unchanged."
fi

set -a
. "${APP_DIR}/.env"
set +a
test -n "${SYNCFUSION_LICENSE:-}" || {
  echo "SYNCFUSION_LICENSE is not configured." >&2
  exit 1
}

run_compose config --quiet
run_compose build app
run_compose up -d postgres
wait_for_postgres
run_compose run --rm migrate
run_compose up -d app
run_compose ps
run_compose logs --tail=100 app

EXTERNAL_IP="$(curl -fsS -H 'Metadata-Flavor: Google' 'http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip' || true)"

echo ""
echo "Deployment complete."
echo "Local health:"
curl -fsS http://localhost:8080/health/ready || true
echo ""
if [ -n "${EXTERNAL_IP}" ]; then
  echo "Access URL: http://${EXTERNAL_IP}:8080"
fi
