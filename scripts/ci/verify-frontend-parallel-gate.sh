#!/usr/bin/env bash
# Required frontend-test aggregator: never accept an absent or failed lane.
set -Eeuo pipefail

deny() {
  printf '::error::Frontend gate rejected: %s\n' "$1" >&2
  exit 1
}

producer="${FRONTEND_PRODUCER_RESULT:-}"
browser="${FRONTEND_BROWSER_RESULT:-}"
unit="${FRONTEND_UNIT_JOB_RESULT:-}"
unit_selected="${FRONTEND_UNIT_SELECTED:-}"
playwright_selected="${FRONTEND_PLAYWRIGHT_SELECTED:-}"

for flag in "$unit_selected" "$playwright_selected"; do
  [[ "$flag" == "true" || "$flag" == "false" ]] ||
    deny "invalid frontend routing flag"
done

[[ "$producer" == "success" ]] || deny "frontend build producer is not successful"
[[ "$browser" == "success" ]] || deny "frontend browser/static lane is not successful"

if [[ "$unit_selected" == "true" ]]; then
  # Every selected unit route (unit-only or dual) must finish in the
  # independent runner. Skipped/failed tests cannot be hidden by the browser
  # lane or this aggregate check.
  [[ "$unit" == "success" ]] ||
    deny "parallel unit tests were required but not successful"
else
  # No unit work was selected; the conditional unit job must remain skipped.
  [[ "$unit" == "skipped" ]] ||
    deny "unexpected unit job result without selected unit work"
fi

echo "Frontend required gate PASS: all routed lanes completed successfully."
