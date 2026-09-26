using Coglatas.Application.Common;

namespace Coglatas.Application.TenantAdministration;

public interface ITenantAdministrationService
{
    Task<Result<PlatformOverviewResponse>> GetPlatformOverviewAsync(CancellationToken cancellationToken = default);

    Task<Result<PlatformUsageResponse>> GetPlatformUsageAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantOverviewResponse>> GetCurrentTenantOverviewAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantSettingsResponse>> GetCurrentTenantSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantSettingsResponse>> UpdateCurrentTenantSettingsAsync(UpdateTenantSettingsRequest request, CancellationToken cancellationToken = default);

    Task<Result<TenantFeaturesResponse>> GetCurrentTenantFeaturesAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantUsageResponse>> GetCurrentTenantUsageAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantUsageResponse>> GetPlatformTenantUsageAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PlanResponse>>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<Result<PlanResponse>> CreatePlanAsync(UpsertPlanRequest request, CancellationToken cancellationToken = default);

    Task<Result<PlanResponse>> UpdatePlanAsync(Guid planId, UpsertPlanRequest request, CancellationToken cancellationToken = default);

    Task<Result> ArchivePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<Result<SubscriptionResponse>> GetTenantSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Result<SubscriptionResponse>> UpdateTenantSubscriptionAsync(Guid tenantId, UpdateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
