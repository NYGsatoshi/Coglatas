#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/coglatas}"
COMPOSE="${COMPOSE:-docker compose}"

run_compose() {
  if docker info >/dev/null 2>&1; then
    ${COMPOSE} "$@"
  else
    sudo ${COMPOSE} "$@"
  fi
}

cd "${APP_DIR}"

echo "== docker compose config =="
run_compose config --quiet

echo ""
echo "== docker compose ps =="
run_compose ps

echo ""
echo "== recent app logs =="
run_compose logs --tail=100 app

echo ""
echo "== localhost checks =="
curl -i http://localhost:8080/health/live || true
curl -i http://localhost:8080/health/ready || true

echo ""
echo "== docker networks =="
docker network ls || sudo docker network ls
run_compose exec -T app sh -lc 'hostname -i' || true

echo ""
echo "== postgres readiness =="
run_compose exec -T postgres sh -lc 'pg_isready -U "${DB_USER}" -d "${DB_NAME}"'

echo ""
echo "== postgres connection =="
run_compose exec -T postgres sh -lc 'psql -U "${DB_USER}" -d "${DB_NAME}" -c "select current_database(), current_user, now();"'

echo ""
echo "== disk =="
df -h

echo ""
echo "== memory =="
free -h

EXTERNAL_IP="$(curl -fsS -H 'Metadata-Flavor: Google' 'http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip' || true)"
if [ -n "${EXTERNAL_IP}" ]; then
  echo ""
  echo "== access URL =="
  echo "http://${EXTERNAL_IP}:8080"
fi
