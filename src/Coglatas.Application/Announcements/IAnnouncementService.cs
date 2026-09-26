using Coglatas.Application.Common;

namespace Coglatas.Application.Announcements;

public interface IAnnouncementService
{
    Task<Result<PagedResponse<AnnouncementListItemResponse>>> ListAsync(AnnouncementListQuery query, CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDetailResponse>> CreateAsync(CreateAnnouncementRequest request, CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDetailResponse>> GetAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDetailResponse>> UpdateAsync(Guid announcementId, UpdateAnnouncementRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<Result> MarkReadAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<Result<AnnouncementReadStatusResponse>> GetReadStatusAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<Result> ResendUnreadAsync(Guid announcementId, CancellationToken cancellationToken = default);
}
