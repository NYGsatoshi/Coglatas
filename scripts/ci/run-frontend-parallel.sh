#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

# shellcheck source=scripts/ci/parallel-lane-lib.sh
source scripts/ci/parallel-lane-lib.sh

enabled() {
  [[ "${1:-false}" == "true" ]]
}

run_build() { npm --prefix frontend run build; }
run_unit() {
  # Bound Vitest worker contention when this same hosted runner also executes
  # two static Playwright workers. Keep the normal unit runner unchanged for
  # local, unit-only and independent validation jobs.
  if [[ "${GITHUB_ACTIONS:-}" == "true" && "${FRONTEND_PLAYWRIGHT:-false}" == "true" ]]; then
    COGLATAS_FRONTEND_VITEST_MAX_WORKERS=2 npm --prefix frontend test
  else
    npm --prefix frontend test
  fi
}

run_static() {
  if enabled "${FRONTEND_ARCHITECTURE:-false}"; then
    npm --prefix frontend run check:architecture
    npm --prefix frontend run test:architecture
  fi
  if enabled "${FRONTEND_LICENSE_GUARD:-false}"; then
    npm --prefix frontend run test:syncfusion-license
  fi
}

run_storybook() {
  NODE_OPTIONS=--max-old-space-size=4096 npm --prefix frontend run build-storybook
}

run_playwright() {
  # Hosted full static UI suite only. The Compose runner enforces the exact
  # prebuilt revision, scope, and worker limits. Main keeps the qualified two
  # workers. Only PRs targeting SecurityTestImplimation opt in to a bounded
  # three-worker performance *trial*; no retries or test selection changes.
  if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
    if [[ "${GITHUB_EVENT_NAME:-}" == "pull_request" &&
          "${GITHUB_BASE_REF:-}" == "SecurityTestImplimation" ]]; then
      COGLATAS_STATIC_PLAYWRIGHT_WORKERS=3 COGLATAS_STATIC_PLAYWRIGHT_SHADOW3=1 \
        npm run test:ui:angular:docker
    else
      COGLATAS_STATIC_PLAYWRIGHT_WORKERS=2 npm run test:ui:angular:docker
    fi
  else
    npm run test:ui:angular:docker
  fi
}

enabled "${FRONTEND_BUILD:-false}" && ci_parallel_start frontend-build run_build
enabled "${FRONTEND_UNIT:-false}" && ci_parallel_start frontend-unit run_unit
if enabled "${FRONTEND_ARCHITECTURE:-false}" || enabled "${FRONTEND_LICENSE_GUARD:-false}"; then
  ci_parallel_start frontend-static run_static
fi
enabled "${FRONTEND_STORYBOOK:-false}" && ci_parallel_start frontend-storybook run_storybook
enabled "${FRONTEND_PLAYWRIGHT:-false}" && ci_parallel_start frontend-playwright run_playwright

ci_parallel_wait
