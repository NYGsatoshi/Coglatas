FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.3@sha256:3a9175b6c54b2f076817af1360f691c9994447b192faa48a1a1f112a516844c7

WORKDIR /workspace

ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=true \
    NUGET_PACKAGES=/root/.nuget/packages \
    BACKEND_USE_WATCH=false

EXPOSE 8080

CMD ["bash", "-lc", "mkdir -p src/Coglatas.Web/wwwroot && dotnet tool restore && dotnet restore src/Coglatas.Web/Coglatas.Web.csproj /p:RestoreFallbackFolders= && dotnet ef database update --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web && if [ \"${BACKEND_USE_WATCH}\" = \"true\" ]; then dotnet watch --project src/Coglatas.Web/Coglatas.Web.csproj run --no-launch-profile; else dotnet run --project src/Coglatas.Web/Coglatas.Web.csproj --no-launch-profile; fi"]
