#!/usr/bin/env bash
set -Eeuo pipefail

# Match the Linux x86_64 release and checksum in the pinned qodana-action.
readonly QODANA_CLI_VERSION='2026.2.2'
readonly QODANA_CLI_SHA256='b1fc01fb5825f4c6382f9cedcfe97d4a8eeaaddc9f5384a3b5870ca63e231c6f'

install_verified_qodana_archive() {
  local archive="$1" destination="$2" actual
  if [[ ! -f "$archive" || -L "$archive" || ! -d "$destination" || -L "$destination" ]]; then
    echo 'Qodana requires a regular archive and an owned installation directory.' >&2
    return 1
  fi
  actual="$(sha256sum -- "$archive")" || return 1
  if [[ "${actual%% *}" != "$QODANA_CLI_SHA256" ]]; then
    echo 'Qodana CLI archive checksum mismatch; extraction refused.' >&2
    return 1
  fi
  # Extract only the authenticated executable, never arbitrary archive paths.
  tar --extract --gzip --file "$archive" --directory "$destination" \
    --no-same-owner --no-same-permissions -- qodana || return 1
  chmod 0755 "$destination/qodana"
}

install_qodana_cli() {
  if [[ "$#" -ne 0 || "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo 'Qodana CLI installation supports Linux x86_64 runners without arguments.' >&2
    return 1
  fi
  if [[ "${RUNNER_TEMP:-}" != /* || ! -d "$RUNNER_TEMP" || -L "$RUNNER_TEMP" || \
        "$RUNNER_TEMP" == *$'\n'* || ! -f "${GITHUB_PATH:-}" || -L "$GITHUB_PATH" ]]; then
    echo 'Qodana CLI installation requires runner-owned temporary and PATH files.' >&2
    return 1
  fi
  local installation archive
  installation="$(mktemp -d "${RUNNER_TEMP%/}/qodana-cli-${QODANA_CLI_VERSION}.XXXXXXXX")"
  archive="$installation/qodana-linux.tar.gz"
  # The official binary API avoids the browser release route's repeated 504s.
  # Public assets need no token. Both routes retain the same authenticated bytes.
  if ! curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' \
    --connect-timeout 20 --max-time 180 --retry 3 --retry-delay 5 --retry-max-time 240 \
    --header 'Accept: application/octet-stream' --header 'X-GitHub-Api-Version: 2022-11-28' \
    --output "$archive" \
    'https://api.github.com/repos/JetBrains/qodana-cli/releases/assets/585558698'; then
    curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' \
      --connect-timeout 20 --max-time 180 --retry 3 --retry-delay 5 --retry-max-time 240 \
      --output "$archive" \
      "https://github.com/JetBrains/qodana-cli/releases/download/v${QODANA_CLI_VERSION}/qodana_linux_x86_64.tar.gz"
  fi
  install_verified_qodana_archive "$archive" "$installation"
  "$installation/qodana" --version
  printf '%s\n' "$installation" >> "$GITHUB_PATH"
  printf 'Installed Qodana CLI %s; archive SHA-256 %s\n' "$QODANA_CLI_VERSION" "$QODANA_CLI_SHA256"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  install_qodana_cli "$@"
fi
