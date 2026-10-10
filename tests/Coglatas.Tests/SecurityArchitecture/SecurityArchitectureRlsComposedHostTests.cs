using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Coglatas.Tests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsComposedHostTests
{
    [Fact]
    public void SelectedComposedHostProbeRequiresExplicitTestEnvironmentAndHasNoDisabledRegistration()
    {
        foreach (var environment in new[] { "Production", "Development" })
        {
            var builder = new HostBuilder().UseEnvironment(environment)
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    { ["COGLATAS_SEC_ARCH_RLS_COMPOSED_PROBE"] = "true" }))
                .ConfigureWebHost(web =>
                {
                    web.UseEnvironment(environment).UseKestrel().Configure(_ => { });
                    new SecurityArchitectureRlsComposedHostStartup().Configure(web);
                });
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }
        var disabled = new HostBuilder().UseEnvironment("Production").ConfigureWebHost(web =>
        {
            web.UseEnvironment("Production").UseKestrel().Configure(_ => { });
            new SecurityArchitectureRlsComposedHostStartup().Configure(web);
        });
        using var host = disabled.Build();
        Assert.Null(host.Services.GetService<ComposedRlsProbe>());
        Assert.Null(host.Services.GetService<ComposedRlsActionFilter>());
        Assert.Null(host.Services.GetService<ComposedRlsTransactionInterceptor>());
        Assert.Null(host.Services.GetService<ComposedRlsSaveInterceptor>());
    }

    [PostgreSqlFact]
    public Task ActualWebPasswordLoginAndSelectedWorkspaceReadDistinguishPostAuthRlsFromPreAuthCompatibility() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, betaWorkspace) =>
        {
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            var alphaLogin = await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            var betaLogin = await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            Assert.True(alphaLogin.GetProperty("workspaces").GetArrayLength() > 0);
            Assert.True(betaLogin.GetProperty("workspaces").GetArrayLength() > 0);
            await InstallWorkspacePolicyAsync(database, role);
            var observations = new List<JsonElement>();
            observations.Add(await WorkspaceAsync(beta, betaWorkspace, betaTenant, HttpStatusCode.OK));
            observations.Add(await WorkspaceAsync(alpha, alphaWorkspace, alphaTenant, HttpStatusCode.OK));
            Assert.Single(observations.Select(observation => observation.GetProperty("backendPid").GetInt32()).Distinct());
            observations.Add(await WorkspaceAsync(alpha, betaWorkspace, alphaTenant, HttpStatusCode.NotFound));
            using (var noContext = await alpha.GetAsync("/api/workspaces/" + alphaWorkspace)) Assert.Equal(HttpStatusCode.NotFound, noContext.StatusCode);
            var afterPolicyLogin = await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            Assert.Equal(0, afterPolicyLogin.GetProperty("workspaces").GetArrayLength());
            observations.Add(await WorkspaceAsync(alpha, alphaWorkspace, alphaTenant, HttpStatusCode.OK));
            var deniedCapture = Guid.NewGuid();
            alpha.DefaultRequestHeaders.Remove("X-Tenant-Slug");
            alpha.DefaultRequestHeaders.Add("X-Tenant-Slug", SecurityCiFixtureSeed.TenantBSlug);
            using (var request = Request(HttpMethod.Get, "/api/workspaces/" + betaWorkspace, deniedCapture))
            using (var denied = await alpha.SendAsync(request)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.False(host.HasCapture(deniedCapture));
            await WritePrivateAsync("draft-rls-composed-web-auth-workspace.json", database, role, new
            {
                observations, webAssemblyDigest = host.WebAssemblyDigest, prePolicyLoginWorkspaceSummaryPositive = true, postPolicyLoginWorkspaceSummaryCount = 0,
                missingPostAuthTransactionStatus = 404, currentForeignMembershipDenialStatus = 401,
                preAuthWorkspaceDiscoveryCompatibility = "UNVERIFIED", productStartupFullScope = "UNVERIFIED"
            });

            async Task<JsonElement> WorkspaceAsync(HttpClient client, Guid workspace, Guid tenant, HttpStatusCode status)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Get, "/api/workspaces/" + workspace, capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                var observed = await host.ReceiptAsync(capture);
                var subject = tenant == alphaTenant ? SecurityCiFixtureSeed.TenantAMemberUserId : SecurityCiFixtureSeed.TenantBOwnerUserId;
                AssertReceipt(observed, role, tenant, subject, committed: true);
                await AssertSessionIdentityAsync(database, observed, subject);
                return observed;
            }
        });

    [PostgreSqlFact]
    public Task ActualWebRawPreferenceMutationRollsBackOnExceptionAndRevokedSessionNeverStartsScopedAction() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, _) =>
        {
            Assert.NotEqual(alphaTenant, betaTenant);
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            await InstallWorkspacePolicyAsync(database, role);
            var observations = new List<JsonElement>();
            observations.Add(await PreferenceAsync(beta, betaTenant, false, injectException: false, HttpStatusCode.OK));
            observations.Add(await PreferenceAsync(alpha, alphaTenant, false, injectException: false, HttpStatusCode.OK));
            Assert.False(await EnabledAsync(SecurityCiFixtureSeed.TenantAMemberUserId, alphaTenant));
            observations.Add(await PreferenceAsync(alpha, alphaTenant, true, injectException: true, HttpStatusCode.InternalServerError));
            Assert.False(await EnabledAsync(SecurityCiFixtureSeed.TenantAMemberUserId, alphaTenant));
            Assert.False(await EnabledAsync(SecurityCiFixtureSeed.TenantBOwnerUserId, betaTenant));
            observations.Add(await PreferenceAsync(alpha, alphaTenant, true, injectException: false, HttpStatusCode.OK));
            Assert.True(await EnabledAsync(SecurityCiFixtureSeed.TenantAMemberUserId, alphaTenant));
            Assert.False(await EnabledAsync(SecurityCiFixtureSeed.TenantBOwnerUserId, betaTenant));
            var session = observations[1].GetProperty("sessionId").GetGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "UPDATE sessions SET \"RevokedAt\"=now() WHERE \"Id\"=@session", ("session", session));
            var deniedCapture = Guid.NewGuid();
            using (var request = Request(HttpMethod.Get, "/api/workspaces/" + alphaWorkspace, deniedCapture))
            using (var denied = await alpha.SendAsync(request)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.False(host.HasCapture(deniedCapture));
            await WritePrivateAsync("draft-rls-composed-web-raw-rollback.json", database, role, new
            {
                observations, webAssemblyDigest = host.WebAssemblyDigest, actualRawPreferenceAdapter = "MessageNotificationPreferenceStore",
                rawSqlRollbackPreservedValue = true, foreignPreferencePreserved = true, currentSessionRevocationStatus = 401,
                revokedSessionScopedActionCount = 0, preferenceRlsAuthority = "UNVERIFIED", authenticationRoleAuthority = "UNVERIFIED"
            });

            Task<bool> EnabledAsync(Guid user, Guid tenant) => PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                "SELECT \"MessageNotificationsEnabled\" FROM tenant_users WHERE \"UserId\"=@user AND \"TenantId\"=@tenant", ("user", user), ("tenant", tenant));
            async Task<JsonElement> PreferenceAsync(HttpClient client, Guid tenant, bool enabled, bool injectException, HttpStatusCode status)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Patch, "/api/me/message-notification-preferences", capture);
                request.Content = JsonContent.Create(new { messageNotificationsEnabled = enabled });
                if (injectException) request.Headers.Add("X-Sec-Arch-Composed-Exception", "true");
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                var observed = await host.ReceiptAsync(capture);
                var subject = tenant == alphaTenant ? SecurityCiFixtureSeed.TenantAMemberUserId : SecurityCiFixtureSeed.TenantBOwnerUserId;
                AssertReceipt(observed, role, tenant, subject, committed: !injectException);
                await AssertSessionIdentityAsync(database, observed, subject);
                return observed;
            }
        });

    [PostgreSqlFact]
    public Task CurrentCookieMembershipReadFailsBeforePostAuthContextWhenDraftMembershipRlsIsInstalled() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, _, alphaWorkspace, _) =>
        {
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await InstallWorkspacePolicyAsync(database, role);
            var positiveCapture = Guid.NewGuid();
            using (var positive = await alpha.SendAsync(Request(HttpMethod.Get, "/api/workspaces/" + alphaWorkspace, positiveCapture)))
                Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
            var positiveReceipt = await host.ReceiptAsync(positiveCapture);
            AssertReceipt(positiveReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true);
            await AssertSessionIdentityAsync(database, positiveReceipt, SecurityCiFixtureSeed.TenantAMemberUserId);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                ALTER TABLE tenant_users ENABLE ROW LEVEL SECURITY; ALTER TABLE tenant_users FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_draft_composed_membership ON tenant_users TO "{role}"
                    USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                    WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                """);
            var deniedCapture = Guid.NewGuid();
            using (var denied = await alpha.SendAsync(Request(HttpMethod.Get, "/api/workspaces/" + alphaWorkspace, deniedCapture)))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.False(host.HasCapture(deniedCapture));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "DROP POLICY sec_arch_draft_composed_membership ON tenant_users; ALTER TABLE tenant_users DISABLE ROW LEVEL SECURITY; ALTER TABLE tenant_users NO FORCE ROW LEVEL SECURITY");
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            var restoredCapture = Guid.NewGuid();
            using (var restored = await alpha.SendAsync(Request(HttpMethod.Get, "/api/workspaces/" + alphaWorkspace, restoredCapture)))
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            var restoredReceipt = await host.ReceiptAsync(restoredCapture);
            AssertReceipt(restoredReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true);
            await AssertSessionIdentityAsync(database, restoredReceipt, SecurityCiFixtureSeed.TenantAMemberUserId);
            await WritePrivateAsync("draft-rls-composed-web-membership-boundary.json", database, role, new
            {
                positiveCapture, deniedCapture, restoredCapture, webAssemblyDigest = host.WebAssemblyDigest, preAuthMembershipPolicyDenialStatus = 401,
                deniedScopedActionCount = 0, restoredActualHostPositive = true,
                authorityDisposition = "CONCRETE_NARROW_AUTHENTICATION_MEMBERSHIP_POLICY_REQUIRES_OWNER_REVIEW",
                fullTableStartupQualification = "UNVERIFIED"
            });
        });

    private static HttpRequestMessage Request(HttpMethod method, string path, Guid capture)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Sec-Arch-Composed-Capture", capture.ToString("N"));
        return request;
    }

    private static void AssertReceipt(JsonElement receipt, string role, Guid tenant, Guid subject, bool committed,
        int efSaveCount = 0, int efSaveFailureCount = 0, string? nativeSqlState = null)
    {
        Assert.Equal(1, receipt.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("DRAFT", receipt.GetProperty("approval").GetString());
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("ownerApproval").ValueKind);
        Assert.Equal("ACTUAL_WEB_ENTRY_POINT_WITH_TEST_OWNED_SELECTED_ACTION_CONTEXT", receipt.GetProperty("executionScope").GetString());
        Assert.Equal(tenant, receipt.GetProperty("tenantId").GetGuid());
        Assert.Equal(subject, receipt.GetProperty("subjectId").GetGuid());
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("sessionId").GetGuid());
        Assert.Equal(1, receipt.GetProperty("boundTransactionCount").GetInt32());
        Assert.Equal(efSaveCount, receipt.GetProperty("efSaveCount").GetInt32());
        Assert.Equal(efSaveFailureCount, receipt.GetProperty("efSaveFailureCount").GetInt32());
        Assert.Equal(nativeSqlState, receipt.GetProperty("nativeSqlState").GetString());
        if (nativeSqlState is null)
        {
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("nativeTable").ValueKind);
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("nativeRoutine").ValueKind);
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("denialMechanism").ValueKind);
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("rlsRejectedTable").ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("nativeTable").ValueKind);
            Assert.Equal("ExecWithCheckOptions", receipt.GetProperty("nativeRoutine").GetString());
            Assert.Equal("RLS_POLICY", receipt.GetProperty("denialMechanism").GetString());
            Assert.Equal("audit_logs", receipt.GetProperty("rlsRejectedTable").GetString());
        }
        Assert.Equal(role, receipt.GetProperty("databaseRole").GetString());
        Assert.True(receipt.GetProperty("backendPid").GetInt32() > 0);
        Assert.Equal(committed, receipt.GetProperty("committed").GetBoolean());
        Assert.Equal(!committed, receipt.GetProperty("rolledBack").GetBoolean());
        Assert.Equal("UNVERIFIED", receipt.GetProperty("operationalRoleEquivalence").GetString());
        Assert.Equal("PRE-AVALONIA SEC-ARCH: BLOCKED", receipt.GetProperty("preAvaloniaVerdict").GetString());
    }

    private static async Task AssertSessionIdentityAsync(string database, JsonElement receipt, Guid subject) =>
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT EXISTS (SELECT 1 FROM sessions WHERE "Id"=@session AND "UserId"=@subject
                AND "RevokedAt" IS NULL AND "ExpiresAt">now())
            """, ("session", receipt.GetProperty("sessionId").GetGuid()), ("subject", subject)));

    private static Task InstallWorkspacePolicyAsync(string database, string role) => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
        ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY; ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
        CREATE POLICY sec_arch_draft_composed_workspace ON workspaces TO "{role}"
            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
        """);

    private static async Task WithHostAsync(Func<string, SecurityArchitectureRlsComposedHostFixture, string, string, Guid, Guid, Guid, Guid, Task> scenario,
        bool workspaceMutation = false, bool taskRuntimeProbe = false)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_composed_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var storage = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-composed-seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                    await SecurityCiFixtureSeed.SeedAsync(seed, new Pbkdf2PasswordHasher(),
                        new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = storage })), password);
                var alpha = await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"Id\" FROM tenants WHERE \"Slug\"=@slug", ("slug", SecurityCiFixtureSeed.TenantASlug));
                var beta = await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"Id\" FROM tenants WHERE \"Slug\"=@slug", ("slug", SecurityCiFixtureSeed.TenantBSlug));
                var alphaWorkspace = await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"Id\" FROM workspaces WHERE \"Slug\"=@slug", ("slug", SecurityCiFixtureSeed.TenantAWorkspaceSlug));
                var betaWorkspace = await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"Id\" FROM workspaces WHERE \"Slug\"=@slug", ("slug", SecurityCiFixtureSeed.TenantBWorkspaceSlug));
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT ON users,sessions,tenants,tenant_users,workspaces,workspace_members TO "{role}";
                    GRANT UPDATE ON users,sessions TO "{role}";
                    GRANT INSERT ON sessions,audit_logs,security_events TO "{role}";
                    GRANT UPDATE ("MessageNotificationsEnabled","UpdatedAt") ON tenant_users TO "{role}";
                    """);
                if (workspaceMutation)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                        $"GRANT UPDATE (\"Description\",\"UpdatedAt\") ON workspaces TO \"{role}\"");
                await AssertRoleAsync(database, role);
                var scoped = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString;
                await using var host = await SecurityArchitectureRlsComposedHostFixture.StartAsync(scoped, taskRuntimeProbe, taskRuntimeProbe ? storage : null);
                await scenario(database, host, role, password, alpha, beta, alphaWorkspace, betaWorkspace);
            });
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\"");
            var resolved = Path.GetFullPath(storage);
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(resolved), ignoreCase: true);
            Assert.StartsWith("coglatas-sec-arch-composed-seed-", Path.GetFileName(resolved));
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task AssertRoleAsync(string database, string role)
    {
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE rolname=@role
            """, ("role", role)));
    }

    private static async Task WritePrivateAsync(string name, string database, string role, object observations)
    {
        await AssertRoleAsync(database, role);
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsComposedHostTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is null || candidate.Length != 40 || candidate.Any(character => !Uri.IsHexDigit(character))) candidate = null;
        await using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, candidateSha = candidate, testAssemblyDigest = assemblyDigest, approval = "DRAFT", ownerApproval = (string?)null,
            executionScope = "ACTUAL_WEB_ENTRY_POINT_WITH_TEST_OWNED_SELECTED_ACTION_CONTEXT", databaseRole = role,
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SHOW server_version"), observations,
            productRlsAppliedCount = 0, operationalRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED",
            limits = new[] { "Selected Workspace/preference actions own a test transaction; the separate Task probe preserves adapter-owned transactions.",
                "The synthetic combined role does not approve authentication/root/worker identity policies.",
                "Tenant discovery, bootstrap and global worker discovery remain separate owner-held designs.",
                "Preference raw SQL runs inside the selected transaction; its identity membership table is not normatively RLS qualified.",
                "A role executing arbitrary SQL can choose mutable tenant context; this is not independent authentication.",
                "The login Workspace summary and cookie-membership probes explicitly retain pre-authentication compatibility gaps." }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
