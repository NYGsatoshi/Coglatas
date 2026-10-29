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

if [[ "$unit_selected" == "true" && "$playwright_selected" == "true" ]]; then
  # The original combined job cannot qualify a full dual-scope change alone:
  # the separately scheduled unit job must complete successfully.
  [[ "$unit" == "success" ]] ||
    deny "parallel unit tests were required but not successful"
else
  # Unit-only changes still run in the browser/static lane. A separate unit
  # job for those paths would indicate a routing/permission policy drift.
  [[ "$unit" == "skipped" ]] ||
    deny "unexpected parallel unit job for non-dual frontend route"
fi

echo "Frontend required gate PASS: all routed lanes completed successfully."
