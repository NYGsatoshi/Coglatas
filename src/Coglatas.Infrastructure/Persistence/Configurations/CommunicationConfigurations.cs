using Coglatas.Application.Notifications;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(announcement => announcement.Title).HasMaxLength(200).IsRequired();
        builder.Property(announcement => announcement.Body).HasMaxLength(20000).IsRequired();
        builder.Property(announcement => announcement.Priority).HasEnumStringConversion().IsRequired();
        builder.Property(announcement => announcement.PublishedAt).IsRequired();

        builder.HasIndex(announcement => announcement.WorkspaceId);
        builder.HasIndex(announcement => announcement.GroupId);
        builder.HasIndex(announcement => announcement.ChannelId);
        builder.HasIndex(announcement => announcement.PublishedAt);
        builder.HasIndex(announcement => announcement.ExpiresAt);
        builder.HasIndex(announcement => announcement.AuthorUserId);
        builder.HasIndex(announcement => announcement.IsPinned);

        builder
            .HasOne(announcement => announcement.Workspace)
            .WithMany()
            .HasForeignKey(announcement => announcement.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(announcement => announcement.Group)
            .WithMany()
            .HasForeignKey(announcement => announcement.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(announcement => announcement.Channel)
            .WithMany()
            .HasForeignKey(announcement => announcement.ChannelId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(announcement => announcement.AuthorUser)
            .WithMany()
            .HasForeignKey(announcement => announcement.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnnouncementReadConfiguration : IEntityTypeConfiguration<AnnouncementRead>
{
    public void Configure(EntityTypeBuilder<AnnouncementRead> builder)
    {
        builder.ToTable("announcement_reads");
        builder.ConfigureEntity();

        builder.Property(read => read.ReadAt).IsRequired();

        builder.HasIndex(read => new { read.TenantId, read.AnnouncementId, read.UserId }).IsUnique();
        builder.HasIndex(read => read.UserId);
        builder.HasIndex(read => read.ReadAt);

        builder
            .HasOne(read => read.Announcement)
            .WithMany(announcement => announcement.Reads)
            .HasForeignKey(read => read.AnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(read => read.User)
            .WithMany()
            .HasForeignKey(read => read.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnnouncementDraftConfiguration : IEntityTypeConfiguration<AnnouncementDraft>
{
    public void Configure(EntityTypeBuilder<AnnouncementDraft> builder)
    {
        builder.ToTable("announcement_drafts");
        builder.ConfigureAuditableEntity();

        builder.Property(draft => draft.Title).HasMaxLength(200).IsRequired();
        builder.Property(draft => draft.Body).HasMaxLength(20000).IsRequired();
        builder.Property(draft => draft.Priority).HasEnumStringConversion().IsRequired();
        builder.Property(draft => draft.Status).HasEnumStringConversion().IsRequired();
        builder.Property(draft => draft.VersionNo).IsConcurrencyToken().IsRequired();
        builder.Property(draft => draft.ScheduleTimeZoneId).HasMaxLength(80);
        builder.Property(draft => draft.ScheduleLocalDateTime).HasColumnType("timestamp without time zone");
        builder.Property(draft => draft.PublicationClaimOwner).HasMaxLength(160);
        builder.Property(draft => draft.LastPublicationFailureCode).HasMaxLength(80);

        builder.HasIndex(draft => draft.AuthorUserId);
        builder.HasIndex(draft => new { draft.TenantId, draft.AuthorUserId, draft.Status, draft.UpdatedAt });
        builder.HasIndex(draft => new
        {
            draft.TenantId,
            draft.Status,
            draft.ScheduledForUtc,
            draft.NextPublicationAttemptAtUtc,
            draft.PublicationClaimExpiresAtUtc
        });
        builder.HasIndex(draft => draft.PublishedAnnouncementId).IsUnique();

        builder
            .HasOne(draft => draft.AuthorUser)
            .WithMany()
            .HasForeignKey(draft => draft.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(draft => draft.Workspace)
            .WithMany()
            .HasForeignKey(draft => draft.WorkspaceId)
            // A durable selected target must not silently turn into a global
            // target if a parent is physically removed. Soft lifecycle state
            // still makes it fail authorization; a physical delete is
            // restricted until the retained draft is handled explicitly.
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(draft => draft.Group)
            .WithMany()
            .HasForeignKey(draft => draft.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(draft => draft.Channel)
            .WithMany()
            .HasForeignKey(draft => draft.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne(draft => draft.PublishedAnnouncement)
            .WithMany()
            .HasForeignKey(draft => draft.PublishedAnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.ConfigureEntity();

        builder.Property(notification => notification.NotificationType).HasEnumStringConversion().IsRequired();
        builder.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Body).HasMaxLength(2000);
        builder.Property(notification => notification.RelatedEntityType).HasMaxLength(80);
        builder.Property(notification => notification.LogicalKey).HasMaxLength(NotificationLogicalKeyContract.MaximumLength);
        builder.Property(notification => notification.CreatedAt).IsRequired();
        builder.Property(notification => notification.StateVersion).IsRequired();

        builder.HasIndex(notification => notification.UserId);
        builder.HasIndex(notification => notification.CreatedAt);
        builder.HasIndex(notification => notification.ReadAt);
        builder.HasIndex(notification => notification.DeletedAt);
        builder.HasIndex(notification => new { notification.UserId, notification.IsRead, notification.DeletedAt });
        builder.HasIndex(notification => new { notification.TenantId, notification.UserId, notification.IsRead, notification.CreatedAt });
        builder.HasIndex(notification => new { notification.RelatedEntityType, notification.RelatedEntityId });
        builder.HasIndex(notification => new { notification.TenantId, notification.UserId, notification.LogicalKey })
            .HasDatabaseName(NotificationLogicalKeyContract.UniqueIndexName)
            .HasFilter("\"LogicalKey\" IS NOT NULL")
            .IsUnique();

        builder
            .HasOne(notification => notification.User)
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NotificationUserStateConfiguration : IEntityTypeConfiguration<NotificationUserState>
{
    public void Configure(EntityTypeBuilder<NotificationUserState> builder)
    {
        builder.ToTable("notification_user_states");
        builder.ConfigureEntity();

        // Every notification producer advances the same recipient-private
        // sequence. Optimistic concurrency makes a racing producer retry
        // instead of committing a duplicate stateVersion/lost update.
        builder.Property(state => state.Version)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(state => state.UpdatedAt).IsRequired();
        builder.HasIndex(state => new { state.TenantId, state.UserId }).IsUnique();

        builder
            .HasOne(state => state.User)
            .WithMany()
            .HasForeignKey(state => state.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
