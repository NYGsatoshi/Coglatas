#!/usr/bin/env bash
set -euo pipefail

readonly COSIGN_VERSION="3.1.3"
readonly COSIGN_LINUX_AMD64_SHA256="4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71"
readonly COSIGN_BINARY="cosign-linux-amd64"
readonly URL="https://github.com/sigstore/cosign/releases/download/v${COSIGN_VERSION}/${COSIGN_BINARY}"

install_dir="${1:-${RUNNER_TEMP:-/tmp}/coglatas-cosign/bin}"
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

mkdir -p "$install_dir"
curl \
  --fail \
  --silent \
  --show-error \
  --location \
  --proto '=https' \
  --tlsv1.2 \
  "$URL" \
  --output "$tmp_dir/$COSIGN_BINARY"
printf '%s  %s\n' "$COSIGN_LINUX_AMD64_SHA256" "$tmp_dir/$COSIGN_BINARY" |
  sha256sum --check --strict
install -m 0755 "$tmp_dir/$COSIGN_BINARY" "$install_dir/cosign"

version_output="$("$install_dir/cosign" version)"
printf '%s\n' "$version_output"
printf '%s\n' "$version_output" | grep -Fq "v${COSIGN_VERSION}" || {
  echo "Installed Cosign version does not match pinned version ${COSIGN_VERSION}." >&2
  exit 1
}

if [[ -n "${GITHUB_PATH:-}" ]]; then
  printf '%s\n' "$install_dir" >> "$GITHUB_PATH"
else
  printf 'Add %s to PATH to use Cosign.\n' "$install_dir"
fi
