# syntax=docker/dockerfile:1
# Isolated experimental Security Runtime image for PRs to SecurityTestImplimation.
# The regular runtime Dockerfile remains the only path for main/deep/local.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b

WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

# Source context intentionally stays separate from the SHA-checked producer
# outputs: .dockerignore continues to exclude bin/obj and all artifacts.
COPY . .
# Named context is staged from the exact-revision producer artifact by a guarded
# CI script. No host secrets, arbitrary workspace files or NuGet caches are staged.
COPY --from=security-prebuilt /src/ /src/src/
COPY --from=security-prebuilt /artifacts/ci/dotnet-build-sha /tmp/security-producer-sha

ARG COGLATAS_TESTED_SHA
RUN test -n "$COGLATAS_TESTED_SHA" \
    && test "$COGLATAS_TESTED_SHA" = "$(cat /tmp/security-producer-sha)" \
    && test -s src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll \
    && test -s src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll \
    && test -s src/Coglatas.Web/obj/project.assets.json \
    && test -s src/Coglatas.Infrastructure/obj/project.assets.json \
    && mkdir -p \
      src/Coglatas.Web/wwwroot \
      /app/storage/uploads \
      /app/data/protection-keys \
    && dotnet tool restore \
    && dotnet restore src/Coglatas.Web/Coglatas.Web.csproj \
      --disable-parallel \
      /p:RestoreFallbackFolders=

CMD ["dotnet", "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"]
