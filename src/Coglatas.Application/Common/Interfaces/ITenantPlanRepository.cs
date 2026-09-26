using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface ITenantPlanRepository
{
    Task<TenantSettings?> GetTenantSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a feature-flag source without retaining it in the current unit of
    /// work. Digest generation uses this after its PostgreSQL source fence so
    /// a preflight read cannot be reused as stale tracked state at commit.
    /// </summary>
    Task<TenantSettings?> GetTenantSettingsForFeatureEvaluationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        GetTenantSettingsAsync(tenantId, cancellationToken);

    Task<TenantSettings> GetOrCreateTenantSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<Plan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default);

    Task AddPlanAsync(Plan plan, CancellationToken cancellationToken = default);

    Task<Subscription?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current active subscription and Plan without tracking either
    /// source in a caller's unit of work. See
    /// <see cref="GetTenantSettingsForFeatureEvaluationAsync"/>.
    /// </summary>
    Task<Subscription?> GetActiveSubscriptionForFeatureEvaluationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        GetActiveSubscriptionAsync(tenantId, cancellationToken);

    Task<Subscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default);

    Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
