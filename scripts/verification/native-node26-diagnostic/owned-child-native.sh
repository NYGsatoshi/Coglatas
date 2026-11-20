set -euo pipefail
mkdir -p /home/runner/work/Coglatas/Coglatas
tar -xf /input/source.tar -C /home/runner/work/Coglatas/Coglatas
cd /home/runner/work/Coglatas/Coglatas
export ASPNETCORE_ENVIRONMENT=Test COGLATAS_TEST_USE_MIGRATED_TEMPLATE=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_UI_LANGUAGE=en-US CI=true
export NUGET_PACKAGES=/evidence/nuget-cache
dotnet --info
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj -c Release \
  -p:SourceRevisionId="$NATIVE_SOURCE_SHA" -p:RepositoryCommit="$NATIVE_SOURCE_SHA" \
  --artifacts-path /evidence/build \
  --filter "$NATIVE_TEST_FILTER" \
  --logger 'trx;LogFileName=native-child.trx' \
  --results-directory /evidence/focused \
  --collect 'XPlat Code Coverage;Format=opencover'
if [[ "$NATIVE_RUN_OWNERS" == true ]]; then
  dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj -c Release --no-build --no-restore \
    --artifacts-path /evidence/build \
    --filter 'FullyQualifiedName~LegacyChannelLifecyclePersistencePostgreSqlTests|FullyQualifiedName~SecurityArchitectureSignalRTests' \
    --logger 'trx;LogFileName=native-child.trx' \
    --results-directory /evidence/owning \
    --collect 'XPlat Code Coverage;Format=opencover'
fi
