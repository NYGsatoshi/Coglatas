using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

public sealed class PerformanceFixtureRoundtripTests
{
    [Theory]
    [InlineData("localhost", "coglatas_fixture_guard")]
    [InlineData("127.0.0.1", "postgres")]
    [InlineData("fixture-guard.invalid", "coglatas_performance")]
    [InlineData("192.0.2.1", "coglatas_performance")]
    public async Task WrongFixtureTargetIsRejectedBeforeOpeningAConnection(string host, string database)
    {
        var connection = new NpgsqlConnectionStringBuilder { Host = host, Database = database };
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection.ConnectionString);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PerformanceCiFixtureSeed.SeedAsync(
            context, new Pbkdf2PasswordHasher(), "not-read.json", "small", Guid.NewGuid().ToString("N"), "not-written.json"));
        Assert.StartsWith("PERF-02 refuses database target", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task InMemoryCannotBeReportedAsAPostgreSqlPerformanceFixture()
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PerformanceCiFixtureSeed.SeedAsync(
            context, new Pbkdf2PasswordHasher(), "not-read.json", "small", Guid.NewGuid().ToString("N"), "not-written.json"));
        Assert.Equal("PERF-02 fixture requires real PostgreSQL.", error.Message);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlWrongDatabaseRetainsItsSentinelAndMigrationHistory()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var sentinel = new Tenant { Name = "Do not truncate", DisplayName = "Sentinel", Slug = Guid.NewGuid().ToString("N") };
                context.Tenants.Add(sentinel);
                await context.SaveChangesAsync();
                var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PerformanceCiFixtureSeed.SeedAsync(
                    context, new Pbkdf2PasswordHasher(), "not-read.json", "small", Guid.NewGuid().ToString("N"), "not-written.json"));
                Assert.StartsWith("PERF-02 refuses database target", error.Message);
                context.ChangeTracker.Clear();
                Assert.Equal("Do not truncate", (await context.Tenants.SingleAsync()).Name);
                Assert.Equal(sentinel.Id, (await context.Tenants.SingleAsync()).Id);
                Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlDedicatedFixtureRebuildsCountsIdentitiesCursorKeysAndEvidence()
    {
        var connection = PostgreSqlTestEnvironment.RequireConnectionString();
        var source = new NpgsqlConnectionStringBuilder(connection);
        Assert.Contains(source.Host, new[] { "localhost", "127.0.0.1", "::1", "postgres" });
        Assert.Equal("coglatas_performance", PerformanceCiFixtureSeed.DatabaseName);
        Assert.NotEqual(PerformanceCiFixtureSeed.DatabaseName, source.Database);
        var dedicated = new NpgsqlConnectionStringBuilder(connection) { Database = PerformanceCiFixtureSeed.DatabaseName }.ConnectionString;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        // CREATE fails if this name already exists. Never adopt or remove a pre-existing database.
        await using (var create = new NpgsqlCommand("CREATE DATABASE \"coglatas_performance\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(dedicated);
            await VerifyRebuildsAsync(dedicated);
        }
        finally
        {
            using (var pool = new NpgsqlConnection(dedicated)) NpgsqlConnection.ClearPool(pool);
            // Only reached after this invocation successfully created the dedicated database.
            // Do not terminate unknown connections if cleanup cannot complete.
            await using var drop = new NpgsqlCommand("DROP DATABASE \"coglatas_performance\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyRebuildsAsync(string connection)
    {
        var manifest = FindManifest();
        var manifestBytes = await File.ReadAllBytesAsync(manifest);
        using var document = JsonDocument.Parse(manifestBytes);
        var profile = document.RootElement.GetProperty("profiles").GetProperty("small");
        var expectedCounts = profile.GetProperty("counts");
        var expectedFocus = profile.GetProperty("focus");
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-performance-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var evidencePath = Path.Combine(directory, "fixture.json");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await using (var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
            {
                await PerformanceCiFixtureSeed.SeedAsync(context, new Pbkdf2PasswordHasher(), manifest, "small", password, evidencePath);
                await AssertCountsAsync(context, expectedCounts);
                Assert.Equal(0, await context.Attachments.IgnoreQueryFilters().CountAsync());
                context.Tenants.Add(new Tenant { Name = "Must be removed by rebuild", DisplayName = "Sentinel", Slug = Guid.NewGuid().ToString("N") });
                await context.SaveChangesAsync();
            }
            using var first = JsonDocument.Parse(await File.ReadAllBytesAsync(evidencePath));
            AssertEvidence(first.RootElement, PerformanceCiFixtureSeed.FixtureVersion, profile, manifestHash);
            Dictionary<string, string> identities;
            await using (var observer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
                identities = await ReadPersistedIdentitiesAsync(observer, first.RootElement);
            for (var repetition = 0; repetition < 2; repetition++)
            {
                var previousBytes = await File.ReadAllBytesAsync(evidencePath);
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                await PerformanceCiFixtureSeed.SeedAsync(context, new Pbkdf2PasswordHasher(), manifest, "small", password, evidencePath, dbScenarioFixture: true);
                context.ChangeTracker.Clear();
                await AssertCountsAsync(context, expectedCounts);
                Assert.Equal(expectedCounts.GetProperty("messages").GetInt32(),
                    await context.Messages.IgnoreQueryFilters().Select(message => message.CreatedAt).Distinct().CountAsync());
                Assert.False(await context.Tenants.IgnoreQueryFilters().AnyAsync(tenant => tenant.Name == "Must be removed by rebuild"));
                var actual = await File.ReadAllBytesAsync(evidencePath);
                using var evidence = JsonDocument.Parse(actual);
                AssertEvidence(evidence.RootElement, PerformanceCiFixtureSeed.DbFixtureVersion, profile, manifestHash);
                var persisted = await ReadPersistedIdentitiesAsync(context, evidence.RootElement);
                Assert.Equal(identities.OrderBy(item => item.Key), persisted.OrderBy(item => item.Key));
                var workspace = Guid.Parse(persisted["workspaceId"]);
                var attachments = await context.Attachments.IgnoreQueryFilters().AsNoTracking().ToListAsync();
                Assert.Equal(expectedFocus.GetProperty("workspaceFiles").GetInt32(), attachments.Count);
                foreach (var attachment in attachments)
                {
                    Assert.Equal(workspace, attachment.WorkspaceId);
                    Assert.Equal(workspace, attachment.OwnerId);
                    Assert.Equal(Coglatas.Domain.Enums.AttachmentOwnerType.Workspace, attachment.OwnerType);
                }
                var fileIds = attachments.Select(attachment => attachment.FileObjectId).ToArray();
                Assert.Equal(attachments.Count, await context.FileObjects.IgnoreQueryFilters().CountAsync(file => fileIds.Contains(file.Id)));
                if (repetition == 1) Assert.Equal(previousBytes, actual);
                var tenantRow = await context.Tenants.IgnoreQueryFilters().SingleAsync();
                Assert.Equal("perf-small", tenantRow.Slug);
                var operatorRow = await context.Users.SingleAsync(user => user.Email == "perf-small-operator@example.test");
                Assert.True(new Pbkdf2PasswordHasher().VerifyPassword(operatorRow.PasswordHash, password));
                Assert.Equal(expectedFocus.GetProperty("workspaceProjects").GetInt32(),
                    await context.Projects.IgnoreQueryFilters().CountAsync(project => project.WorkspaceId == workspace));
            }
            Assert.Equal(manifestBytes, await File.ReadAllBytesAsync(manifest));
        }
        finally
        {
            File.Delete(evidencePath);
            Directory.Delete(directory);
        }
    }

    private static async Task<Dictionary<string, string>> ReadPersistedIdentitiesAsync(AppDbContext context, JsonElement evidence)
    {
        var reported = JsonSerializer.Deserialize<Dictionary<string, string>>(evidence.GetProperty("identities").GetRawText())!;
        var tenant = await context.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Slug == "perf-small");
        var actor = await context.Users.AsNoTracking().SingleAsync(row => row.Email == "perf-small-operator@example.test");
        var workspace = await context.Workspaces.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Slug == "perf-small-workspace-000");
        // The tracked small profile shares project 0000 across the task, Gantt and Kanban scenarios.
        var project = await context.Projects.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Slug == "perf-small-project-0000");
        var conversation = await context.Conversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(row =>
            row.WorkspaceId == workspace.Id && row.Title == "PERF conversation 00000");
        var task = await context.TaskItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(row =>
            row.ProjectId == project.Id && row.Title == "PERF task 0000-000000");
        Assert.Equal(tenant.Id, workspace.TenantId);
        Assert.Equal(tenant.Id, task.TenantId);
        Assert.Equal(tenant.Id, conversation.TenantId);
        Assert.Equal(workspace.Id, conversation.WorkspaceId);
        Assert.Equal(project.Id, task.ProjectId);
        Assert.Equal(project.Id, conversation.ProjectId);
        Assert.Equal(workspace.Id, project.WorkspaceId);
        Assert.Equal(tenant.Id, project.TenantId);
        Assert.Equal(actor.Id, task.PrimaryAssigneeUserId);
        var focus = evidence.GetProperty("focus");
        var projectTasks = await context.TaskItems.IgnoreQueryFilters().CountAsync(row => row.ProjectId == project.Id);
        Assert.Equal(focus.GetProperty("projectTasks").GetInt32(), projectTasks);
        Assert.True(projectTasks >= focus.GetProperty("kanbanAuthorizedCards").GetInt32());
        Assert.Equal(focus.GetProperty("projectMilestones").GetInt32(),
            await context.Milestones.IgnoreQueryFilters().CountAsync(row => row.ProjectId == project.Id));
        Assert.Equal(focus.GetProperty("projectDependencies").GetInt32(),
            await context.TaskDependencies.IgnoreQueryFilters().CountAsync(row => row.ProjectId == project.Id));
        Assert.Equal(focus.GetProperty("conversationMessages").GetInt32(),
            await context.Messages.IgnoreQueryFilters().CountAsync(row => row.ConversationId == conversation.Id));
        Assert.Equal(focus.GetProperty("userMyTasks").GetInt32(),
            await context.TaskItems.IgnoreQueryFilters().CountAsync(row => row.PrimaryAssigneeUserId == actor.Id));
        var persisted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantSlug"] = tenant.Slug,
            ["operatorEmail"] = actor.Email,
            ["workspaceId"] = workspace.Id.ToString("D"),
            ["taskListProjectId"] = project.Id.ToString("D"),
            ["ganttProjectId"] = project.Id.ToString("D"),
            ["kanbanProjectId"] = project.Id.ToString("D"),
            ["conversationId"] = conversation.Id.ToString("D"),
            ["taskId"] = task.Id.ToString("D")
        };
        var normalized = reported.ToDictionary(item => item.Key, item => item.Key.EndsWith("Id", StringComparison.Ordinal)
            ? Guid.Parse(item.Value).ToString("D") : item.Value, StringComparer.Ordinal);
        Assert.Equal(normalized.OrderBy(item => item.Key), persisted.OrderBy(item => item.Key));
        return persisted;
    }

    private static async Task AssertCountsAsync(AppDbContext context, JsonElement expected)
    {
        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["tenants"] = await context.Tenants.IgnoreQueryFilters().CountAsync(),
            ["workspaces"] = await context.Workspaces.IgnoreQueryFilters().CountAsync(),
            ["projects"] = await context.Projects.IgnoreQueryFilters().CountAsync(),
            ["tasks"] = await context.TaskItems.IgnoreQueryFilters().CountAsync(),
            ["workItems"] = await context.TaskItems.IgnoreQueryFilters().CountAsync(),
            ["milestones"] = await context.Milestones.IgnoreQueryFilters().CountAsync(),
            ["dependencies"] = await context.TaskDependencies.IgnoreQueryFilters().CountAsync(),
            ["members"] = await context.WorkspaceMembers.IgnoreQueryFilters().CountAsync(),
            ["messages"] = await context.Messages.IgnoreQueryFilters().CountAsync(),
            ["notifications"] = await context.Notifications.IgnoreQueryFilters().CountAsync(),
            ["announcements"] = await context.Announcements.IgnoreQueryFilters().CountAsync(),
            ["files"] = await context.FileObjects.IgnoreQueryFilters().CountAsync()
        };
        Assert.Equal(expected.EnumerateObject().Count(), actual.Count);
        foreach (var item in expected.EnumerateObject()) Assert.Equal(item.Value.GetInt32(), actual[item.Name]);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private static void AssertEvidence(JsonElement evidence, int version, JsonElement profile, string manifestHash)
    {
        Assert.True(evidence.GetProperty("complete").GetBoolean());
        Assert.Equal("current", evidence.GetProperty("migrationStatus").GetString());
        Assert.Equal("small", evidence.GetProperty("profile").GetString());
        Assert.Equal(version, evidence.GetProperty("fixtureVersion").GetInt32());
        Assert.Equal(profile.GetProperty("seed").GetInt32(), evidence.GetProperty("seed").GetInt32());
        var canonical = $"fixtureVersion={version}\nmanifestSha256={manifestHash}\nprofile=small\nseed={profile.GetProperty("seed").GetInt32()}\n";
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant(), evidence.GetProperty("fixtureHash").GetString());
        foreach (var (source, actual) in new[] { ("counts", "cardinalities"), ("focus", "focus") })
        {
            var expectedValues = JsonSerializer.Deserialize<Dictionary<string, int>>(profile.GetProperty(source).GetRawText())!;
            var actualValues = JsonSerializer.Deserialize<Dictionary<string, int>>(evidence.GetProperty(actual).GetRawText())!;
            Assert.Equal(expectedValues.OrderBy(item => item.Key), actualValues.OrderBy(item => item.Key));
        }
    }

    private static string FindManifest([CallerFilePath] string sourcePath = "")
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(sourcePath)! })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var manifest = Path.Combine(directory.FullName, "performance", "datasets.json");
                if (File.Exists(manifest) && File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx"))) return manifest;
            }
        }
        throw new FileNotFoundException("Tracked performance dataset manifest was not found.");
    }
}
