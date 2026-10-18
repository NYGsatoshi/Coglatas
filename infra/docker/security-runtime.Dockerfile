FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa

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

CMD ["dotnet", "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"]
