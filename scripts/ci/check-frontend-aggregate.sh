#!/usr/bin/env bash
set -Eeuo pipefail

for task_result in CHANGES_RESULT FRONTEND_BUILD_RESULT FRONTEND_CHECKS_RESULT; do
  if [[ "${!task_result:-}" != success ]]; then
    printf '::error::Required frontend prerequisite %s is not successful: %s\n' "$task_result" "${!task_result:-missing}" >&2
    exit 1
  fi
done

case "${FRONTEND_STORYBOOK:-}" in
  true) expected_storybook=success ;;
  false) expected_storybook=skipped ;;
  *) echo '::error::Storybook routing must be exactly true or false.' >&2; exit 1 ;;
esac
if [[ "${STORYBOOK_RESULT:-}" != "$expected_storybook" ]]; then
  printf '::error::Storybook must be %s for routed=%s; observed=%s\n' "$expected_storybook" "$FRONTEND_STORYBOOK" "${STORYBOOK_RESULT:-missing}" >&2
  exit 1
fi
printf 'Frontend checks and the exact routed Storybook prerequisite are verified.\n'