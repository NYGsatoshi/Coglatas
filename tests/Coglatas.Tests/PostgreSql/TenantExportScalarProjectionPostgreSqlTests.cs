using System.IO.Compression;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Security.Redaction;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class TenantExportScalarProjectionPostgreSqlTests
{
    private static readonly string[] ArchiveEntries =
    [
        "manifest.json", "tenant.json", "users.json", "workspaces.json", "groups.json", "projects.json",
        "tasks.json", "events.json", "attendance.json", "comments.json", "artifacts.json", "file_objects.json",
        "notifications.json", "audit_logs.json", "tenant_settings.json", "usage_records.json"
    ];

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NativeTrackedGraphsExportOnlyCompleteScopedScalarsThroughCanonicalRedaction() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var tenantContext = new CurrentTenantService();
                tenantContext.SetPlatformScope();
                await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenantContext);
                var actor = new User { DisplayName = "Synthetic shared actor", Email = Guid.NewGuid().ToString("N") + "@example.invalid",
                    PasswordHash = "synthetic-private-export-canary", Status = UserStatus.Active };
                db.Users.Add(actor);
                var own = Seed(db, actor, "owned");
                var foreign = Seed(db, actor, "foreign-export-boundary-canary");
                await db.SaveChangesAsync();
                Assert.Same(own.Settings, own.Tenant.Settings);
                Assert.Same(own.Tenant, own.Settings.Tenant);
                Assert.Same(own.Event, own.Attendance.Event);
                Assert.Contains(own.Attendance, own.Event.Attendances);
                Assert.Same(actor, own.Notification.User);
                Assert.Same(actor, foreign.Notification.User);
                Assert.Same(own.Tenant, own.Usage.Tenant);

                var observer = new ScalarProjectionObserver(db, own.Tenant.Id, own.Rows);
                var repository = new TenantExportRepository(db, observer);
                var bytes = await repository.CreateMetadataZipAsync(own.Tenant.Id,
                    new AuthorizationContext(actor.Id, own.Tenant.Id, "TenantExport", "ExportBuild", "native-scalar-export",
                        RedactionAuthorizationState.Allowed, FieldAccessPolicySnapshot.ThroughConfidential));
                Assert.Equal(own.Rows.Keys.Order(), observer.Observed.Order());
                // Projection must leave mutable tracked graphs intact.
                Assert.Same(own.Settings, own.Tenant.Settings);
                Assert.Same(own.Event, own.Attendance.Event);

                using var stream = new MemoryStream(bytes);
                await using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                Assert.Equal(ArchiveEntries.Order(), archive.Entries.Select(entry => entry.FullName).Order());
                foreach (var entry in archive.Entries)
                {
                    await using var content = await entry.OpenAsync();
                    using var json = await JsonDocument.ParseAsync(content);
                    var serialized = json.RootElement.GetRawText();
                    Assert.DoesNotContain("foreign-export-boundary-canary", serialized, StringComparison.Ordinal);
                    Assert.DoesNotContain("synthetic-private-export-canary", serialized, StringComparison.Ordinal);
                    if (entry.FullName == "manifest.json") continue;
                    Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
                    foreach (var row in json.RootElement.EnumerateArray())
                        if (row.TryGetProperty("tenantId", out var id)) Assert.Equal(own.Tenant.Id, id.GetGuid());
                    if (entry.FullName is "events.json" or "attendance.json" or "comments.json" or "artifacts.json" or
                        "notifications.json" or "audit_logs.json" or "tenant_settings.json" or "usage_records.json")
                    {
                        var row = Assert.Single(json.RootElement.EnumerateArray());
                        Assert.False(row.TryGetProperty("tenant", out _));
                        Assert.False(row.TryGetProperty("user", out _));
                        Assert.False(row.TryGetProperty("actorUser", out _));
                        Assert.False(row.TryGetProperty("workspace", out _));
                        Assert.False(row.TryGetProperty("project", out _));
                        Assert.False(row.TryGetProperty("event", out _));
                    }
                    if (entry.FullName == "comments.json")
                    {
                        var row = Assert.Single(json.RootElement.EnumerateArray());
                        Assert.True(row.GetProperty("isDeleted").GetBoolean());
                        Assert.Equal(actor.Id, row.GetProperty("deletedByUserId").GetGuid());
                        Assert.Equal("Synthetic confidential comment", row.GetProperty("body").GetString());
                        Assert.Equal("[redacted:restricted]", row.GetProperty("deleteReason").GetString());
                    }
                    if (entry.FullName == "notifications.json")
                    {
                        var row = Assert.Single(json.RootElement.EnumerateArray());
                        Assert.Equal(7, row.GetProperty("stateVersion").GetInt64());
                        Assert.Equal("Synthetic confidential notification", row.GetProperty("body").GetString());
                    }
                    if (entry.FullName == "usage_records.json")
                        Assert.Equal(23, Assert.Single(json.RootElement.EnumerateArray()).GetProperty("storageUsedBytes").GetInt64());
                }

                // The product ExportBuild policy permits Confidential fields; a narrower policy must redact them.
                var narrowBytes = await new TenantExportRepository(db,
                        new CanonicalFileMetadataRedactionService(new CanonicalRedactionService()))
                    .CreateMetadataZipAsync(own.Tenant.Id, new AuthorizationContext(actor.Id, own.Tenant.Id,
                        "TenantExport", "ExportBuild", "native-redaction-negative", RedactionAuthorizationState.Allowed,
                        FieldAccessPolicySnapshot.StandardAuthorized));
                using var narrowStream = new MemoryStream(narrowBytes);
                await using var narrowArchive = new ZipArchive(narrowStream, ZipArchiveMode.Read);
                await using var narrowComment = await narrowArchive.GetEntry("comments.json")!.OpenAsync();
                using var narrowJson = await JsonDocument.ParseAsync(narrowComment);
                Assert.Equal("[redacted:restricted]", Assert.Single(narrowJson.RootElement.EnumerateArray()).GetProperty("body").GetString());
            });

    private static ExportGraph Seed(AppDbContext db, User actor, string label)
    {
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var tenant = new Tenant { Name = label, DisplayName = label, Slug = label + "-" + Guid.NewGuid().ToString("N") };
        var workspace = new Workspace { TenantId = tenant.Id, Name = label, Slug = label, CreatedByUserId = actor.Id };
        var project = new Project { TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = label, Slug = label,
            OwnerUserId = actor.Id, CreatedByUserId = actor.Id, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.WorkspaceVisible, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = now, ActivationVersion = 1 };
        var item = new ActivityEvent { TenantId = tenant.Id, WorkspaceId = workspace.Id, CreatedByUserId = actor.Id,
            Title = label, StartsAt = now, EndsAt = now.AddHours(1), Capacity = 10 };
        var attendance = new EventAttendance { TenantId = tenant.Id, EventId = item.Id, UserId = actor.Id, Comment = label };
        var comment = new Comment { TenantId = tenant.Id, WorkspaceId = workspace.Id, AuthorUserId = actor.Id,
            TargetType = CommentTargetType.Project, TargetId = project.Id, Body = "Synthetic confidential comment" };
        comment.MarkDeleted(now, actor.Id, "Synthetic lifecycle metadata");
        var artifact = new Artifact { TenantId = tenant.Id, ProjectId = project.Id, Name = label, CreatedByUserId = actor.Id };
        var notification = new Notification { TenantId = tenant.Id, UserId = actor.Id, Title = label,
            Body = "Synthetic confidential notification", StateVersion = 7, CreatedAt = now };
        var audit = new AuditLog { TenantId = tenant.Id, ActorUserId = actor.Id, WorkspaceId = workspace.Id,
            ProjectId = project.Id, Action = "SyntheticProjection", EntityType = "Project", EntityId = project.Id,
            Summary = label, CreatedAt = now, CorrelationId = "synthetic-correlation" };
        var settings = new TenantSettings { TenantId = tenant.Id, DisplayName = label, FeatureFlagsJson = "{}" };
        var usage = new UsageRecord { TenantId = tenant.Id, Date = new DateOnly(2026, 11, 3), StorageUsedBytes = 23,
            ApiRequestCount = 17, ActiveUserCount = 1, TotalUserCount = 1, CreatedAt = now };
        db.AddRange(tenant, workspace, project, item, attendance, comment, artifact, notification, audit, settings, usage,
            new TenantUser { TenantId = tenant.Id, UserId = actor.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
        var rows = new Entity[] { item, attendance, comment, artifact, notification, audit, settings, usage };
        return new ExportGraph(tenant, settings, item, attendance, notification, usage,
            rows.ToDictionary(row => row.Id, row => row.GetType()));
    }

    private sealed record ExportGraph(Tenant Tenant, TenantSettings Settings, ActivityEvent Event, EventAttendance Attendance,
        Notification Notification, UsageRecord Usage, IReadOnlyDictionary<Guid, Type> Rows);

    // Observes the real serialization boundary, then delegates unchanged to the production redactor.
    private sealed class ScalarProjectionObserver(AppDbContext db, Guid tenantId, IReadOnlyDictionary<Guid, Type> expectedRows) : IRedactionService
    {
        public Guid ExpectedTenantId { get; } = tenantId;
        private readonly CanonicalFileMetadataRedactionService _inner = new(new CanonicalRedactionService());
        public HashSet<Guid> Observed { get; } = [];
        public RedactionResult Redact(AuthorizationContext context, object source, RedactionProfile profile)
        {
            Assert.Equal(RedactionProfile.ExportRow, profile);
            Assert.Equal(ExpectedTenantId, context.TenantId);
            var properties = source.GetType().GetProperties();
            if (source.GetType().GetProperty("Id")?.GetValue(source) is Guid id && expectedRows.TryGetValue(id, out var type))
            {
                // EF's independent model is the scalar schema oracle; navigation properties must never enter redaction.
                var expected = db.Model.FindEntityType(type)!.GetProperties().Where(property => property.PropertyInfo is not null)
                    .Select(property => property.PropertyInfo!.Name).ToHashSet(StringComparer.Ordinal);
                if (typeof(SoftDeletableEntity).IsAssignableFrom(type)) expected.Add(nameof(SoftDeletableEntity.IsDeleted));
                Assert.Equal(expected.Order(), properties.Select(property => property.Name).Order());
                Assert.True(Observed.Add(id));
            }
            return _inner.Redact(context, source, profile);
        }
    }
}
