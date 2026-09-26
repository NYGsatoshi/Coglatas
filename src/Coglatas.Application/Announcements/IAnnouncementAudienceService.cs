using Coglatas.Application.Common;

namespace Coglatas.Application.Announcements;

public interface IAnnouncementAudienceService
{
    Task<Result<IReadOnlyList<AnnouncementAudienceOptionResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<bool>> IsAuthorizedAsync(
        Guid? workspaceId,
        Guid? groupId,
        Guid? channelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rechecks one persisted selected target for a server-owned actor. This
    /// is used by the durable publisher after it establishes the draft's
    /// Tenant context; it never accepts a browser-supplied actor identity.
    /// </summary>
    Task<Result<bool>> IsAuthorizedForActorAsync(
        Guid actorUserId,
        Guid? workspaceId,
        Guid? groupId,
        Guid? channelId,
        CancellationToken cancellationToken = default);
}
