using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", table =>
        {
            table.HasCheckConstraint(
                "CK_conversations_workspace_general_shape",
                "\"DefaultKind\" <> 'WorkspaceGeneral' OR (\"Type\" = 'WorkspaceChannel' AND \"ProjectId\" IS NULL AND \"Visibility\" = 'PublicWithinScope')");
            table.HasCheckConstraint(
                "CK_conversations_project_general_shape",
                "\"DefaultKind\" <> 'ProjectGeneral' OR (\"Type\" = 'ProjectChannel' AND \"ProjectId\" IS NOT NULL AND \"Visibility\" = 'PublicWithinScope')");
        });
        builder.ConfigureAuditableEntity();

        builder.Property(conversation => conversation.Type).HasEnumStringConversion().IsRequired();
        builder.Property(conversation => conversation.Visibility).HasConversion<string>().HasMaxLength(40);
        builder.Property(conversation => conversation.DefaultKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(conversation => conversation.Title).HasMaxLength(200);

        builder.HasIndex(conversation => conversation.WorkspaceId);
        builder.HasIndex(conversation => conversation.ProjectId);
        builder.HasIndex(conversation => conversation.ParentConversationId);
        builder.HasIndex(conversation => conversation.RootConversationId);
        builder.HasIndex(conversation => conversation.CreatedByUserId);
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.WorkspaceId });
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.WorkspaceId, conversation.ProjectId });
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.WorkspaceId, conversation.DefaultKind })
            .IsUnique()
            .HasFilter("\"DefaultKind\" = 'WorkspaceGeneral'");
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.ProjectId, conversation.DefaultKind })
            .IsUnique()
            .HasFilter("\"DefaultKind\" = 'ProjectGeneral' AND \"ProjectId\" IS NOT NULL");
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.ParentConversationId });
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.UpdatedAt });

        builder
            .HasOne(conversation => conversation.Workspace)
            .WithMany()
            .HasForeignKey(conversation => conversation.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(conversation => conversation.Project)
            .WithMany()
            .HasForeignKey(conversation => conversation.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(conversation => conversation.ParentConversation)
            .WithMany(conversation => conversation.Threads)
            .HasForeignKey(conversation => conversation.ParentConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(conversation => conversation.RootConversation)
            .WithMany()
            .HasForeignKey(conversation => conversation.RootConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(conversation => conversation.CreatedByUser)
            .WithMany()
            .HasForeignKey(conversation => conversation.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("conversation_members");
        builder.ConfigureAuditableEntity();

        builder.Property(member => member.JoinedAt).IsRequired();
        builder.Property(member => member.Role).HasEnumStringConversion().IsRequired();
        builder.Property(member => member.CanRead).IsRequired().HasDefaultValue(true);
        builder.Property(member => member.CanPost).IsRequired().HasDefaultValue(true);
        builder.Property(member => member.CanManageMembers).IsRequired().HasDefaultValue(false);
        builder.Property(member => member.CanCreateThread).IsRequired().HasDefaultValue(true);
        builder.Property(member => member.IsMuted).IsRequired().HasDefaultValue(false);
        builder.Property(member => member.IsArchived).IsRequired().HasDefaultValue(false);
        builder.Property(member => member.IsLater).IsRequired().HasDefaultValue(false);

        builder.HasIndex(member => new { member.TenantId, member.ConversationId, member.UserId }).IsUnique();
        builder.HasIndex(member => new { member.TenantId, member.UserId });
        builder.HasIndex(member => new { member.TenantId, member.UserId, member.IsLater });
        builder.HasIndex(member => member.UserId);
        builder.HasIndex(member => member.LastOpenedAt);
        builder.HasIndex(member => member.LastReadMessageId);
        builder.HasIndex(member => member.LastReadAt);
        builder.HasIndex(member => member.UnreadCursorMessageId);
        builder.HasIndex(member => member.LeftAt);
        builder.HasIndex(member => member.RemovedAt);
        builder.HasIndex(member => member.RemovedByUserId);

        builder
            .HasOne(member => member.Conversation)
            .WithMany(conversation => conversation.Members)
            .HasForeignKey(member => member.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.RemovedByUser)
            .WithMany()
            .HasForeignKey(member => member.RemovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.LastReadMessage)
            .WithMany()
            .HasForeignKey(member => member.LastReadMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(member => member.UnreadCursorMessage)
            .WithMany()
            .HasForeignKey(member => member.UnreadCursorMessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", table =>
        {
            table.HasCheckConstraint(
                "CK_messages_thread_root_not_self",
                "\"ThreadRootMessageId\" IS NULL OR \"ThreadRootMessageId\" <> \"Id\"");
        });
        builder.ConfigureSoftDeletableEntity();

        builder.Property(message => message.Body).HasMaxLength(12000).IsRequired();
        builder.Property(message => message.Version).IsRequired().HasDefaultValue(1L);

        builder.HasIndex(message => message.WorkspaceId);
        builder.HasIndex(message => message.ConversationId);
        builder.HasIndex(message => message.AuthorUserId);
        builder.HasIndex(message => message.EditedAt);
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt });
        builder.HasIndex(message => new { message.TenantId, message.WorkspaceId, message.CreatedAt });
        builder.HasIndex(message => new { message.TenantId, message.ConversationId, message.CreatedAt });
        builder.HasIndex(message => new { message.TenantId, message.ConversationId, message.ThreadRootMessageId, message.CreatedAt, message.Id })
            .HasDatabaseName("IX_messages_thread_replies");
        builder.HasIndex(message => new { message.TenantId, message.ConversationId, message.AuthorUserId, message.ClientRequestId })
            .IsUnique()
            .HasFilter("\"ClientRequestId\" IS NOT NULL");

        builder
            .HasOne(message => message.Workspace)
            .WithMany()
            .HasForeignKey(message => message.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(message => message.Conversation)
            .WithMany(conversation => conversation.Messages)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(message => message.AuthorUser)
            .WithMany()
            .HasForeignKey(message => message.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(message => message.ThreadRootMessage)
            .WithMany(message => message.ThreadReplies)
            .HasForeignKey(message => message.ThreadRootMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("message_attachments");
        builder.ConfigureEntity();

        builder.HasIndex(item => new { item.TenantId, item.MessageId, item.AttachmentId }).IsUnique();
        builder.HasIndex(item => item.AttachmentId);

        builder
            .HasOne(item => item.Message)
            .WithMany(message => message.Attachments)
            .HasForeignKey(item => item.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(item => item.Attachment)
            .WithMany()
            .HasForeignKey(item => item.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReadStateConfiguration : IEntityTypeConfiguration<ReadState>
{
    public void Configure(EntityTypeBuilder<ReadState> builder)
    {
        builder.ToTable("read_states");
        builder.ConfigureAuditableEntity();

        builder.Property(readState => readState.ScopeType).HasEnumStringConversion().IsRequired();
        builder.Property(readState => readState.LastReadAt).IsRequired();
        builder.Property(readState => readState.LastReadSequence).IsRequired().HasDefaultValue(0L);
        builder.Property(readState => readState.StateVersion).IsRequired().HasDefaultValue(0L);

        builder.HasIndex(readState => new { readState.TenantId, readState.UserId, readState.ScopeType, readState.ScopeId }).IsUnique();
        builder.HasIndex(readState => new { readState.TenantId, readState.UserId, readState.ConversationId }).IsUnique();
        builder.HasIndex(readState => readState.ScopeId);
        builder.HasIndex(readState => readState.ConversationId);
        builder.HasIndex(readState => readState.LastReadMessageId);
        builder.HasIndex(readState => readState.LastReadAt);

        builder
            .HasOne(readState => readState.User)
            .WithMany()
            .HasForeignKey(readState => readState.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(readState => readState.Conversation)
            .WithMany()
            .HasForeignKey(readState => readState.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(readState => readState.LastReadMessage)
            .WithMany()
            .HasForeignKey(readState => readState.LastReadMessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class MessageFollowUpConfiguration : IEntityTypeConfiguration<MessageFollowUp>
{
    public void Configure(EntityTypeBuilder<MessageFollowUp> builder)
    {
        builder.ToTable("message_follow_ups");
        builder.ConfigureAuditableEntity();

        builder.HasIndex(item => new { item.TenantId, item.UserId, item.MessageId }).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.UserId, item.CreatedAt, item.Id });
        builder.HasIndex(item => item.MessageId);

        builder
            .HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(item => item.Message)
            .WithMany()
            .HasForeignKey(item => item.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
