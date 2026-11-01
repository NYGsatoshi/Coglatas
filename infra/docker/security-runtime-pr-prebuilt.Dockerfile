# PR-only security runtime consumer. Compile the application ONCE in
# .NET build producer and copy its exact verified binaries into this SDK image.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b

WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY . .

# SEC-02 migration requires the local EF CLI and NuGet dependency graph,
# but application compilation must never recur inside this image.
RUN test -s src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll \
    && test -s src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll \
    && test -s src/Coglatas.Web/obj/project.assets.json \
    && mkdir -p src/Coglatas.Web/wwwroot /app/storage/uploads /app/data/protection-keys \
    && dotnet tool restore \
    && dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --disable-parallel /p:RestoreFallbackFolders= \
    && test -s src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll

CMD ["dotnet", "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"]
