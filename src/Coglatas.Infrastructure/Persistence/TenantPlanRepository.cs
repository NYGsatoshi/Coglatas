using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class TenantPlanRepository(AppDbContext dbContext) : ITenantPlanRepository
{
    public Task<TenantSettings?> GetTenantSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return dbContext.TenantSettings.FirstOrDefaultAsync(settings => settings.TenantId == tenantId, cancellationToken);
    }

    public Task<TenantSettings?> GetTenantSettingsForFeatureEvaluationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(settings => settings.TenantId == tenantId, cancellationToken);
    }

    public async Task<TenantSettings> GetOrCreateTenantSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetTenantSettingsAsync(tenantId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
        settings = new TenantSettings
        {
            TenantId = tenantId,
            DisplayName = tenant?.DisplayName ?? tenant?.Name ?? "Tenant"
        };

        await dbContext.TenantSettings.AddAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Plans.AsNoTracking().OrderBy(plan => plan.Name).ToListAsync(cancellationToken);
    }

    public Task<Plan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        return dbContext.Plans.FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken);
    }

    public Task AddPlanAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        return dbContext.Plans.AddAsync(plan, cancellationToken).AsTask();
    }

    public Task<Subscription?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return dbContext.Subscriptions
            .Include(subscription => subscription.Plan)
            .Where(subscription => subscription.TenantId == tenantId)
            .Where(subscription => subscription.Status == SubscriptionStatus.Trial || subscription.Status == SubscriptionStatus.Active)
            .OrderByDescending(subscription => subscription.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Subscription?> GetActiveSubscriptionForFeatureEvaluationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Plan)
            .Where(subscription => subscription.TenantId == tenantId)
            .Where(subscription => subscription.Status == SubscriptionStatus.Trial || subscription.Status == SubscriptionStatus.Active)
            .OrderByDescending(subscription => subscription.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Subscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        return dbContext.Subscriptions
            .Include(subscription => subscription.Plan)
            .FirstOrDefaultAsync(subscription => subscription.Id == subscriptionId, cancellationToken);
    }

    public Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        return dbContext.Subscriptions.AddAsync(subscription, cancellationToken).AsTask();
    }

    public async Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var activeUserCount = await dbContext.TenantUsers
            .IgnoreQueryFilters()
            .CountAsync(user => user.TenantId == tenantId && user.Status == TenantUserStatus.Active, cancellationToken);
        var totalUserCount = await dbContext.TenantUsers
            .IgnoreQueryFilters()
            .CountAsync(user => user.TenantId == tenantId, cancellationToken);
        var projectCount = await dbContext.Projects
            .IgnoreQueryFilters()
            .CountAsync(project => project.TenantId == tenantId && !project.DeletedAt.HasValue, cancellationToken);
        var taskCount = await dbContext.TaskItems
            .IgnoreQueryFilters()
            .CountAsync(task => task.TenantId == tenantId && !task.DeletedAt.HasValue, cancellationToken);
        var fileQuery = dbContext.FileObjects
            .IgnoreQueryFilters()
            .Where(file => file.TenantId == tenantId && file.Status != FileObjectStatus.Deleted && !file.DeletedAt.HasValue);
        var fileCount = await fileQuery.CountAsync(cancellationToken);
        var storageUsedBytes = await fileQuery.SumAsync(file => (long?)file.SizeBytes, cancellationToken) ?? 0;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var apiRequestCount = await dbContext.UsageRecords
            .IgnoreQueryFilters()
            .Where(record => record.TenantId == tenantId && record.Date == today)
            .Select(record => (int?)record.ApiRequestCount)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        return new TenantUsageSnapshot(tenantId, activeUserCount, totalUserCount, projectCount, taskCount, fileCount, storageUsedBytes, apiRequestCount);
    }
}
