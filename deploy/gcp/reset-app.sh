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

echo "This resets the development deployment in ${APP_DIR}."
echo "Choose one:"
echo "  restart : recreate containers, keep PostgreSQL data volume"
echo "  volumes : DELETE PostgreSQL/uploads/data-protection volumes"
echo "  cancel  : do nothing"
read -r -p "Type restart, volumes, or cancel: " choice

case "${choice}" in
  restart)
    run_compose down
    run_compose up -d --build
    run_compose ps
    run_compose logs --tail=100 app
    ;;
  volumes)
    echo "DANGER: this deletes the PostgreSQL database volume and uploaded files for this Compose project."
    read -r -p "Type DELETE-DATABASE to continue: " confirm
    if [ "${confirm}" != "DELETE-DATABASE" ]; then
      echo "Cancelled."
      exit 0
    fi
    run_compose down --volumes --remove-orphans
    run_compose up -d --build
    run_compose ps
    run_compose logs --tail=100 app
    ;;
  *)
    echo "Cancelled."
    ;;
esac
