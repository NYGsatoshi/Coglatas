using System.Text;
using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Announcement : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementPriority Priority { get; set; } = AnnouncementPriority.Normal;
    public bool IsPinned { get; set; }
    public bool RequiresReadConfirmation { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Channel? Channel { get; set; }
    public User? AuthorUser { get; set; }
    public ICollection<AnnouncementRead> Reads { get; } = new List<AnnouncementRead>();
}

public sealed class AnnouncementRead : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AnnouncementId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; }

    public Announcement? Announcement { get; set; }
    public User? User { get; set; }
}

/// <summary>
/// A server-owned editable Announcement buffer. Browser form state is never
/// publication authority: every transition reauthorizes this stored scope.
/// The selected scope remains one exact canonical target; #378 deliberately
/// does not introduce a campaign or multi-audience model.
/// </summary>
public sealed class AnnouncementDraft : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementPriority Priority { get; set; } = AnnouncementPriority.Normal;
    public bool IsPinned { get; set; }
    public bool RequiresReadConfirmation { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public AnnouncementDraftStatus Status { get; set; } = AnnouncementDraftStatus.Draft;
    public long VersionNo { get; set; } = 1;

    /// <summary>
    /// The original local value and IANA zone are retained only so the author
    /// can accurately review the accepted schedule. The worker uses the UTC
    /// instant below and never reinterprets a wall-clock value.
    /// </summary>
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public string? ScheduleTimeZoneId { get; set; }
    public DateTime? ScheduleLocalDateTime { get; set; }
    public int? ScheduleUtcOffsetMinutes { get; set; }

    public Guid? PublishedAnnouncementId { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }

    // Short-lived worker lease. It is server operational state, never an API
    // authority or browser-visible credential.
    public string? PublicationClaimOwner { get; set; }
    public Guid? PublicationClaimToken { get; set; }
    public DateTimeOffset? PublicationClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? NextPublicationAttemptAtUtc { get; set; }
    public int PublicationAttemptCount { get; set; }
    public string? LastPublicationFailureCode { get; set; }

    public User? AuthorUser { get; set; }
    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Channel? Channel { get; set; }
    public Announcement? PublishedAnnouncement { get; set; }
}

public sealed class Notification : Entity, ITenantEntity
{
    public const int TitleMaximumLength = 200;
    public const int BodyMaximumLength = 2000;

    private string title = string.Empty;
    private string? body;

    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>
    /// Immutable, recipient-specific identity for a newly produced logical
    /// notification. Legacy rows deliberately retain a null value.
    /// </summary>
    public string? LogicalKey { get; set; }
    public NotificationType NotificationType { get; set; } = NotificationType.System;
    public string Title
    {
        get => title;
        set => title = NormalizeTitle(value);
    }

    public string? Body
    {
        get => body;
        set => body = value is null ? null : TruncateToScalarLength(value, BodyMaximumLength);
    }

    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long StateVersion { get; set; }

    public User? User { get; set; }

    public static string NormalizeTitle(string? value)
    {
        return TruncateToScalarLength(value ?? string.Empty, TitleMaximumLength);
    }

    private static string TruncateToScalarLength(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var builder = new StringBuilder(maximumLength);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count >= maximumLength)
            {
                break;
            }

            builder.Append(rune.ToString());
            count++;
        }

        return builder.ToString();
    }
}

/// <summary>
/// Monotonic, recipient-private ordering token for notification state changes.
/// It is deliberately separate from a notification row so all-read and delete
/// operations can be reconciled safely across the user's tabs and devices.
/// </summary>
public sealed class NotificationUserState : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
}