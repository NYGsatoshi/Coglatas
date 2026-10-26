FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:d247d17dd429d96580eeb96cfce220c1089d860dbbf9f94d599ca64cf3604fe4 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        openssl \
        libssl3t64 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/storage/uploads

COPY artifacts/main-runtime/publish/ ./
RUN rm -rf /app/wwwroot && mkdir -p /app/wwwroot
COPY artifacts/main-runtime/frontend/ /app/wwwroot/

ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet Coglatas.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
