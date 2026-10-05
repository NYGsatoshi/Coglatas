FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
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
