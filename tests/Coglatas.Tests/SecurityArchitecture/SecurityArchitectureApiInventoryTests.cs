using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Coglatas.Web.Controllers;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureApiInventoryTests
{
    [Fact]
    public async Task ActualComposedHostInventoryPreservesAnonymousAndProtectedMetadataWithoutAcceptance() =>
        await ObserveAsync();

    internal static async Task<JsonElement> ObserveAsync(bool savePrivateOutput = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = FindRepositoryRoot();
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(AuthController).Assembly.Location);
            foreach (var argument in new[] { "--AvMigContractVerify", "true", "--AvMigContractPolicy",
                         Path.Combine(root, "docs/migration/avalonia/p0-api-boundary.json"),
                         "--AvMigContractInventoryDirectory", directory })
                start.ArgumentList.Add(argument);
            var inherited = new Dictionary<string, string?>();
            foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT", "HOME" })
                inherited[key] = Environment.GetEnvironmentVariable(key);
            start.Environment.Clear();
            foreach (var (key, value) in inherited)
                if (value is not null) start.Environment[key] = value;
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
                ["ASPNETCORE_ENVIRONMENT"] = "Test", ["DOTNET_ENVIRONMENT"] = "Test",
                ["ConnectionStrings__DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=synthetic_inventory;Username=unused;Password=unused;Timeout=1",
                ["Tenancy__AppMode"] = "SaaS", ["Tenancy__SeedOnStartup"] = "false",
                ["UiShell__SeedOnStartup"] = "false", ["Security__EvaluationMode"] = "Disabled"
            }) start.Environment[key] = value;
            start.Environment["ASPNETCORE_CONTENTROOT"] = Path.Combine(root, "src/Coglatas.Web");
            var openApiPath = await GenerateOpenApiAsync(start, root, directory);
            start.ArgumentList.Add("--AvMigContractOpenApi");
            start.ArgumentList.Add(openApiPath);
            using var process = new Process();
            process.StartInfo = start;
            Assert.True(process.Start());
            // Drain without exposing inherited configuration, user paths or diagnostic bodies.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await Task.WhenAll(output, error);
            Assert.Equal(0, process.ExitCode);
            var path = Path.Combine(directory, "composed-host-inventory.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var report = document.RootElement;
            Assert.Equal("DRAFT", report.GetProperty("approval").GetString());
            Assert.Equal("UNVERIFIED", report.GetProperty("completion").GetString());
            Assert.Equal("Cookies", report.GetProperty("defaultAuthenticateScheme").GetString());
            Assert.Equal(JsonValueKind.Null, report.GetProperty("fallbackPolicy").ValueKind);
            Assert.Empty(report.GetProperty("duplicateSurfaceKeys").EnumerateArray());
            var endpoints = report.GetProperty("endpoints").EnumerateArray().ToArray();
            Assert.Equal(report.GetProperty("endpointCount").GetInt32(), endpoints.Length);
            Assert.True(endpoints.Length > 393);
            var login = Assert.Single(endpoints, row => row.GetProperty("normalizedPath").GetString() == "/api/auth/login" &&
                row.GetProperty("method").GetString() == "POST");
            // Login is implicit anonymous access through a null fallback, without AllowAnonymous metadata.
            Assert.False(login.GetProperty("allowAnonymous").GetBoolean());
            Assert.Equal("NONE", login.GetProperty("policySource").GetString());
            Assert.Equal(JsonValueKind.Null, login.GetProperty("effectivePolicy").ValueKind);
            Assert.False(login.GetProperty("authenticatedUserRequired").GetBoolean());
            var logout = Assert.Single(endpoints, row => row.GetProperty("normalizedPath").GetString() == "/api/auth/logout" &&
                row.GetProperty("method").GetString() == "POST");
            Assert.True(logout.GetProperty("authenticatedUserRequired").GetBoolean());
            var hub = Assert.Single(endpoints, row => row.GetProperty("normalizedPath").GetString() == "/hubs/app/negotiate");
            Assert.True(hub.GetProperty("authenticatedUserRequired").GetBoolean());
            var live = Assert.Single(endpoints, row => row.GetProperty("normalizedPath").GetString() == "/health/live");
            Assert.Equal("MINIMAL_OR_FALLBACK", live.GetProperty("kind").GetString());
            Assert.False(live.GetProperty("authenticatedUserRequired").GetBoolean());
            Assert.All(endpoints, row => Assert.Equal("UNVERIFIED", row.GetProperty("runtimeAuthorizationOutcome").GetString()));
            Assert.Equal(381, endpoints.Count(row => row.GetProperty("authorizationRequired").GetBoolean() &&
                row.GetProperty("kind").GetString() != "HUB"));
            Assert.Equal(28, endpoints.Count(row => row.GetProperty("authorizationRequired").GetBoolean() &&
                !row.GetProperty("authenticatedUserRequired").GetBoolean()));
            var openApi = report.GetProperty("openApi");
            Assert.Equal("METADATA_OBSERVED", openApi.GetProperty("outcome").GetString());
            Assert.Equal("SUPPLIED_OPENAPI_3_DOCUMENT", openApi.GetProperty("source").GetString());
            Assert.Empty(openApi.GetProperty("documentationOnly").EnumerateArray());
            var runtimeOnly = openApi.GetProperty("runtimeOnly").EnumerateArray().ToArray();
            Assert.Equal(5, runtimeOnly.Length);
            Assert.All(runtimeOnly, row =>
            {
                Assert.NotEqual("UNKNOWN_REQUIRES_REVIEW", row.GetProperty("observedPurpose").GetString());
                Assert.Equal("UNVERIFIED", row.GetProperty("normativeClassification").GetString());
                Assert.Equal("DRAFT", row.GetProperty("approval").GetString());
            });
            Assert.Equal(endpoints.Length, openApi.GetProperty("operationCount").GetInt32() + runtimeOnly.Length);
            var realtime = report.GetProperty("realtime");
            Assert.Equal(8, realtime.GetProperty("methods").GetArrayLength());
            Assert.Equal(5, realtime.GetProperty("subscriptionTypes").GetArrayLength());
            Assert.Equal("DurableEvent", Assert.Single(realtime.GetProperty("serverEvents").EnumerateArray()).GetString());
            Assert.All(realtime.GetProperty("methods").EnumerateArray(), row =>
            {
                Assert.NotEqual("UNKNOWN_REQUIRES_REVIEW", row.GetProperty("observedBoundary").GetString());
                Assert.Empty(row.GetProperty("specIds").EnumerateArray());
            });
            Assert.Equal(15, realtime.GetProperty("events").GetArrayLength());
            Assert.All(realtime.GetProperty("events").EnumerateArray(), row =>
            {
                Assert.NotEqual("UNKNOWN_REQUIRES_REVIEW", row.GetProperty("observedDeliveryBoundary").GetString());
                Assert.Equal("UNVERIFIED", row.GetProperty("runtimeOutcome").GetString());
            });
            var topology = report.GetProperty("serviceTopology");
            var workers = topology.GetProperty("hostedServices").EnumerateArray()
                .Select(row => row.GetProperty("type").GetString()).ToArray();
            Assert.Contains("Coglatas.Web.Realtime.OutboxDispatcher", workers);
            Assert.Contains("Coglatas.Web.Notifications.TaskDeadlineDigestWorker", workers);
            Assert.Contains("Coglatas.Web.Notifications.AnnouncementPublisherWorker", workers);
            Assert.Empty(topology.GetProperty("kafkaServiceTypes").EnumerateArray());
            var database = topology.GetProperty("database");
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", database.GetProperty("provider").GetString());
            Assert.Equal("Scoped", Assert.Single(database.GetProperty("lifetimes").EnumerateArray()).GetString());
            Assert.Equal("UNVERIFIED", database.GetProperty("applicationRoleOutcome").GetString());
            Assert.Equal("UNVERIFIED", database.GetProperty("workerRoleOutcome").GetString());
            var privateOutput = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
            if (savePrivateOutput && !string.IsNullOrWhiteSpace(privateOutput))
            {
                Directory.CreateDirectory(privateOutput);
                File.Copy(path, Path.Combine(privateOutput, "composed-host-inventory.json"));
            }
            return report.Clone();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<string> GenerateOpenApiAsync(ProcessStartInfo inspection, string root, string directory)
    {
        var webProject = Path.Combine(root, "src/Coglatas.Web/Coglatas.Web.csproj");
        var version = XDocument.Load(webProject).Descendants("PackageReference")
            .Single(element => (string?)element.Attribute("Include") == "Microsoft.Extensions.ApiDescription.Server")
            .Attribute("Version")!.Value;
        var assetsPath = Path.Combine(root, "src/Coglatas.Web/obj/project.assets.json");
        using var assets = JsonDocument.Parse(await File.ReadAllTextAsync(assetsPath));
        var tool = assets.RootElement.GetProperty("packageFolders").EnumerateObject()
            .Select(folder => Path.Combine(folder.Name, "microsoft.extensions.apidescription.server", version,
                "tools/dotnet-getdocument.dll")).Single(File.Exists);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Clear();
        foreach (var pair in inspection.Environment) start.Environment[pair.Key] = pair.Value;
        // The official prebuilt generator initializes minimal API exploration.
        // The inspection's pre-start provider alone omits those operations.
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var webAssembly = Path.Combine(root, "src/Coglatas.Web/bin", frameworkDirectory.Parent!.Name,
            frameworkDirectory.Name, "Coglatas.Web.dll");
        Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(typeof(AuthController).Assembly.Location)),
            SHA256.HashData(await File.ReadAllBytesAsync(webAssembly)));
        foreach (var argument in new[] { tool, "--assembly", webAssembly,
            "--file-list", Path.Combine(directory, "openapi-files.cache"), "--framework", ".NETCoreApp,Version=v10.0",
            "--output", directory, "--project", "Coglatas.Web", "--assets-file", assetsPath,
            "--platform", "AnyCPU", "--file-name", "composed-host-openapi", "--openapi-version", "OpenApi3_1" })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        await Task.WhenAll(output, error);
        Assert.Equal(0, process.ExitCode);
        var path = Path.Combine(directory, "composed-host-openapi.json");
        Assert.True(File.Exists(path));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs/migration/avalonia/p0-api-boundary.json")))
                return directory.FullName;
        throw new InvalidOperationException("Repository contract policy unavailable.");
    }
}
