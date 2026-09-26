#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$repo_root"

spec="${1:-artifacts/openapi/coglatas-openapi.json}"
scratch_parent="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
mkdir -p "$(dirname "$spec")"
first="$(mktemp "$scratch_parent/coglatas-openapi.first.XXXXXX.json")"
trap 'rm -f "$first"' EXIT

# Contract generation executes the application through dotnet-getdocument, so it
# must carry the same explicit Test-only activation boundary as SEC-02/SEC-03.
# Never inherit a caller's Production environment for this synthetic contract
# generation path.
export ASPNETCORE_ENVIRONMENT=Test
export DOTNET_ENVIRONMENT=Test

# Contract generation is a read-only build-time operation. Fail closed if the
# application accidentally tries to use a database or any ordinary seed path.
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=1;Database=sec01_openapi;Username=unused;Password=unused;Timeout=1;Command Timeout=1"
export Tenancy__AppMode=SaaS
export Tenancy__SeedOnStartup=false
export UiShell__SeedOnStartup=false
export BrowserSmokeSeed__Enabled=false
export COGLATAS_BROWSER_SMOKE_SEED_ENABLED=false
export DemoDataset__Enabled=false
export COGLATAS_DEMO_DATASET_ENABLED=false
export COGLATAS_SEED_ADMIN_ENABLED=false
export COGLATAS_BOOTSTRAP_ADMIN_EMAIL=""
export BootstrapAdmin__Email=""

generate_openapi() {
  dotnet build src/Coglatas.Web/Coglatas.Web.csproj \
    --configuration Release \
    --no-restore \
    --no-incremental \
    --disable-build-servers \
    -m:1 \
    -p:GenerateSecurityOpenApiContract=true
}

rm -f "$spec"
generate_openapi
python3 scripts/ci/verify-openapi.py "$spec"
cp "$spec" "$first"

rm "$spec"
generate_openapi
python3 scripts/ci/verify-openapi.py "$spec"
if ! cmp --silent "$first" "$spec"; then
  echo "SEC-01 OpenAPI output is not deterministic across repeated builds." >&2
  diff -u "$first" "$spec" || true
  exit 1
fi

sha256sum "$spec"
