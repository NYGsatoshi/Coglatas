using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Audit;

public sealed class DbAuditLoggerPrivacyTests
{
    [Fact]
    public async Task TaskSensitiveMetadataIsRemovedWhileSafeClassificationRemains()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var tenant = new CurrentTenantService();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenant);
        var logger = new DbAuditLogger(db, FixedClock.Instance, new CurrentUser(actorId), tenant);

        await logger.LogAsync(new AuditLogEntry(
            actorId,
            "TaskDeadlineChanged",
            "TaskItem",
            Guid.NewGuid(),
            "Task deadline changed.",
            Metadata: new Dictionary<string, object?>
            {
                ["deadlineChangeClassification"] = "ShiftAtLeast24Hours",
                ["commentBody"] = "private comment",
                ["reviewReturnReason"] = "private review reason",
                ["watchState"] = new { isWatching = true },
                ["taskNotificationPreference"] = "08:00",
                ["taskTitle"] = "restricted title",
                ["licenseKey"] = "private-license"
            }));

        var audit = Assert.Single(db.AuditLogs.Local);
        using var metadata = JsonDocument.Parse(Assert.IsType<string>(audit.MetadataJson));
        Assert.Equal(
            "ShiftAtLeast24Hours",
            metadata.RootElement.GetProperty("deadlineChangeClassification").GetString());
        Assert.False(metadata.RootElement.TryGetProperty("commentBody", out _));
        Assert.False(metadata.RootElement.TryGetProperty("reviewReturnReason", out _));
        Assert.False(metadata.RootElement.TryGetProperty("watchState", out _));
        Assert.False(metadata.RootElement.TryGetProperty("taskNotificationPreference", out _));
        Assert.False(metadata.RootElement.TryGetProperty("taskTitle", out _));
        Assert.False(metadata.RootElement.TryGetProperty("licenseKey", out _));
    }

    [Fact]
    [Trait("Scope", "Issue357")]
    public async Task ExecutionScopeAuditActionsFailClosedWhenAuditStagingIsUnavailable()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var tenant = new CurrentTenantService();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenant);
        var logger = new DbAuditLogger(db, FixedClock.Instance, new CurrentUser(actorId), tenant);
        await db.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => logger.LogAsync(new AuditLogEntry(
            actorId,
            "TaskExecutionRunRequested",
            "TaskExecutionRun",
            Guid.NewGuid())));
    }

    private sealed class FixedClock : IClock
    {
        public static FixedClock Instance { get; } = new();
        public DateTimeOffset UtcNow => new(2026, 8, 2, 3, 0, 0, TimeSpan.Zero);
    }

    private sealed class CurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }
}
