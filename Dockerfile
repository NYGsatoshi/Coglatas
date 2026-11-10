# syntax=docker/dockerfile:1.28

FROM node:26@sha256:438603bbb5cda404b9416f0b5c8026f61501ab911760d861ec28c36b5d675725 AS frontend-deps
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

FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade \
        apt libapt-pkg6.0t64 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 2.8.4ubuntu0.1 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libapt-pkg6.0t64)" ge 2.8.4ubuntu0.1 \
    && apt-get install -y --no-install-recommends \
        curl \
        openssl \
        libssl3t64 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/storage/uploads
COPY --from=build /app/publish .
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet Coglatas.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
