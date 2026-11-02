using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class StartupSeedPostgreSqlTests
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(15) };

    [PostgreSqlFact]
    public Task DisabledOnPremSeedPreservesAnExistingTenantWithoutAddingPlans() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: true);

    [PostgreSqlFact]
    public Task DisabledOnPremSeedDoesNotBootstrapAnEmptyDatabase() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: false);

    [PostgreSqlFact]
    public Task ExplicitOnPremSeedStillBootstrapsTheConfiguredTenantAndPlans() =>
        VerifyStartupAsync(seedEnabled: true, existingTenant: false);

    [PostgreSqlFact]
    public Task DisabledOnPremSeedPreservesACustomizedPlanExactly() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: true, existingPlan: true);

    [PostgreSqlFact]
    public Task ExplicitDevelopmentOnPremLocalAdminStillBootstrapsWithTenantSeedDisabled() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: false, development: true, localAdmin: true);

    [PostgreSqlFact]
    public Task LocalAdminFlagDoesNotBootstrapOutsideDevelopment() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: false, localAdmin: true);

    [PostgreSqlFact]
    public Task LocalAdminFlagDoesNotExpandDevelopmentSaaSBootstrap() =>
        VerifyStartupAsync(seedEnabled: false, existingTenant: false, development: true, localAdmin: true, mode: AppMode.SaaS);

    private static Task VerifyStartupAsync(bool seedEnabled, bool existingTenant, bool existingPlan = false,
        bool development = false, bool localAdmin = false, AppMode mode = AppMode.OnPremSingleTenant) =>
        PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                var tenantId = Guid.NewGuid();
                string? originalPlan = null;
                var seedRequested = seedEnabled || (development && localAdmin && mode == AppMode.OnPremSingleTenant);
                const string slug = "synthetic-recovery-startup";
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    // Historical migrations insert a default Tenant. Build an explicitly
                    // empty, owned fixture before testing restored/missing Tenant states.
                    var migrationTenant = Assert.Single(await db.Tenants.ToListAsync());
                    Assert.Equal("default", migrationTenant.Slug);
                    db.Tenants.Remove(migrationTenant);
                    await db.SaveChangesAsync();
                    if (existingTenant)
                    {
                        db.Tenants.Add(new Tenant(tenantId)
                        {
                            Slug = slug, Name = "Synthetic preserved tenant", DisplayName = "Synthetic preserved tenant",
                            Status = TenantStatus.Active
                        });
                        await db.SaveChangesAsync();
                    }
                    Assert.Empty(await db.Plans.ToListAsync());
                    if (existingPlan)
                    {
                        var plan = new Plan
                        {
                            Name = "SyntheticRestoredPlan", Description = "Synthetic preserved settings",
                            MaxUsers = 17, MaxStorageBytes = 8192, MaxProjects = 3,
                            EnabledFeaturesJson = "[\"FileSharing\"]", Status = PlanStatus.InternalOnly
                        };
                        db.Plans.Add(plan);
                        await db.SaveChangesAsync();
                        originalPlan = JsonSerializer.Serialize(await db.Plans.AsNoTracking().SingleAsync());
                    }
                }

                await using (var host = await StartupHost.StartAsync(database, slug, seedEnabled, development, localAdmin, mode))
                {
                    using var live = await Client.GetAsync(new Uri(host.Address, "/health/live"));
                    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                    using var ready = await Client.GetAsync(new Uri(host.Address, "/health/ready"));
                    Assert.Equal(seedRequested || existingTenant || mode == AppMode.SaaS ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
                        ready.StatusCode);
                }

                await using var restored = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var tenants = await restored.Tenants.AsNoTracking().ToListAsync();
                var plans = await restored.Plans.AsNoTracking().OrderBy(plan => plan.Name).Select(plan => plan.Name).ToListAsync();
                if (seedRequested)
                {
                    Assert.Equal(slug, Assert.Single(tenants).Slug);
                    Assert.Equal(["Enterprise", "InternalPilot", "SchoolPilot", "Standard"], plans);
                }
                else
                {
                    if (existingPlan)
                    {
                        Assert.Equal(["SyntheticRestoredPlan"], plans);
                        Assert.Equal(originalPlan, JsonSerializer.Serialize(Assert.Single(await restored.Plans.AsNoTracking().ToListAsync())));
                    }
                    else Assert.Empty(plans);
                    if (existingTenant)
                    {
                        var tenant = Assert.Single(tenants);
                        Assert.Equal(tenantId, tenant.Id);
                        Assert.Equal(slug, tenant.Slug);
                        Assert.Equal("Synthetic preserved tenant", tenant.Name);
                        Assert.Equal("Synthetic preserved tenant", tenant.DisplayName);
                        Assert.Equal(TenantStatus.Active, tenant.Status);
                    }
                    else Assert.Empty(tenants);
                }
                if (development && localAdmin && mode == AppMode.OnPremSingleTenant)
                {
                    var user = Assert.Single(await restored.Users.AsNoTracking().ToListAsync());
                    Assert.Equal("synthetic-startup-admin@example.test", user.Email);
                    Assert.Equal(SystemRole.PlatformAdmin, user.SystemRole);
                    var membership = Assert.Single(await restored.TenantUsers.AsNoTracking().ToListAsync());
                    Assert.Equal(user.Id, membership.UserId);
                    Assert.Equal(Assert.Single(tenants).Id, membership.TenantId);
                    Assert.Equal(TenantUserRole.Owner, membership.Role);
                    Assert.Equal(TenantUserStatus.Active, membership.Status);
                }
                else
                {
                    Assert.Empty(await restored.Users.ToListAsync());
                    Assert.Empty(await restored.TenantUsers.ToListAsync());
                }
            });

    // Exercise Program's real startup branch, rather than a reimplemented seed predicate.
    private sealed class StartupHost : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "coglatas-startup-seed-" + Guid.NewGuid().ToString("N"));
        private readonly Process _server;
        private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<string, byte> _startupTypes = new();
        private bool _started;
        public Uri Address { get; private set; } = null!;

        private StartupHost(string database, string slug, bool seedEnabled, bool development, bool localAdmin, AppMode mode)
        {
            Directory.CreateDirectory(_directory);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(AuthController).Assembly.Location);
            var inherited = new Dictionary<string, string?>();
            foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" })
                inherited[key] = Environment.GetEnvironmentVariable(key);
            start.Environment.Clear();
            foreach (var (key, value) in inherited) if (value is not null) start.Environment[key] = value;
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
                ["ASPNETCORE_ENVIRONMENT"] = development ? "Development" : "Test",
                ["DOTNET_ENVIRONMENT"] = development ? "Development" : "Test",
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:0", ["ConnectionStrings__DefaultConnection"] = database,
                ["Tenancy__AppMode"] = mode.ToString(), ["Tenancy__DefaultTenantSlug"] = slug,
                ["Tenancy__TenantResolutionStrategy"] = "ConfigDefault", ["Tenancy__AllowTenantSwitching"] = "false",
                ["Tenancy__SeedOnStartup"] = seedEnabled ? "true" : "false", ["UiShell__SeedOnStartup"] = "false",
                ["SecurityCiFixture__Enabled"] = "false", ["COGLATAS_SEED_ADMIN_ENABLED"] = "false",
                ["COGLATAS_BROWSER_SMOKE_SEED_ENABLED"] = "false", ["COGLATAS_DEMO_DATASET_ENABLED"] = "false",
                ["LocalAdmin__SeedOnStartup"] = localAdmin ? "true" : "false", ["Security__EvaluationMode"] = "Disabled",
                ["Security__RequireHttps"] = "false", ["Security__EnableHsts"] = "false",
                ["Security__CookieSecurePolicy"] = "SameAsRequest", ["Security__EnableCsrfProtection"] = "true",
                ["FileStorage__RootPath"] = Path.Combine(_directory, "files"),
                ["FileStorage__AllowedExtensions__0"] = ".txt", ["FileStorage__AllowedContentTypes__0"] = "text/plain",
                ["DataProtection__KeysPath"] = Path.Combine(_directory, "keys"),
                ["Logging__LogLevel__Default"] = "Warning", ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information"
            }) start.Environment[key] = value;
            if (localAdmin)
            {
                start.Environment["LocalAdmin__Email"] = "synthetic-startup-admin@example.test";
                start.Environment["LocalAdmin__Password"] = Guid.NewGuid().ToString("N");
                start.Environment["LocalAdmin__DisplayName"] = "Synthetic Startup Admin";
            }
            _server = new Process { StartInfo = start, EnableRaisingEvents = true };
            _server.OutputDataReceived += (_, args) =>
            {
                const string marker = "Now listening on: ";
                var index = args.Data?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
                if (index >= 0 && Uri.TryCreate(args.Data![(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) &&
                    address.Scheme == "http" && address.Host == "127.0.0.1" && address.Port > 0)
                    _listening.TrySetResult(address);
            };
            _server.ErrorDataReceived += (_, args) =>
            {
                foreach (Match match in Regex.Matches(args.Data ?? "", @"\b(?:System|Coglatas|Microsoft)\.[A-Za-z0-9_.]+(?:Exception|Service|Worker|Options)\b"))
                    _startupTypes.TryAdd(match.Value, 0);
                if (args.Data?.Contains("An assembly specified in the application dependencies manifest", StringComparison.Ordinal) == true)
                    _startupTypes.TryAdd("MissingDeclaredAssembly", 0);
            };
            _server.Exited += (_, _) => _listening.TrySetException(new InvalidOperationException(
                "Synthetic startup process exited; exit=" + _server.ExitCode + "; types=" +
                string.Join(',', _startupTypes.Keys.Order(StringComparer.Ordinal))));
        }

        public static async Task<StartupHost> StartAsync(string database, string slug, bool seedEnabled,
            bool development, bool localAdmin, AppMode mode)
        {
            var host = new StartupHost(database, slug, seedEnabled, development, localAdmin, mode);
            try
            {
                host._started = host._server.Start();
                if (!host._started) throw new InvalidOperationException("Synthetic startup process did not start.");
                host._server.BeginOutputReadLine();
                host._server.BeginErrorReadLine();
                host.Address = await host._listening.Task.WaitAsync(TimeSpan.FromSeconds(50));
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_started && !_server.HasExited) _server.Kill(entireProcessTree: true);
            if (_started) await _server.WaitForExitAsync();
            _server.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
