namespace Coglatas.Application.Common.Interfaces;

public interface ITenantResolver
{
    Task<TenantResolutionResult> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed record TenantResolutionResult(
    bool IsResolved,
    Guid? TenantId,
    string? TenantSlug,
    string? FailureReason = null)
{
    public static TenantResolutionResult Resolved(Guid tenantId, string tenantSlug)
    {
        return new TenantResolutionResult(true, tenantId, tenantSlug);
    }

    public static TenantResolutionResult Unresolved(string? failureReason = null)
    {
        return new TenantResolutionResult(false, null, null, failureReason);
    }
}
