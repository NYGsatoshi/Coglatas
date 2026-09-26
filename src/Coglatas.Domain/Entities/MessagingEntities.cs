using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Conversation : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public ConversationType Type { get; set; } = ConversationType.DirectMessage;
    public ConversationVisibility? Visibility { get; set; }
    public ConversationDefaultKind? DefaultKind { get; set; }
    public string? Title { get; set; }
    public Guid? ParentConversationId { get; set; }
    public Guid? RootConversationId { get; set; }
    public bool IsArchived { get; set; }
    public bool IsLocked { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Workspace? Workspace { get; set; }
    public Project? Project { get; set; }
    public Conversation? ParentConversation { get; set; }
    public Conversation? RootConversation { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ConversationMember> Members { get; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; } = new List<Message>();
    public ICollection<Conversation> Threads { get; } = new List<Conversation>();
}

public sealed class ConversationMember : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public ConversationMemberRole Role { get; set; } = ConversationMemberRole.Member;
    public bool CanRead { get; set; } = true;
    public bool CanPost { get; set; } = true;
    public bool CanManageMembers { get; set; }
    public bool CanCreateThread { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
    public Guid? UnreadCursorMessageId { get; set; }
    public bool IsMuted { get; set; }
    public bool IsArchived { get; set; }
    public bool IsLater { get; set; }

    public Conversation? Conversation { get; set; }
    public User? User { get; set; }
    public User? RemovedByUser { get; set; }
    public Message? LastReadMessage { get; set; }
    public Message? UnreadCursorMessage { get; set; }
}

public sealed class Message : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AuthorUserId { get; set; }
    /// <summary>Client-generated idempotency key, scoped to the sender and conversation.</summary>
    public Guid? ClientRequestId { get; set; }
    /// <summary>
    /// Canonical root Message for a thread reply. Null identifies a message in
    /// the parent Conversation timeline (and a potential thread root).
    /// </summary>
    public Guid? ThreadRootMessageId { get; set; }
    /// <summary>Monotonic entity version used to reject stale realtime events.</summary>
    public long Version { get; set; } = 1;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? EditedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Conversation? Conversation { get; set; }
    public User? AuthorUser { get; set; }
    public Message? ThreadRootMessage { get; set; }
    public ICollection<Message> ThreadReplies { get; } = new List<Message>();
    public ICollection<MessageAttachment> Attachments { get; } = new List<MessageAttachment>();
}

public sealed class ReadState : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public ReadScopeType ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public Guid? LastReadItemId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? LastReadMessageId { get; set; }
    /// <summary>Server-derived ordering token for the current user's monotonic read cursor.</summary>
    public long LastReadSequence { get; set; }
    /// <summary>Monotonic state version for same-user cross-device reconciliation.</summary>
    public long StateVersion { get; set; }
    public DateTimeOffset LastReadAt { get; set; }

    public User? User { get; set; }
    public Conversation? Conversation { get; set; }
    public Message? LastReadMessage { get; set; }
}

public sealed class MessageAttachment : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public Guid AttachmentId { get; set; }

    public Message? Message { get; set; }
    public Attachment? Attachment { get; set; }
}

/// <summary>
/// A participant-private work marker for a Message. This state is deliberately
/// independent from ConversationMember/ReadState so saving a message never
/// changes unread cursors.
/// </summary>
public sealed class MessageFollowUp : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid MessageId { get; set; }

    public User? User { get; set; }
    public Message? Message { get; set; }
}
