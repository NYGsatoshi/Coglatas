using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.TenantAdministration;

public sealed class QuotaService(ITenantPlanRepository tenantPlans) : IQuotaService
{
    public Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return tenantPlans.GetCurrentUsageAsync(tenantId, cancellationToken);
    }

    public async Task<Result> CanCreateUserAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var (usage, limits) = await GetLimitsAsync(tenantId, cancellationToken);
        if (limits.UserLimit > 0 && usage.TotalUserCount >= limits.UserLimit)
        {
            return Result.Failure($"Tenant user limit of {limits.UserLimit} has been reached.");
        }

        return Result.Success();
    }

    public async Task<Result> CanCreateProjectAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var (usage, limits) = await GetLimitsAsync(tenantId, cancellationToken);
        if (limits.ProjectLimit > 0 && usage.ProjectCount >= limits.ProjectLimit)
        {
            return Result.Failure($"Tenant project limit of {limits.ProjectLimit} has been reached.");
        }

        return Result.Success();
    }

    public async Task<Result> CanUploadFileAsync(Guid tenantId, long fileSizeBytes, CancellationToken cancellationToken = default)
    {
        var (usage, limits) = await GetLimitsAsync(tenantId, cancellationToken);
        if (limits.FileUploadLimitBytes > 0 && fileSizeBytes > limits.FileUploadLimitBytes)
        {
            return Result.Failure($"File exceeds the tenant upload limit of {limits.FileUploadLimitBytes} bytes.");
        }

        if (limits.StorageQuotaBytes > 0 && usage.StorageUsedBytes + fileSizeBytes > limits.StorageQuotaBytes)
        {
            return Result.Failure($"Tenant storage quota of {limits.StorageQuotaBytes} bytes would be exceeded.");
        }

        return Result.Success();
    }

    public Task<Result> CanInviteGuestAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // TODO: enforce guest counts after Prompt 23 expands guest membership semantics.
        return Task.FromResult(Result.Success());
    }

    public Task RecordApiRequestAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // TODO: persist API counters when request metering middleware is added.
        return Task.CompletedTask;
    }

    private async Task<(TenantUsageSnapshot Usage, TenantQuotaLimits Limits)> GetLimitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var usage = await tenantPlans.GetCurrentUsageAsync(tenantId, cancellationToken);
        var settings = await tenantPlans.GetTenantSettingsAsync(tenantId, cancellationToken);
        var subscription = await tenantPlans.GetActiveSubscriptionAsync(tenantId, cancellationToken);

        var storageQuota = settings?.StorageQuotaBytes > 0 ? settings.StorageQuotaBytes : subscription?.Plan?.MaxStorageBytes ?? 0;
        var userLimit = settings?.UserLimit > 0 ? settings.UserLimit : subscription?.Plan?.MaxUsers ?? 0;
        var projectLimit = settings?.ProjectLimit > 0 ? settings.ProjectLimit : subscription?.Plan?.MaxProjects ?? 0;
        var fileUploadLimit = settings?.FileUploadLimitBytes > 0 ? settings.FileUploadLimitBytes : 0;

        return (usage, new TenantQuotaLimits(storageQuota, userLimit, projectLimit, fileUploadLimit));
    }

    private sealed record TenantQuotaLimits(long StorageQuotaBytes, int UserLimit, int ProjectLimit, long FileUploadLimitBytes);
}
