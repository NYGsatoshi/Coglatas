FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e

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
