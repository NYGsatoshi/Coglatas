using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Notifications;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.Notifications;

[Trait("Scope", "TaskV1PR07C")]
public sealed class TaskDeadlineDigestRepositoryFallbackTests
{
    [Fact]
    public async Task FallbackIdenticalScheduleUpsertIsNoOp()
    {
        var tenantScope = new CurrentTenantService();
        tenantScope.SetPlatformScope();
        var saveCounter = new SaveCounterInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(saveCounter)
            .Options;
        await using var db = new AppDbContext(options, tenantScope);
        var tenant = new Tenant
        {
            Name = "Digest fallback tenant",
            DisplayName = "Digest fallback tenant",
            Slug = $"digest-fallback-{Guid.NewGuid():N}",
            Status = TenantStatus.Active
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantScope.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace
        {
            TenantId = tenant.Id,
            Name = "Digest fallback workspace",
            Slug = $"digest-fallback-workspace-{Guid.NewGuid():N}",
            Status = WorkspaceStatus.Active,
            CreatedByUserId = Guid.NewGuid()
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var repository = new TaskDeadlineDigestRepository(db, tenantScope);
        var dueAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
        var schedule = new TaskDeadlineDigestScheduleWrite(
            Guid.NewGuid(),
            workspace.Id,
            Guid.NewGuid(),
            new DateOnly(2026, 8, 3),
            TaskDeadlineDigestPolicy.PolicyVersion,
            dueAt);

        saveCounter.Reset();
        Assert.Equal(1, await repository.UpsertSchedulesAsync([schedule], dueAt));
        Assert.Equal(1, saveCounter.Saves);
        var original = await db.TaskDeadlineDigestJobs.AsNoTracking().SingleAsync();

        saveCounter.Reset();
        Assert.Equal(0, await repository.UpsertSchedulesAsync([schedule], dueAt.AddMinutes(1)));

        Assert.Equal(0, saveCounter.Saves);
        Assert.DoesNotContain(db.ChangeTracker.Entries<TaskDeadlineDigestJob>(), entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        var persisted = await db.TaskDeadlineDigestJobs.AsNoTracking().SingleAsync();
        Assert.Equal(original.UpdatedAt, persisted.UpdatedAt);
        Assert.Equal(original.ScheduledForUtc, persisted.ScheduledForUtc);
        Assert.Equal(original.NextAttemptAt, persisted.NextAttemptAt);
    }

    private sealed class SaveCounterInterceptor : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public void Reset() => Saves = 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Saves++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
