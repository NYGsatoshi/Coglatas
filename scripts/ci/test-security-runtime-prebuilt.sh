#!/usr/bin/env bash
# Secretless negative controls for the opt-in, exact-revision image builder.
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
source scripts/ci/build-security-runtime-prebuilt.sh

fail() { echo "Security prebuilt image policy test failed: $*" >&2; exit 1; }

GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SECURITY_PREBUILT_SHADOW=1 security_prebuilt_shadow_allowed ||
  fail "opted-in test branch PR rejected"

for event in push workflow_dispatch pull_request_target; do
  if GITHUB_ACTIONS=true GITHUB_EVENT_NAME="$event" GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SECURITY_PREBUILT_SHADOW=1 security_prebuilt_shadow_allowed; then
    fail "untrusted event accepted"
  fi
done
for base in main securitytestimplimation SecurityTestImplementation ""; do
  if GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF="$base" COGLATAS_SECURITY_PREBUILT_SHADOW=1 security_prebuilt_shadow_allowed; then
    fail "unexpected base branch accepted"
  fi
done
for shadow in "" 0 true 2; do
  if GITHUB_ACTIONS=true GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SECURITY_PREBUILT_SHADOW="$shadow" security_prebuilt_shadow_allowed; then
    fail "invalid opt-in accepted"
  fi
done
if GITHUB_ACTIONS=false GITHUB_EVENT_NAME=pull_request GITHUB_BASE_REF=SecurityTestImplimation COGLATAS_SECURITY_PREBUILT_SHADOW=1 security_prebuilt_shadow_allowed; then
  fail "non-GitHub action accepted"
fi

stamp=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
other=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
security_prebuilt_stamp_valid "$stamp" "$stamp" "$stamp" || fail "valid SHA combination rejected"
for variant in "$other" "" not-a-sha; do
  if security_prebuilt_stamp_valid "$stamp" "$variant" "$stamp"; then
    fail "stale/corrupt producer accepted"
  fi
  if security_prebuilt_stamp_valid "$stamp" "$stamp" "$variant"; then
    fail "stale/corrupt workflow SHA accepted"
  fi
done

tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/artifacts/ci" "$tmp/src/Coglatas.Web/bin/Release/net10.0" \
  "$tmp/src/Coglatas.Infrastructure/bin/Release/net10.0" \
  "$tmp/src/Coglatas.Web/obj" "$tmp/src/Coglatas.Infrastructure/obj"
printf '%s\n' "$stamp" > "$tmp/artifacts/ci/dotnet-build-sha"
for relative in \
  src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll \
  src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll \
  src/Coglatas.Web/obj/project.assets.json \
  src/Coglatas.Infrastructure/obj/project.assets.json; do
  printf 'synthetic-fixture\n' > "$tmp/$relative"
done
security_prebuilt_required_outputs "$tmp" || fail "valid synthetic producer outputs rejected"
rm "$tmp/src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll"
if security_prebuilt_required_outputs "$tmp"; then
  fail "missing infrastructure assembly accepted"
fi

dockerfile=infra/docker/security-runtime-prebuilt.Dockerfile
grep -Fq 'COPY --from=security-prebuilt /src/ /src/src/' "$dockerfile" ||
  fail "producer context not copied into shadow image"
grep -Fq 'COPY --from=security-prebuilt /artifacts/ci/dotnet-build-sha' "$dockerfile" ||
  fail "producer stamp is not checked inside the image"
grep -Eq '^ARG[[:space:]]+COGLATAS_TESTED_SHA([[:space:]]|$)' "$dockerfile" ||
  fail "Docker image does not declare the checkout SHA"
grep -E '^RUN[[:space:]]+test[[:space:]]+-n' "$dockerfile" |
  grep -Fq '"$COGLATAS_TESTED_SHA"' ||
  fail "Docker image does not require a nonempty checkout SHA"

# Only active RUN continuation guards qualify; a commented-out copy of a
# required test must not satisfy the negative-control gate.
active_run_guard() {
  local source="$1" needle="$2"
  grep -E '^[[:space:]]*&&[[:space:]]+test([[:space:]]|$)' "$source" |
    grep -Fq -- "$needle"
}
require_active_run_guard() {
  local needle="$1" description="$2"
  active_run_guard "$dockerfile" "$needle" ||
    fail "Docker image does not actively enforce $description"
}
require_active_run_guard 'test "$COGLATAS_TESTED_SHA" = "$(cat /tmp/security-producer-sha)"' "producer SHA equality"
require_active_run_guard 'test -s src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll' "Web Release assembly"
require_active_run_guard 'test -s src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll' "Infrastructure Release assembly"
require_active_run_guard 'test -s src/Coglatas.Web/obj/project.assets.json' "Web project assets"
require_active_run_guard 'test -s src/Coglatas.Infrastructure/obj/project.assets.json' "Infrastructure project assets"

printf '# && test "$COGLATAS_TESTED_SHA" = "$(cat /tmp/security-producer-sha)"\\n' > "$tmp/comment-only.Dockerfile"
if active_run_guard "$tmp/comment-only.Dockerfile" 'test "$COGLATAS_TESTED_SHA" = "$(cat /tmp/security-producer-sha)"'; then
  fail "Commented-out in-image guard incorrectly qualifies"
fi

# The workflow must not infer a shadow opt-in solely from event/base; the
# manually applied PR label is an independent, default-off allowlist.
grep -Fq "contains(github.event.pull_request.labels.*.name, 'ci-security-prebuilt-shadow')" .github/workflows/ci.yml ||
  fail "Security image trial no longer requires an explicit PR label"
grep -Fq 'dotnet restore src/Coglatas.Web/Coglatas.Web.csproj' "$dockerfile" ||
  fail "EF tooling project restore was removed"
if grep -Eq '(^RUN[[:space:]]+dotnet[[:space:]]+(build|publish)|^[[:space:]]*&&[[:space:]]+dotnet[[:space:]]+(build|publish))' "$dockerfile"; then
  fail "experimental image still recompiles Web"
fi
echo 'SEC-02 producer-image shadow eligibility, SHA and required-artifact negative controls PASS.'
