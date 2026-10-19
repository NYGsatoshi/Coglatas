#!/usr/bin/env bash
# Sourced by the four legacy acceptance wrappers after their Compose scope exists.

acceptance_prepare_image_reuse() {
  [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == "1" ]] || return 0
  ACCEPTANCE_PREBUILT_IMAGE="${COGLATAS_APP_IMAGE:-}"
  ACCEPTANCE_PREBUILT_IMAGE_ID="${COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID:-}"
  ACCEPTANCE_PREBUILT_ENGINE_ID="${COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID:-$ACCEPTANCE_PREBUILT_IMAGE_ID}"
  if [[ -z "$ACCEPTANCE_PREBUILT_IMAGE" || ! "$ACCEPTANCE_PREBUILT_IMAGE_ID" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo "Prebuilt acceptance reuse requires the restored image name and producer image ID." >&2
    return 1
  fi
  acceptance_verify_image_reuse || return 1
  local configured_image
  configured_image="$("${compose[@]}" config --format json 2>/dev/null | python3 -c '
import json, sys
try:
    print(json.load(sys.stdin)["services"]["app"]["image"])
except (ValueError, KeyError, TypeError):
    raise SystemExit("Acceptance app image configuration is unavailable.")
')" || return 1
  if [[ "$configured_image" != "$ACCEPTANCE_PREBUILT_IMAGE" ]]; then
    echo "Acceptance Compose app image differs from the restored producer image." >&2
    return 1
  fi
}

acceptance_verify_image_reuse() {
  [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == "1" ]] || return 0
  local actual_id container_id container_image
  actual_id="$(docker image inspect --format '{{.Id}}' "$ACCEPTANCE_PREBUILT_IMAGE" 2>/dev/null)" || {
    echo "Restored acceptance image is unavailable; refusing to build or pull it." >&2
    return 1
  }
  if [[ "$actual_id" != "$ACCEPTANCE_PREBUILT_ENGINE_ID" ]]; then
    echo "Acceptance image tag no longer matches the restored producer image ID." >&2
    return 1
  fi
  if [[ "$ACCEPTANCE_PREBUILT_ENGINE_ID" != "$ACCEPTANCE_PREBUILT_IMAGE_ID" ]]; then
    python3 "$(dirname "${BASH_SOURCE[0]}")/runtime_image_identity.py" verify \
      --archive "${COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE:-}" \
      --source-sha "${COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA:-}" --image-name "$ACCEPTANCE_PREBUILT_IMAGE" \
      --producer-id "$ACCEPTANCE_PREBUILT_IMAGE_ID" --engine-id "$ACCEPTANCE_PREBUILT_ENGINE_ID" \
      --binding "${COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING:-}" \
      --dotnet-stamp "${COGLATAS_REUSE_PREBUILT_DOTNET_STAMP:-}" || return 1
  fi
  if [[ "${1:-}" == "container" ]]; then
    container_id="$("${compose[@]}" ps --all -q app | head -n 1)" || return 1
    [[ -n "$container_id" ]] || { echo "Acceptance app container is unavailable." >&2; return 1; }
    container_image="$(docker inspect --format '{{.Image}}' "$container_id" 2>/dev/null)" || return 1
    if [[ "$container_image" != "$ACCEPTANCE_PREBUILT_ENGINE_ID" ]]; then
      echo "Acceptance app container differs from the restored producer image ID." >&2
      return 1
    fi
  fi
}

acceptance_compose_up() {
  local mode="$1"
  shift
  local -a build_args=()
  if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == "1" ]]; then
    acceptance_verify_image_reuse || return 1
    local argument
    for argument in "$@"; do
      case "$argument" in
        --build|--build=*|--no-build|--no-build=*|--pull|--pull=*) echo "Acceptance reuse cannot override build or pull policy." >&2; return 1 ;;
      esac
    done
    build_args=(--no-build --pull never)
  elif [[ "$mode" == "build" ]]; then
    build_args=(--build)
  fi
  "${compose[@]}" up "${build_args[@]}" "$@" || return $?
  acceptance_verify_image_reuse container
}

acceptance_compose_run() {
  local mode="$1"
  shift
  local -a build_args=(--build)
  if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == "1" ]]; then
    acceptance_verify_image_reuse container || return 1
    local argument
    for argument in "$@"; do
      case "$argument" in
        --build|--build=*) echo "Acceptance reuse cannot build the probe image." >&2; return 1 ;;
        --no-deps|--no-deps=*) echo "Acceptance reuse cannot override the probe dependency policy." >&2; return 1 ;;
      esac
    done
    build_args=(--no-deps)
  elif [[ "$mode" == "no-deps" ]]; then
    build_args+=(--no-deps)
  fi
  "${compose[@]}" run "${build_args[@]}" "$@" || return $?
  acceptance_verify_image_reuse container || return 1
}

acceptance_record_image_reuse() {
  [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == "1" ]] || return 0
  acceptance_verify_image_reuse container || return 1
  mkdir -p "$(dirname "$1")" || return 1
  if [[ "$ACCEPTANCE_PREBUILT_ENGINE_ID" == "$ACCEPTANCE_PREBUILT_IMAGE_ID" ]]; then
    printf '{"schemaVersion":1,"expectedProducerImageId":"%s","tagAndAppContainerMatched":true,"scope":"within-this-acceptance-wrapper"}\n' \
      "$ACCEPTANCE_PREBUILT_IMAGE_ID" > "$1"
  else
    local binding_sha
    binding_sha="$(sha256sum "$COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING")" || return 1
    binding_sha="${binding_sha%% *}"
    printf '{"schemaVersion":2,"expectedProducerImageId":"%s","engineImageId":"%s","bindingSha256":"%s","tagAndAppContainerMatched":true,"scope":"within-this-acceptance-wrapper"}\n' \
      "$ACCEPTANCE_PREBUILT_IMAGE_ID" "$ACCEPTANCE_PREBUILT_ENGINE_ID" "$binding_sha" > "$1"
  fi
}
