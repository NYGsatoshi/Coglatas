FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS runtime
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

COPY artifacts/main-runtime/publish/ ./
RUN rm -rf /app/wwwroot && mkdir -p /app/wwwroot
COPY artifacts/main-runtime/frontend/ /app/wwwroot/

ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet Coglatas.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
