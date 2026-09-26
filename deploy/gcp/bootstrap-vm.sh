#!/usr/bin/env bash
set -euo pipefail

readonly DOCKER_APT_KEY_URL="https://download.docker.com/linux/ubuntu/gpg"
# Docker's legacy Debian/Ubuntu signing key and its current rotated signing key.
# Any future key rotation must be explicitly reviewed before this bootstrap trusts it.
readonly DOCKER_APT_KEY_FINGERPRINT_LEGACY="9DC858229FC7DD38854AE2D88D81803C0EBFCD88"
readonly DOCKER_APT_KEY_FINGERPRINT_CURRENT="060A61C51B558A7F742B77AAC52FEB6B621E9F35"

APP_DIR="${APP_DIR:-/opt/coglatas}"

echo "Installing base packages..."
sudo apt-get update
sudo apt-get install -y ca-certificates curl git gnupg lsb-release openssl

echo "Installing Docker Engine and Compose plugin..."
sudo install -m 0755 -d /etc/apt/keyrings
docker_key_tmp="$(mktemp)"
trap 'rm -f "$docker_key_tmp"' EXIT
curl \
  --fail \
  --silent \
  --show-error \
  --proto '=https' \
  --tlsv1.2 \
  --retry 3 \
  --retry-all-errors \
  --connect-timeout 10 \
  --max-time 120 \
  "$DOCKER_APT_KEY_URL" \
  --output "$docker_key_tmp"

mapfile -t docker_key_fingerprints < <(
  gpg --batch --show-keys --with-colons "$docker_key_tmp" 2>/dev/null |
    awk -F: '
      $1 == "pub" { want_fingerprint = 1; next }
      want_fingerprint && $1 == "fpr" { print toupper($10); want_fingerprint = 0 }
    '
)
if [[ ${#docker_key_fingerprints[@]} -eq 0 ]]; then
  echo "Downloaded Docker apt key did not contain a primary signing key." >&2
  exit 1
fi
for fingerprint in "${docker_key_fingerprints[@]}"; do
  case "$fingerprint" in
    "$DOCKER_APT_KEY_FINGERPRINT_LEGACY"|"$DOCKER_APT_KEY_FINGERPRINT_CURRENT")
      ;;
    *)
      echo "Downloaded Docker apt key contains an unreviewed signing-key fingerprint." >&2
      exit 1
      ;;
  esac
done
sudo install -m 0644 "$docker_key_tmp" /etc/apt/keyrings/docker.asc

. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${UBUNTU_CODENAME:-$VERSION_CODENAME} stable" |
  sudo tee /etc/apt/sources.list.d/docker.list >/dev/null

sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker

echo "Preparing ${APP_DIR}..."
sudo mkdir -p "${APP_DIR}"
sudo chown "${USER}:${USER}" "${APP_DIR}"

if ! groups "${USER}" | grep -q '\bdocker\b'; then
  sudo usermod -aG docker "${USER}"
  echo "Added ${USER} to the docker group. Log out and SSH back in before running Docker without sudo."
fi

sudo docker version
sudo docker compose version

echo "Bootstrap complete."
echo "Next:"
echo "  cd ${APP_DIR}"
echo "  bash ~/coglatas-gcp/gcp/deploy-app.sh"
