using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Application.Auth;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Coglatas.SecurityArchitecture;
using Coglatas.Ui.Core.Interaction;
using Coglatas.Web.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Actual Web entry point with explicitly loaded test-owned selected-action context.</summary>
internal sealed class SecurityArchitectureRlsComposedHostFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-rls-composed-" + Guid.NewGuid().ToString("N"));
    private readonly Process _server;
    private readonly ServiceProvider _clients;
    private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, byte> _startupTypes = new();
    private bool _started;
    private string CaptureDirectory => Path.Combine(_directory, "captures");
    private Uri Address { get; set; } = null!;
    public string WebAssemblyDigest { get; }
    public string FileStorageRoot { get; }

    private SecurityArchitectureRlsComposedHostFixture(string database, bool taskRuntimeProbe, string? taskStorageRoot, bool fileProbe,
        string web, IReadOnlyDictionary<string, BoundProduct> products)
    {
        Directory.CreateDirectory(_directory);
        var application = PrepareOwnedApplication(web, products);
        FileStorageRoot = taskStorageRoot ?? Path.Combine(_directory, "files");
        WebAssemblyDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(application))).ToLowerInvariant();
        var services = new ServiceCollection();
        foreach (var name in new[] { "alpha", "beta", "anonymous", "restricted" })
            services.AddHttpClient(name, client => client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false });
        _clients = services.BuildServiceProvider();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--additional-deps");
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(application)!, "Coglatas.Tests.deps.json"));
        start.ArgumentList.Add(application);
        var inherited = new Dictionary<string, string?>();
        foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" }) inherited[key] = Environment.GetEnvironmentVariable(key);
        start.Environment.Clear();
        foreach (var (key, value) in inherited) if (value is not null) start.Environment[key] = value;
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_ENVIRONMENT"] = "Test", ["ASPNETCORE_ENVIRONMENT"] = "Test", ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
            ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = typeof(SecurityArchitectureRlsComposedHostStartup).Assembly.GetName().Name!,
            ["COGLATAS_SEC_ARCH_RLS_COMPOSED_PROBE"] = "true", ["COGLATAS_SEC_ARCH_RLS_COMPOSED_DIRECTORY"] = CaptureDirectory,
            ["COGLATAS_SEC_ARCH_RLS_FILE_COMPOSED_PROBE"] = fileProbe ? "true" : "false",
            ["COGLATAS_SEC_ARCH_RLS_TASK_COMPOSED_PROBE"] = taskRuntimeProbe ? "true" : "false",
            ["ConnectionStrings__DefaultConnection"] = database,
            ["Tenancy__AppMode"] = "SaaS", ["Tenancy__TenantResolutionStrategy"] = "HeaderForDevelopmentOnly",
            ["Tenancy__AllowDevelopmentHeaderTenantResolution"] = "true", ["Tenancy__DevelopmentTenantHeaderName"] = "X-Tenant-Slug",
            ["Tenancy__SeedOnStartup"] = "false", ["SecurityCiFixture__Enabled"] = "false",
            ["Security__RequireHttps"] = "false", ["Security__CookieSecurePolicy"] = "SameAsRequest",
            ["Security__EnableHsts"] = "false", ["Security__EnableRateLimiting"] = "false", ["Security__EnableCsrfProtection"] = "true",
            ["Security__EvaluationMode"] = "Disabled", ["FileStorage__RootPath"] = FileStorageRoot,
            ["FileStorage__AllowedExtensions__0"] = ".txt", ["FileStorage__AllowedContentTypes__0"] = "text/plain",
            ["DataProtection__KeysPath"] = Path.Combine(_directory, "keys"),
            ["Realtime__DispatcherPollSeconds"] = "600", ["TaskDeadlineDigest__PollSeconds"] = "600",
            ["AnnouncementPublisher__PollSeconds"] = "600", ["AuditPackageExportWorker__PollSeconds"] = "60",
            ["Logging__LogLevel__Default"] = "Warning", ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information"
        }) start.Environment[key] = value;
        _server = new Process { StartInfo = start, EnableRaisingEvents = true };
        _server.OutputDataReceived += (_, args) =>
        {
            const string marker = "Now listening on: ";
            var index = args.Data?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (index >= 0 && Uri.TryCreate(args.Data![(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) &&
                address.Scheme == "http" && address.Host == "127.0.0.1" && address.Port > 0) _listening.TrySetResult(address);
        };
        _server.ErrorDataReceived += (_, args) =>
        {
            foreach (Match match in Regex.Matches(args.Data ?? "", @"\b(?:System|Coglatas|Microsoft)\.[A-Za-z0-9_.]+(?:Exception|Service|Worker|Options)\b"))
                _startupTypes.TryAdd(match.Value, 0);
            foreach (Match match in Regex.Matches(args.Data ?? "", @"\b[A-Za-z0-9_.-]+\.dll\b"))
                _startupTypes.TryAdd(match.Value, 0);
            foreach (Match match in Regex.Matches(args.Data ?? "", @"\bat (Coglatas\.[A-Za-z0-9_.]+)\("))
                _startupTypes.TryAdd(match.Groups[1].Value, 0);
            if (args.Data?.Contains("An assembly specified in the application dependencies manifest", StringComparison.Ordinal) == true)
                _startupTypes.TryAdd("MissingDeclaredAssembly", 0);
        };
    }

    private sealed record BoundProduct(string ProducerPath, InventoryAssemblyBinding Binding);

    private static async Task<(string Web, Dictionary<string, BoundProduct> Products)> CaptureProductBindingsAsync()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? root = framework;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Coglatas.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("The composed-host probe requires the current source checkout.");
        var web = Path.Combine(root.FullName, "src/Coglatas.Web/bin", framework.Parent!.Name, framework.Name);
        var products = new Dictionary<string, BoundProduct>(StringComparer.Ordinal);
        foreach (var loaded in new[] { typeof(AuthController).Assembly, typeof(IUserSessionService).Assembly,
                     typeof(AppDbContext).Assembly, typeof(CapabilityGrant).Assembly, typeof(SpecRegistryValidator).Assembly,
                     typeof(ContextScope).Assembly })
        {
            var name = loaded.GetName().Name!;
            var project = name == "Coglatas.SecurityArchitecture" ? "tools" : "src";
            var producer = Path.Combine(root.FullName, project, name, "bin", framework.Parent!.Name, framework.Name, name + ".dll");
            Assert.Equal(Path.GetFullPath(Path.Combine(framework.FullName, name + ".dll")), Path.GetFullPath(loaded.Location));
            products.Add(name + ".dll", new BoundProduct(producer,
                await SecurityArchitectureInventoryAssemblyBinding.CaptureAsync(loaded, producer)));
        }
        return (web, products);
    }

    private string PrepareOwnedApplication(string web, IReadOnlyDictionary<string, BoundProduct> products)
    {
        var owned = Path.Combine(_directory, "application");
        Directory.CreateDirectory(owned);
        foreach (var source in new[] { web, AppContext.BaseDirectory })
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetExtension(file) is not ".dll" and not ".json") continue;
            if (Path.GetFileName(file).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
            var destination = Path.Combine(owned, Path.GetFileName(file));
            if (products.TryGetValue(Path.GetFileName(file), out var product))
            {
                var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
                Assert.Equal(source == web ? product.Binding.ProducerDigest : product.Binding.TestLoadedDigest, digest);
                if (!File.Exists(destination)) File.Copy(product.ProducerPath, destination, overwrite: false);
                Assert.Equal(product.Binding.ProducerDigest, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(destination))));
                continue;
            }
            Assert.False(Path.GetExtension(file) == ".dll" && Path.GetFileName(file).StartsWith("Coglatas.", StringComparison.Ordinal) &&
                Path.GetFileName(file) != "Coglatas.Tests.dll", "Unclassified product module cannot enter the owned canonical child.");
            if (File.Exists(destination)) Assert.Equal(SHA256.HashData(File.ReadAllBytes(file)), SHA256.HashData(File.ReadAllBytes(destination)));
            else File.Copy(file, destination, overwrite: false);
        }
        return Path.Combine(owned, "Coglatas.Web.dll");
    }

    public static async Task<SecurityArchitectureRlsComposedHostFixture> StartAsync(string database, bool taskRuntimeProbe = false, string? taskStorageRoot = null, bool fileProbe = false)
    {
        if (taskStorageRoot is not null && !taskRuntimeProbe)
            throw new InvalidOperationException("A supplied task storage root requires the explicit test-owned task prototype.");
        var (web, products) = await CaptureProductBindingsAsync();
        var fixture = new SecurityArchitectureRlsComposedHostFixture(database, taskRuntimeProbe, taskStorageRoot, fileProbe, web, products);
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY")))
                await SecurityArchitectureInventoryTests.WritePrivateInventoryAsync(
                    "composed-host-assembly-binding-" + Guid.NewGuid().ToString("N") + ".json", new
                    {
                        schemaVersion = 1, bindings = products.Values.Select(product => product.Binding).OrderBy(binding => binding.AssemblyName).ToArray(),
                        ownerApproval = (string?)null,
                        limits = new[] { "The owned child executes canonical product bytes; instrumented test-loaded copies remain distinct.",
                            "Collector original-byte equality does not attest the instrumented transformation or test execution.",
                            "This copy observation includes test-only UI/tool dependencies and does not replace six-assembly runtime reconciliation." }
                    });
            if (!fixture._server.Start()) throw new InvalidOperationException("Selected composed host did not start.");
            fixture._started = true;
            fixture._server.BeginOutputReadLine();
            fixture._server.BeginErrorReadLine();
            var readiness = fixture._listening.Task.WaitAsync(TimeSpan.FromSeconds(50));
            var exited = fixture._server.WaitForExitAsync();
            if (await Task.WhenAny(readiness, exited) == exited)
            {
                await exited;
                throw new InvalidOperationException("Selected composed-host process exited before readiness; exit=" +
                    fixture._server.ExitCode + "; types=" + string.Join(',', fixture._startupTypes.Keys.Order(StringComparer.Ordinal)));
            }
            fixture.Address = await readiness;
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<HttpClient> ClientAsync(string identity, string tenant)
    {
        var client = _clients.GetRequiredService<IHttpClientFactory>().CreateClient(identity);
        client.BaseAddress = Address;
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", tenant);
        await RefreshCsrfAsync(client);
        return client;
    }

    private static async Task RefreshCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/security/csrf-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var header = body.RootElement.GetProperty("headerName").GetString()!;
        client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add(header, body.RootElement.GetProperty("token").GetString()!);
    }

    public async Task<JsonElement> LoginAsync(HttpClient client, string email, string password)
    {
        // A rejected cookie changes the antiforgery identity; use the actual token endpoint again.
        await RefreshCsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await RefreshCsrfAsync(client);
        return body.RootElement.Clone();
    }

    public async Task<JsonElement> ReceiptAsync(Guid capture)
    {
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(CaptureDirectory, capture.ToString("N") + ".json")));
        return receipt.RootElement.Clone();
    }

    public bool HasCapture(Guid capture) => File.Exists(Path.Combine(CaptureDirectory, capture.ToString("N") + ".json"));

    public async Task<JsonElement> PreAuthReceiptAsync(Guid capture)
    {
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(CaptureDirectory, capture.ToString("N") + ".pre-auth.json")));
        return receipt.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (_started && !_server.HasExited)
        {
            _server.Kill(entireProcessTree: true);
            await _server.WaitForExitAsync();
        }
        _server.Dispose();
        await _clients.DisposeAsync();
        var resolved = Path.GetFullPath(_directory);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("coglatas-sec-arch-rls-composed-", StringComparison.Ordinal))
            throw new InvalidOperationException("Composed-host cleanup target is outside the owned temporary directory.");
        // Windows may briefly retain mapped-image handles after the owned process exits.
        // Keep the verified cleanup target and a bounded deadline; do not hide persistent failures.
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(resolved, recursive: true); break; }
            catch (Exception exception) when (attempt < 10 && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }
}
