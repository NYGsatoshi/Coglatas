#!/usr/bin/env bash
set -Eeuo pipefail
for prerequisite in DOTNET_BUILD_RESULT SECURITY_PREPARE_RESULT; do
  if [[ "${!prerequisite:-}" != success ]]; then
    printf '::error::Required security prerequisite %s is not successful: %s\n' "$prerequisite" "${!prerequisite:-missing}" >&2
    exit 1
  fi
done
case "${SECURITY_RUNTIME_REQUIRED:-}" in
  true) expected=success ;;
  false) expected=skipped ;;
  *) echo '::error::Security runtime routing must be exactly true or false.' >&2; exit 1 ;;
esac
for runtime in SECURITY_CORE_RESULT SECURITY_ZAP_RESULT; do
  if [[ "${!runtime:-}" != "$expected" ]]; then
    printf '::error::Security runtime %s must be %s; observed=%s\n' "$runtime" "$expected" "${!runtime:-missing}" >&2
    exit 1
  fi
done
printf 'Security preflight and every exact routed native runtime prerequisite are verified.\n'
