# Compile-only RESTler toolchain; target execution is a separate SEC-15 slice.
ARG RESTLER_SDK_BASE
FROM ${RESTLER_SDK_BASE}
ENV RESTLER_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
WORKDIR /source
# The caller admits the exact upstream commit and tree before building this context.
COPY src ./src
RUN dotnet restore src/compiler/Restler.CompilerExe/Restler.CompilerExe.fsproj --locked-mode --disable-parallel \
 && dotnet publish src/compiler/Restler.CompilerExe/Restler.CompilerExe.fsproj --no-restore -c Release -f net8.0 -o /compiler
ENTRYPOINT ["dotnet", "/compiler/Restler.CompilerExe.dll"]
