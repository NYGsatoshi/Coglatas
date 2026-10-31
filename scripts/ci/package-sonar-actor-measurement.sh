#!/usr/bin/env bash
# Called only by the closed same-run measurement entry, before scanner credentials.
set -Eeuo pipefail
[[ $# == 1 && -n "${RUNNER_TEMP:-}" ]] || exit 1
runtime="$1"
[[ "$runtime" == "$(realpath "$runtime")" && -d "$runtime" && ! -L "$runtime" &&
   "$(dirname "$(dirname "$runtime")")" == "$RUNNER_TEMP" &&
   "$(basename "$(dirname "$runtime")")" =~ ^sonar-actor-[a-zA-Z0-9]+$ && "$(basename "$runtime")" == runtime ]] || exit 1
[[ ! -e .sonarqube && -z "${SONAR_TOKEN:-}" && -z "${SYNCFUSION_LICENSE:-}" ]] || exit 1
source_sha="$(git rev-parse HEAD)"
[[ "$source_sha" =~ ^[0-9a-f]{40}$ && "$source_sha" == "$GITHUB_SHA" && -z "$(git status --porcelain --untracked-files=no)" ]] || exit 1
[[ ! -L artifacts && ! -L artifacts/ci && ! -e artifacts/main-runtime && ! -L artifacts/main-runtime ]] || exit 1
mkdir -p artifacts/main-runtime/publish artifacts/main-runtime/frontend artifacts/ci
# No licensed Main artifact or build:licensed path is used.
npm --prefix frontend run build
[[ -s frontend/dist/frontend/browser/index.html && -s frontend/dist/frontend/browser/angular-app.marker ]] || exit 1
dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m
dotnet publish src/Coglatas.Web/Coglatas.Web.csproj --configuration Release --no-build --no-restore --output artifacts/main-runtime/publish
cp -a frontend/dist/frontend/browser/. artifacts/main-runtime/frontend/
printf '%s\n' "$source_sha" > artifacts/ci/dotnet-build-sha
printf '%s\n' "$source_sha" > "$runtime/source-sha"
image="coglatas-sonar-actor-measurement:$source_sha"
docker build --file infra/docker/runtime-prebuilt.Dockerfile --tag "$image" .
printf '%s\n' "$image" > "$runtime/runtime-image-name"
docker image inspect --format '{{.Id}}' "$image" > "$runtime/runtime-image-id"
docker save "$image" | gzip > "$runtime/runtime-image.tar.gz"
[[ "$(git rev-parse HEAD)" == "$source_sha" && -z "$(git status --porcelain --untracked-files=no)" ]] || exit 1
