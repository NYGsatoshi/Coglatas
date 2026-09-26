using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.TenantAdministration;

public sealed class FeatureFlagService(
    ITenantPlanRepository tenantPlans,
    ICurrentTenant currentTenant) : IFeatureFlagService
{
    public async Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable)
        {
            return false;
        }

        var enabled = await GetEnabledFeaturesAsync(currentTenant.TenantId, cancellationToken);
        return enabled.Contains(FeatureKeys.Normalize(featureKey), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result> RequireEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
    {
        return await IsEnabledAsync(featureKey, cancellationToken)
            ? Result.Success()
            : Result.Failure($"Feature '{featureKey}' is disabled for this tenant.");
    }

    public async Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var enabled = new HashSet<string>(FeatureKeys.DefaultEnabled, StringComparer.OrdinalIgnoreCase);
        var subscription = await tenantPlans.GetActiveSubscriptionForFeatureEvaluationAsync(
            tenantId,
            cancellationToken);
        if (subscription?.Plan is not null)
        {
            enabled = ParseFeatureArray(subscription.Plan.EnabledFeaturesJson);
        }

        var settings = await tenantPlans.GetTenantSettingsForFeatureEvaluationAsync(
            tenantId,
            cancellationToken);
        if (settings is not null)
        {
            ApplyTenantOverrides(enabled, settings.FeatureFlagsJson);
        }

        return enabled.OrderBy(key => key).ToList();
    }

    private static HashSet<string> ParseFeatureArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(FeatureKeys.DefaultEnabled, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json);
            return values is { Length: > 0 }
                ? values.Select(FeatureKeys.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(FeatureKeys.DefaultEnabled, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void ApplyTenantOverrides(HashSet<string> enabled, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var overrides = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (overrides is null)
            {
                return;
            }

            foreach (var (key, isEnabled) in overrides)
            {
                var normalizedKey = FeatureKeys.Normalize(key);
                if (isEnabled)
                {
                    enabled.Add(normalizedKey);
                }
                else
                {
                    enabled.Remove(normalizedKey);
                }
            }
        }
        catch (JsonException)
        {
            // Invalid tenant override JSON should not disable the product shell.
        }
    }
}
