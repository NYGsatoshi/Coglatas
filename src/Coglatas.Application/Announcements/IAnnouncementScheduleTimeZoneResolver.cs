using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Announcements;

/// <summary>
/// Resolves the organizational timezone fixed by the selected announcement
/// audience: Workspace, then Tenant, then UTC. The returned IANA identifier is
/// displayed before confirmation and is resolved again when scheduling.
/// </summary>
public interface IAnnouncementScheduleTimeZoneResolver
{
    Task<TimeZoneInfo> ResolveAsync(
        Guid tenantId,
        Guid? workspaceId,
        CancellationToken cancellationToken = default);
}

public sealed class AnnouncementScheduleTimeZoneResolver(
    IWorkspaceRepository workspaces,
    ITenantPlanRepository tenantPlans) : IAnnouncementScheduleTimeZoneResolver
{
    public async Task<TimeZoneInfo> ResolveAsync(
        Guid tenantId,
        Guid? workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId.HasValue)
        {
            var workspace = await workspaces.GetByIdAsync(workspaceId.Value, cancellationToken);
            var workspaceZone = workspace?.TenantId == tenantId ? TryResolve(workspace.TimeZone) : null;
            if (workspaceZone is not null)
            {
                return workspaceZone;
            }
        }

        var settings = await tenantPlans.GetTenantSettingsAsync(tenantId, cancellationToken);
        return TryResolve(settings?.TimeZone) ?? TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo? TryResolve(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
