#!/usr/bin/env bash
set -Eeuo pipefail

[[ "$GITHUB_ACTIONS" == true && "$GITHUB_JOB" == frontend-test ]]
[[ "$GITHUB_RUN_ID" =~ ^[1-9][0-9]{0,19}$ && "$GITHUB_RUN_ATTEMPT" == 1 ]]
[[ "$D12_GENERATION" == obsolete || "$D12_GENERATION" == successor ]]
[[ "$D12_EVIDENCE" == "$RUNNER_TEMP/d12-evidence" ]]
[[ "$(git rev-parse HEAD)" == "$D12_SOURCE" ]]

helper=.d12/security-source/scripts/ci/cleanup-pr-ci-resources.sh
[[ "$(git -C .d12/security-source rev-parse HEAD)" == "$SECURITY_SOURCE" ]]
[[ "$(git hash-object "$helper")" == "$CLEANUP_BLOB" ]]

project="coglatas-playwright-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT-frontend-test"
owner="com.docker.compose.project=$project"
sentinel="${project}_unrelated_d12"
image="coglatas-d12-cleanup:$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"

case "$1" in
  prepare)
    mkdir -m 700 "$D12_EVIDENCE"
    git rev-parse HEAD HEAD^{tree} > "$D12_EVIDENCE/git-source.txt"
    sha256sum "$helper" > "$D12_EVIDENCE/helper.sha256"
    printf '%s\n' "$D12_GENERATION" "$GITHUB_RUN_ID" "$GITHUB_RUN_ATTEMPT" > "$D12_EVIDENCE/run.txt"

    # Import an empty filesystem. The stopped fixture executes no image entry point.
    tar -cf - --files-from /dev/null | docker import - "$image" > "$D12_EVIDENCE/image-id.txt"
    network=$(docker network create --label "$owner" "${project}_default")
    volume=$(docker volume create --label "$owner" "${project}_data")
    container=$(docker container create --label "$owner" --name "${project}_d12" \
      --network "$network" --mount "type=volume,source=$volume,target=/data" "$image" /never-started)
    docker volume create --label "com.docker.compose.project=${project}-sentinel" "$sentinel" > /dev/null

    printf '%s\n' "$container" "$network" "$volume" > "$D12_EVIDENCE/owned-ids.txt"
    docker container inspect --format '{{.Id}} {{.State.Status}}' "$container" | tee "$D12_EVIDENCE/container-before.txt"
    test "$(docker network inspect --format '{{.Id}}' "$network")" = "$network"
    test "$(docker volume inspect --format '{{.Name}}' "$volume")" = "$volume"
    printf 'D12 fixtures present for %s source %s.\n' "$D12_GENERATION" "$D12_SOURCE"
    ;;
  verify)
    mapfile -t owned < "$D12_EVIDENCE/owned-ids.txt"
    [[ "${#owned[@]}" == 3 ]]
    trap 'docker volume rm "$sentinel" > /dev/null || true; docker image rm "$image" > /dev/null || true' EXIT
    bash "$helper" frontend-test 2>&1 | tee "$D12_EVIDENCE/helper.log"

    # Verify the exact resource identities against successful Docker inventories.
    containers=$(docker container ls --all --quiet --no-trunc)
    networks=$(docker network ls --quiet --no-trunc)
    volumes=$(docker volume ls --quiet)
    [[ $'\n'"$containers"$'\n' != *$'\n'"${owned[0]}"$'\n'* ]]
    [[ $'\n'"$networks"$'\n' != *$'\n'"${owned[1]}"$'\n'* ]]
    [[ $'\n'"$volumes"$'\n' != *$'\n'"${owned[2]}"$'\n'* ]]
    [[ "$(docker volume inspect --format '{{.Name}}' "$sentinel")" == "$sentinel" ]]
    printf 'D12 %s cleanup verified: exact container, network and volume absent; unrelated volume retained.\n' \
      "$D12_GENERATION" | tee "$D12_EVIDENCE/result.txt"
    ;;
  *)
    exit 2
    ;;
esac
