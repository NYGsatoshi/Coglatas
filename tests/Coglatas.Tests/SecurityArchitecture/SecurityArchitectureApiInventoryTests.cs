using System.Diagnostics;
using System.Text.Json;
using Coglatas.Web.Controllers;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureApiInventoryTests
{
    [Fact]
    public async Task ActualComposedHostInventoryPreservesAnonymousAndProtectedMetadataWithoutAcceptance()
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
            Assert.Equal("UNVERIFIED", report.GetProperty("openApi").GetProperty("outcome").GetString());
            var privateOutput = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(privateOutput))
            {
                Directory.CreateDirectory(privateOutput);
                File.Copy(path, Path.Combine(privateOutput, "composed-host-inventory.json"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs/migration/avalonia/p0-api-boundary.json")))
                return directory.FullName;
        throw new InvalidOperationException("Repository contract policy unavailable.");
    }
}
