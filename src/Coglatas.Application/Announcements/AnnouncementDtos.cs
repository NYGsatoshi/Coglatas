using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Announcements;

public sealed record AnnouncementListQuery(int Page = 1, int PageSize = 20, Guid? WorkspaceId = null, Guid? GroupId = null, Guid? ChannelId = null);

public sealed record AnnouncementListItemResponse(
    Guid Id,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ChannelId,
    Guid AuthorUserId,
    string Title,
    AnnouncementPriority Priority,
    bool IsPinned,
    bool RequiresReadConfirmation,
    bool IsRead,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt);

public sealed record AnnouncementDetailResponse
{
    public AnnouncementDetailResponse(
        Guid Id,
        Guid? WorkspaceId,
        Guid? GroupId,
        Guid? ChannelId,
        Guid AuthorUserId,
        string Title,
        string Body,
        AnnouncementPriority Priority,
        bool IsPinned,
        bool RequiresReadConfirmation,
        bool IsRead,
        DateTimeOffset PublishedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt)
    {
        var content = AnnouncementContentContract.Decode(Body);
        this.Id = Id;
        this.WorkspaceId = WorkspaceId;
        this.GroupId = GroupId;
        this.ChannelId = ChannelId;
        this.AuthorUserId = AuthorUserId;
        this.Title = Title;
        this.Body = content.Body;
        this.Priority = Priority;
        this.IsPinned = IsPinned;
        this.RequiresReadConfirmation = RequiresReadConfirmation;
        this.IsRead = IsRead;
        this.PublishedAt = PublishedAt;
        this.ExpiresAt = ExpiresAt;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        Cta = content.Cta;
        Attachment = content.Attachment;
    }

    public Guid Id { get; init; }
    public Guid? WorkspaceId { get; init; }
    public Guid? GroupId { get; init; }
    public Guid? ChannelId { get; init; }
    public Guid AuthorUserId { get; init; }
    public string Title { get; init; }
    public string Body { get; init; }
    public AnnouncementPriority Priority { get; init; }
    public bool IsPinned { get; init; }
    public bool RequiresReadConfirmation { get; init; }
    public bool IsRead { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public AnnouncementActionLink? Cta { get; init; }
    public AnnouncementActionLink? Attachment { get; init; }
}

public sealed record AnnouncementAudienceOptionResponse(
    string Key,
    string ScopeType,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ChannelId,
    string DisplayName,
    int EstimatedRecipientCount,
    string ScheduleTimeZoneId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAnnouncementRequest
{
    [JsonConstructor]
    public CreateAnnouncementRequest(
        Guid? WorkspaceId,
        Guid? GroupId,
        Guid? ChannelId,
        string Title,
        string Body,
        AnnouncementPriority Priority = AnnouncementPriority.Normal,
        bool IsPinned = false,
        bool RequiresReadConfirmation = false,
        DateTimeOffset? PublishedAt = null,
        DateTimeOffset? ExpiresAt = null,
        AnnouncementActionLink? Cta = null,
        AnnouncementActionLink? Attachment = null)
    {
        var content = AnnouncementContentContract.PrepareForPersistence(Body, Cta, Attachment);
        this.WorkspaceId = WorkspaceId;
        this.GroupId = GroupId;
        this.ChannelId = ChannelId;
        this.Title = Title;
        this.Body = content.PersistedBody;
        this.Priority = Priority;
        this.IsPinned = IsPinned;
        this.RequiresReadConfirmation = RequiresReadConfirmation;
        this.PublishedAt = PublishedAt;
        this.ExpiresAt = ExpiresAt;
        this.Cta = content.Cta;
        this.Attachment = content.Attachment;
    }

    public Guid? WorkspaceId { get; init; }
    public Guid? GroupId { get; init; }
    public Guid? ChannelId { get; init; }
    public string Title { get; init; }
    public string Body { get; init; }
    public AnnouncementPriority Priority { get; init; }
    public bool IsPinned { get; init; }
    public bool RequiresReadConfirmation { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public AnnouncementActionLink? Cta { get; init; }
    public AnnouncementActionLink? Attachment { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAnnouncementRequest
{
    [JsonConstructor]
    public UpdateAnnouncementRequest(
        string? Title = null,
        string? Body = null,
        AnnouncementPriority? Priority = null,
        bool? IsPinned = null,
        bool? RequiresReadConfirmation = null,
        DateTimeOffset? PublishedAt = null,
        DateTimeOffset? ExpiresAt = null,
        AnnouncementActionLink? Cta = null,
        AnnouncementActionLink? Attachment = null)
    {
        if (Body is null && (Cta is not null || Attachment is not null))
        {
            throw new JsonException("Announcement body must be supplied when CTA or attachment content is updated.");
        }

        var content = Body is null
            ? null
            : AnnouncementContentContract.PrepareForPersistence(Body, Cta, Attachment);
        this.Title = Title;
        this.Body = content?.PersistedBody;
        this.Priority = Priority;
        this.IsPinned = IsPinned;
        this.RequiresReadConfirmation = RequiresReadConfirmation;
        this.PublishedAt = PublishedAt;
        this.ExpiresAt = ExpiresAt;
        this.Cta = content?.Cta;
        this.Attachment = content?.Attachment;
    }

    public string? Title { get; init; }
    public string? Body { get; init; }
    public AnnouncementPriority? Priority { get; init; }
    public bool? IsPinned { get; init; }
    public bool? RequiresReadConfirmation { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public AnnouncementActionLink? Cta { get; init; }
    public AnnouncementActionLink? Attachment { get; init; }
}

public sealed record AnnouncementUnreadUserResponse(Guid UserId, string DisplayName, string Email);

public sealed record AnnouncementReadStatusResponse(
    Guid AnnouncementId,
    int TargetUserCount,
    int ReadCount,
    int UnreadCount,
    IReadOnlyList<AnnouncementUnreadUserResponse> UnreadUsers);
