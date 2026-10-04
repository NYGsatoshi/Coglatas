using Coglatas.Application.Announcements;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IAnnouncementRepository
{
    Task<PagedResponse<Announcement>> ListVisibleAsync(Guid userId, bool isSystemAdmin, AnnouncementListQuery query, CancellationToken cancellationToken = default);

    Task<Announcement?> GetAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<bool> IsVisibleToUserAsync(Guid announcementId, Guid userId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    Task<bool> HasReadAsync(Guid announcementId, Guid userId, CancellationToken cancellationToken = default);

    async Task<IReadOnlySet<Guid>> GetReadAnnouncementIdsAsync(Guid userId, IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default)
    {
        var readIds = new HashSet<Guid>();
        foreach (var announcementId in announcementIds.Distinct())
        {
            if (await HasReadAsync(announcementId, userId, cancellationToken)) readIds.Add(announcementId);
        }
        return readIds;
    }

    Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default);

    Task AddReadAsync(AnnouncementRead read, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementTargetUser>> ListTargetUsersAsync(Announcement announcement, CancellationToken cancellationToken = default);

    Task<int> CountReadsAsync(Guid announcementId, CancellationToken cancellationToken = default);
}

public sealed record AnnouncementTargetUser(Guid UserId, string DisplayName, string Email, bool HasRead);
