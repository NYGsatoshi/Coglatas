#!/usr/bin/env bash
# Shared fail-closed process orchestration for CI lanes.
set -Eeuo pipefail

declare -a CI_PARALLEL_NAMES=()
declare -a CI_PARALLEL_PIDS=()
declare -a CI_PARALLEL_LOGS=()
declare -a CI_PARALLEL_TIMINGS=()

ci_parallel_start() {
  local name="$1"
  shift

  # Names become filenames and summary rows; reject arbitrary path/Markdown.
  if [[ ! "$name" =~ ^[a-zA-Z][a-zA-Z0-9_-]{0,63}$ ]] || (($# == 0)); then
    echo "::error::Invalid parallel lane name or empty command." >&2
    return 2
  fi
  local existing
  for existing in "${CI_PARALLEL_NAMES[@]}"; do
    if [[ "$existing" == "$name" ]]; then
      echo "::error::Duplicate parallel lane name." >&2
      return 2
    fi
  done

  local root="${CI_PARALLEL_LOG_DIR:-${RUNNER_TEMP:-/tmp}/coglatas-parallel}"
  mkdir -p "$root"
  local log="$root/${name}.log"
  local elapsed_file="$root/${name}.seconds"
  rm -f -- "$log" "$elapsed_file"

  (
    set -Eeuo pipefail
    # Per-worker clock: the parent may wait for unrelated workers first.
    SECONDS=0
    trap 'printf "%s\n" "$SECONDS" > "$elapsed_file" || true' EXIT
    "$@"
  ) >"$log" 2>&1 &

  CI_PARALLEL_NAMES+=("$name")
  CI_PARALLEL_PIDS+=("$!")
  CI_PARALLEL_LOGS+=("$log")
  CI_PARALLEL_TIMINGS+=("$elapsed_file")
}

ci_parallel_wait() {
  local failed=0
  local -a statuses=()
  local i rc

  if ((${#CI_PARALLEL_PIDS[@]} == 0)); then
    echo "No parallel CI lanes were selected."
    return 0
  fi

  for i in "${!CI_PARALLEL_PIDS[@]}"; do
    if wait "${CI_PARALLEL_PIDS[$i]}"; then
      rc=0
    else
      rc=$?
      failed=1
    fi
    statuses+=("$rc")
  done

  # Advisory status/duration only. Do not copy commands, args, logs or env.
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    printf '### CI parallel lane timing (advisory)\n\n| Lane | Result | Seconds |\n| --- | --- | ---: |\n' >> "$GITHUB_STEP_SUMMARY" || true
  fi

  for i in "${!CI_PARALLEL_NAMES[@]}"; do
    local elapsed="unavailable"
    local raw=""
    if [[ -f "${CI_PARALLEL_TIMINGS[$i]}" ]]; then
      IFS= read -r raw < "${CI_PARALLEL_TIMINGS[$i]}" || true
      if [[ "$raw" =~ ^[0-9]{1,8}$ ]]; then
        elapsed="$raw"
      fi
    fi
    local outcome="PASS"
    if ((statuses[i] != 0)); then
      outcome="FAIL (exit ${statuses[i]})"
    fi
    echo "CI parallel lane: ${CI_PARALLEL_NAMES[$i]} result=${outcome} elapsed_seconds=${elapsed}"
    if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
      printf '| %s | %s | %s |\n' "${CI_PARALLEL_NAMES[$i]}" "$outcome" "$elapsed" >> "$GITHUB_STEP_SUMMARY" || true
    fi

    echo "::group::${CI_PARALLEL_NAMES[$i]} (exit ${statuses[$i]})"
    cat "${CI_PARALLEL_LOGS[$i]}" || true
    echo "::endgroup::"
  done

  if ((failed != 0)); then
    echo "::error::One or more parallel CI lanes failed."
    return 1
  fi

  echo "All selected parallel CI lanes passed."
}
