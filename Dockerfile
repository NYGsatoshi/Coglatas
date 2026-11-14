# syntax=docker/dockerfile:1.29

FROM node:26@sha256:e18c3ccdf7d71619990b0e85639f506e2028a95410999cab24cc373cbd10e0ac AS frontend-deps
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json frontend/.npmrc ./
COPY scripts/ci/verify-npm-lockfile.mjs /usr/local/lib/coglatas/verify-npm-lockfile.mjs
COPY scripts/ci/verify-docker-frontend-deps.mjs /usr/local/lib/coglatas/verify-docker-frontend-deps.mjs
RUN --mount=type=cache,id=coglatas-docker-npm,target=/root/.npm,sharing=locked \
    npm install --global npm@11.17.0 \
      --ignore-scripts \
      --allow-git=none \
      --allow-remote=none \
      --no-audit \
      --no-fund
RUN node /usr/local/lib/coglatas/verify-npm-lockfile.mjs .
RUN --mount=type=cache,id=coglatas-docker-npm,target=/root/.npm,sharing=locked \
    npm ci \
      --ignore-scripts \
      --prefer-online \
      --strict-allow-scripts \
      --allow-git=none \
      --allow-remote=none \
      --no-audit \
      --no-fund
# Test exactly the dependencies from the production Dockerfile without exposing a license.
# The PR smoke is a separate target; full production still uses frontend-build.
FROM frontend-deps AS frontend-unlicensed-smoke
COPY frontend/ ./
RUN --network=none node /usr/local/lib/coglatas/verify-docker-frontend-deps.mjs
RUN CI=1 npm run build \
    && test -s dist/coglatas-web/index.html \
    && test -s dist/coglatas-web/angular-app.marker

FROM frontend-deps AS frontend-build
COPY frontend/ ./
RUN --mount=type=secret,id=syncfusion_license,required=true \
    --mount=type=cache,id=coglatas-docker-angular,target=/src/frontend/.angular/cache,sharing=locked \
    set -eu; \
    test -x node_modules/.bin/syncfusion-license || { echo "Syncfusion License CLI is not installed." >&2; exit 1; }; \
    SYNCFUSION_LICENSE="$(tr -d '\r\n' < /run/secrets/syncfusion_license)"; \
    test -n "$SYNCFUSION_LICENSE" || { echo "SYNCFUSION_LICENSE is not configured." >&2; exit 1; }; \
    export SYNCFUSION_LICENSE; \
    npm run build:licensed; \
    ! grep -R -F -q -- "$SYNCFUSION_LICENSE" dist || { echo "Syncfusion license material was found in frontend build output." >&2; exit 1; }; \
    unset SYNCFUSION_LICENSE

FROM mcr.microsoft.com/dotnet/sdk:11.0.100@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
WORKDIR /src

COPY Coglatas.slnx ./
COPY src/Coglatas.Domain/Coglatas.Domain.csproj src/Coglatas.Domain/
COPY src/Coglatas.UI.Core/Coglatas.UI.Core.csproj src/Coglatas.UI.Core/
COPY src/Coglatas.Application/Coglatas.Application.csproj src/Coglatas.Application/
COPY src/Coglatas.Infrastructure/Coglatas.Infrastructure.csproj src/Coglatas.Infrastructure/
COPY src/Coglatas.Web/Coglatas.Web.csproj src/Coglatas.Web/
COPY src/Coglatas.Application/packages.lock.json src/Coglatas.Application/
COPY src/Coglatas.Infrastructure/packages.lock.json src/Coglatas.Infrastructure/
COPY src/Coglatas.Web/packages.lock.json src/Coglatas.Web/
RUN --mount=type=cache,id=coglatas-docker-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode

COPY . .
RUN rm -rf src/Coglatas.Web/wwwroot/*
COPY --from=frontend-build /src/frontend/dist/coglatas-web/ src/Coglatas.Web/wwwroot/
# BuildKit cache mounts are mutable and can be pruned independently from cached
# restore layers. Force a restore in the same cache mount as publish so missing
# NuGet packages are repaired before --no-restore is used.
RUN --mount=type=cache,id=coglatas-docker-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode --force && \
    dotnet publish src/Coglatas.Web/Coglatas.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS runtime
WORKDIR /app
# The pinned .NET 11 runtime uses Ubuntu 26.04 (resolute), whose libapt ABI is 7.0.
# Keep a fail-closed APT security floor; do not silently skip the library upgrade.
# Pebble is an unused inherited supervisor: our ENTRYPOINT runs dotnet directly.
# Its bundled Go stdlib is vulnerable; do not ship the unused binary.
RUN set -eu; \
    . /etc/os-release; \
    test "$ID" = ubuntu; \
    test "$VERSION_CODENAME" = resolute; \
    apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade \
        apt libapt-pkg7.0 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 3.2.1ubuntu0.1 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libapt-pkg7.0)" ge 3.2.1ubuntu0.1 \
    && apt-get install -y --no-install-recommends \
        curl \
        openssl \
        libssl3t64 \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /usr/bin/pebble \
    && test ! -e /usr/bin/pebble \
    && install -d -m 0700 -o "$APP_UID" /app/storage/uploads /app/data/protection-keys
COPY --from=build /app/publish .
USER $APP_UID
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet Coglatas.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
