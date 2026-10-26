FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7

WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY . .

RUN mkdir -p \
      src/Coglatas.Web/wwwroot \
      /app/storage/uploads \
      /app/data/protection-keys \
    && dotnet tool restore \
    && dotnet restore src/Coglatas.Web/Coglatas.Web.csproj \
      --disable-parallel \
      /p:RestoreFallbackFolders=

CMD ["dotnet", "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"]
