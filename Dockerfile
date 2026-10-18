# syntax=docker/dockerfile:1.7

FROM node:24@sha256:14078204b6bb268c107ceff6d3c43f45a888105e5c2b18b14c32b3409d93a1b0 AS frontend-deps
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

FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src

COPY Coglatas.slnx ./
COPY src/Coglatas.Domain/Coglatas.Domain.csproj src/Coglatas.Domain/
COPY src/Coglatas.Application/Coglatas.Application.csproj src/Coglatas.Application/
COPY src/Coglatas.Infrastructure/Coglatas.Infrastructure.csproj src/Coglatas.Infrastructure/
COPY src/Coglatas.Web/Coglatas.Web.csproj src/Coglatas.Web/
RUN --mount=type=cache,id=coglatas-docker-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Coglatas.Web/Coglatas.Web.csproj

COPY . .
RUN rm -rf src/Coglatas.Web/wwwroot/*
COPY --from=frontend-build /src/frontend/dist/coglatas-web/ src/Coglatas.Web/wwwroot/
# BuildKit cache mounts are mutable and can be pruned independently from cached
# restore layers. Force a restore in the same cache mount as publish so missing
# NuGet packages are repaired before --no-restore is used.
RUN --mount=type=cache,id=coglatas-docker-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --force && \
    dotnet publish src/Coglatas.Web/Coglatas.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
WORKDIR /app
RUN apt-get update \
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
