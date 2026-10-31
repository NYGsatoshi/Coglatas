FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.3@sha256:3a9175b6c54b2f076817af1360f691c9994447b192faa48a1a1f112a516844c7

WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY . .

RUN mkdir -p \
      src/Coglatas.Web/wwwroot \
      /app/storage/uploads \
      /app/data/protection-keys \
    && dotnet tool restore \
    && dotnet restore src/Coglatas.Web/Coglatas.Web.csproj /p:RestoreFallbackFolders= \
    && dotnet build src/Coglatas.Web/Coglatas.Web.csproj \
      --configuration Release \
      --no-restore \
      --disable-build-servers \
      -m:1

CMD ["dotnet", "src/Coglatas.Web/bin/Release/net11.0/Coglatas.Web.dll"]
