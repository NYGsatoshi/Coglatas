#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/coglatas}"
BRANCH="${BRANCH:-main}"
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

cd "${APP_DIR}"

if [ ! -f .env ]; then
  echo "Missing ${APP_DIR}/.env. Run deploy-app.sh first."
  exit 1
fi

git fetch origin "${BRANCH}"
git checkout "${BRANCH}"
git pull --ff-only origin "${BRANCH}"

run_compose config --quiet
run_compose build app
run_compose up -d postgres
wait_for_postgres
run_compose run --rm migrate
run_compose up -d --build app
run_compose ps
run_compose logs --tail=100 app

echo ""
echo "Update complete. EF Core migrations were applied before the app was recreated."
echo "Database volume is preserved unless you explicitly run reset-app.sh with volume deletion."
