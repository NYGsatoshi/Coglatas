using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(attachment => attachment.FileName).HasMaxLength(260).IsRequired();
        builder.Property(attachment => attachment.StoredFileName).HasMaxLength(260).IsRequired();
        builder.Property(attachment => attachment.FilePath).HasMaxLength(1024).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(160).IsRequired();
        builder.Property(attachment => attachment.Extension).HasMaxLength(32).IsRequired();
        builder.Property(attachment => attachment.StorageProvider).HasMaxLength(80).IsRequired();
        builder.Property(attachment => attachment.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(attachment => attachment.OwnerType).HasConversion<string>().HasMaxLength(40);
        builder.Property(attachment => attachment.ScanStatus).HasEnumStringConversion().IsRequired();

        builder.HasIndex(attachment => attachment.FileObjectId);
        // A Task may reference a canonical file only once.  Restrict the uniqueness
        // boundary to active Task associations so legacy/message attachments retain
        // their existing semantics and soft-deleted links can be recreated.
        builder.HasIndex(attachment => new { attachment.OwnerType, attachment.OwnerId, attachment.FileObjectId })
            .HasFilter("\"OwnerType\" = 'TaskItem' AND \"DeletedAt\" IS NULL")
            .IsUnique();
        builder.HasIndex(attachment => attachment.WorkspaceId);
        builder.HasIndex(attachment => new { attachment.OwnerType, attachment.OwnerId });
        builder.HasIndex(attachment => attachment.OwnerUserId);
        builder.HasIndex(attachment => attachment.UploadedByUserId);
        builder.HasIndex(attachment => attachment.ScanStatus);
        builder.HasIndex(attachment => attachment.Extension);

        builder
            .HasOne(attachment => attachment.FileObject)
            .WithMany()
            .HasForeignKey(attachment => attachment.FileObjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attachment => attachment.Workspace)
            .WithMany()
            .HasForeignKey(attachment => attachment.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attachment => attachment.OwnerUser)
            .WithMany()
            .HasForeignKey(attachment => attachment.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attachment => attachment.UploadedByUser)
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> builder)
    {
        builder.ToTable("file_objects");
        builder.ConfigureAuditableEntity();

        builder.Property(file => file.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(file => file.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(160).IsRequired();
        builder.Property(file => file.HashSha256).HasMaxLength(64);
        builder.Property(file => file.Classification).HasConversion<string>().HasMaxLength(40);
        builder.Property(file => file.SharingPolicy)
            .HasEnumStringConversion()
            .HasDefaultValue(FileSharingPolicy.Private)
            .IsRequired();
        builder.Property(file => file.SharingVersion).IsConcurrencyToken().HasDefaultValue(1L).IsRequired();
        builder.Property(file => file.Status).HasEnumStringConversion().IsRequired();
        builder.Property(file => file.DeleteReason).HasMaxLength(500);

        builder.HasIndex(file => file.WorkspaceId);
        builder.HasIndex(file => file.GroupId);
        builder.HasIndex(file => file.ProjectId);
        builder.HasIndex(file => file.UploadedByUserId);
        builder.HasIndex(file => file.Status);
        builder.HasIndex(file => file.Classification);
        builder.HasIndex(file => new { file.TenantId, file.WorkspaceId, file.SharingPolicy });
        builder.HasIndex(file => file.StorageKey).IsUnique();
        builder.HasIndex(file => new { file.TenantId, file.Status, file.CreatedAt });

        builder
            .HasOne(file => file.Workspace)
            .WithMany()
            .HasForeignKey(file => file.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(file => file.Group)
            .WithMany()
            .HasForeignKey(file => file.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(file => file.Project)
            .WithMany()
            .HasForeignKey(file => file.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(file => file.UploadedByUser)
            .WithMany()
            .HasForeignKey(file => file.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FileAccessGrantConfiguration : IEntityTypeConfiguration<FileAccessGrant>
{
    public void Configure(EntityTypeBuilder<FileAccessGrant> builder)
    {
        builder.ToTable("file_access_grants");
        builder.ConfigureAuditableEntity();

        builder.Property(grant => grant.RecipientKind).HasEnumStringConversion().IsRequired();
        builder.Property(grant => grant.RevokedAt);
        builder.Property(grant => grant.RevokedByUserId);

        builder.HasIndex(grant => new { grant.TenantId, grant.FileObjectId, grant.RecipientUserId })
            .HasFilter("\"RevokedAt\" IS NULL")
            .IsUnique();
        builder.HasIndex(grant => new { grant.TenantId, grant.WorkspaceId, grant.FileObjectId, grant.RevokedAt });
        builder.HasIndex(grant => new { grant.TenantId, grant.RecipientUserId, grant.RevokedAt });

        builder
            .HasOne(grant => grant.FileObject)
            .WithMany()
            .HasForeignKey(grant => grant.FileObjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(grant => grant.Workspace)
            .WithMany()
            .HasForeignKey(grant => grant.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(grant => grant.RecipientUser)
            .WithMany()
            .HasForeignKey(grant => grant.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(grant => grant.GrantedByUser)
            .WithMany()
            .HasForeignKey(grant => grant.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(grant => grant.RevokedByUser)
            .WithMany()
            .HasForeignKey(grant => grant.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FileDownloadGrantConfiguration : IEntityTypeConfiguration<FileDownloadGrant>
{
    public void Configure(EntityTypeBuilder<FileDownloadGrant> builder)
    {
        builder.ToTable("file_download_grants");
        builder.ConfigureAuditableEntity();

        builder.Property(grant => grant.TargetScopeType).HasEnumStringConversion().IsRequired();
        builder.Property(grant => grant.Classification).HasEnumStringConversion().IsRequired();
        builder.Property(grant => grant.AllowedOperation).HasMaxLength(40).IsRequired();
        builder.Property(grant => grant.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(grant => grant.PolicyStamp).HasMaxLength(128).IsRequired();
        builder.Property(grant => grant.Purpose).HasMaxLength(160);

        builder.HasIndex(grant => grant.ActorUserId);
        builder.HasIndex(grant => grant.WorkspaceId);
        builder.HasIndex(grant => grant.FileObjectId);
        builder.HasIndex(grant => grant.AttachmentId);
        builder.HasIndex(grant => new { grant.TargetScopeType, grant.TargetScopeId });
        builder.HasIndex(grant => grant.TokenHash);
        builder.HasIndex(grant => grant.ExpiresAt);
        builder.HasIndex(grant => grant.RevokedAt);
    }
}

public sealed class FileSelectionSnapshotConfiguration : IEntityTypeConfiguration<FileSelectionSnapshot>
{
    public void Configure(EntityTypeBuilder<FileSelectionSnapshot> builder)
    {
        builder.ToTable("file_selection_snapshots");
        builder.ConfigureAuditableEntity();

        builder.Property(snapshot => snapshot.NormalizedQuery).HasMaxLength(512).IsRequired();
        builder.Property(snapshot => snapshot.FileKind).HasMaxLength(32).IsRequired();
        builder.Property(snapshot => snapshot.ExpiresAt).IsRequired();
        builder.Property(snapshot => snapshot.ConsumptionVersion).IsConcurrencyToken();

        builder.HasIndex(snapshot => snapshot.ActorUserId);
        builder.HasIndex(snapshot => snapshot.WorkspaceId);
        builder.HasIndex(snapshot => snapshot.ExpiresAt);
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.ActorUserId, snapshot.ExpiresAt });

        builder.HasMany(snapshot => snapshot.Items)
            .WithOne(item => item.SelectionSnapshot)
            .HasForeignKey(item => item.SelectionSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FileSelectionSnapshotItemConfiguration : IEntityTypeConfiguration<FileSelectionSnapshotItem>
{
    public void Configure(EntityTypeBuilder<FileSelectionSnapshotItem> builder)
    {
        builder.ToTable("file_selection_snapshot_items");
        builder.HasKey(item => new { item.SelectionSnapshotId, item.FileObjectId });
        builder.HasIndex(item => item.FileObjectId);
    }
}

public sealed class FileScanResultConfiguration : IEntityTypeConfiguration<FileScanResult>
{
    public void Configure(EntityTypeBuilder<FileScanResult> builder)
    {
        builder.ToTable("file_scan_results");
        builder.ConfigureEntity();

        builder.Property(result => result.Status).HasEnumStringConversion().IsRequired();
        builder.Property(result => result.ScannerName).HasMaxLength(120).IsRequired();
        builder.Property(result => result.ResultSummary).HasMaxLength(2000);
        builder.Property(result => result.ScannedAt).IsRequired();

        builder.HasIndex(result => result.AttachmentId);
        builder.HasIndex(result => result.ScannedAt);

        builder
            .HasOne(result => result.Attachment)
            .WithMany(attachment => attachment.ScanResults)
            .HasForeignKey(result => result.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.ConfigureEntity();

        builder.Property(log => log.Action).HasMaxLength(160).IsRequired();
        builder.Property(log => log.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(log => log.IpAddress).HasMaxLength(80);
        builder.Property(log => log.UserAgent).HasMaxLength(500);
        builder.Property(log => log.Summary).HasMaxLength(2000);
        builder.Property(log => log.MetadataJson).HasColumnType("jsonb");
        builder.Property(log => log.CorrelationId).HasMaxLength(120);
        builder.Property(log => log.CreatedAt).IsRequired();

        builder.HasIndex(log => log.ActorUserId);
        builder.HasIndex(log => log.WorkspaceId);
        builder.HasIndex(log => log.GroupId);
        builder.HasIndex(log => log.ProjectId);
        builder.HasIndex(log => new { log.EntityType, log.EntityId });
        builder.HasIndex(log => log.Action);
        builder.HasIndex(log => log.CreatedAt);
        builder.HasIndex(log => new { log.TenantId, log.CreatedAt });
        builder.HasIndex(log => new { log.TenantId, log.Action });
        builder.HasIndex(log => new { log.TenantId, log.ActorUserId });

        builder
            .HasOne(log => log.ActorUser)
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(log => log.Workspace)
            .WithMany()
            .HasForeignKey(log => log.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(log => log.Group)
            .WithMany()
            .HasForeignKey(log => log.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(log => log.Project)
            .WithMany()
            .HasForeignKey(log => log.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("security_events");
        builder.ConfigureEntity();

        builder.Property(evt => evt.EventType).HasEnumStringConversion().IsRequired();
        builder.Property(evt => evt.Email).HasMaxLength(320);
        builder.Property(evt => evt.IpAddress).HasMaxLength(80);
        builder.Property(evt => evt.UserAgent).HasMaxLength(500);
        builder.Property(evt => evt.Severity).HasEnumStringConversion().IsRequired();
        builder.Property(evt => evt.Summary).HasMaxLength(2000).IsRequired();
        builder.Property(evt => evt.MetadataJson).HasColumnType("jsonb");
        builder.Property(evt => evt.CreatedAt).IsRequired();

        builder.HasIndex(evt => evt.EventType);
        builder.HasIndex(evt => evt.Severity);
        builder.HasIndex(evt => evt.UserId);
        builder.HasIndex(evt => evt.Email);
        builder.HasIndex(evt => evt.CreatedAt);
        builder.HasIndex(evt => new { evt.TenantId, evt.CreatedAt });
        builder.HasIndex(evt => new { evt.TenantId, evt.EventType });

        builder
            .HasOne(evt => evt.User)
            .WithMany()
            .HasForeignKey(evt => evt.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");
        builder.ConfigureEntity();

        builder.Property(setting => setting.Key).HasMaxLength(160).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(4000).IsRequired();
        builder.Property(setting => setting.ValueType).HasMaxLength(80).IsRequired();
        builder.Property(setting => setting.Description).HasMaxLength(1000);
        builder.Property(setting => setting.UpdatedAt).IsRequired();

        builder.HasIndex(setting => setting.Key).IsUnique();
        builder.HasIndex(setting => setting.UpdatedByUserId);
        builder.HasIndex(setting => setting.UpdatedAt);

        builder
            .HasOne(setting => setting.UpdatedByUser)
            .WithMany()
            .HasForeignKey(setting => setting.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FeatureModuleConfiguration : IEntityTypeConfiguration<FeatureModule>
{
    public void Configure(EntityTypeBuilder<FeatureModule> builder)
    {
        builder.ToTable("feature_modules");
        builder.ConfigureEntity();

        builder.Property(module => module.Key).HasMaxLength(120).IsRequired();
        builder.Property(module => module.Name).HasMaxLength(160).IsRequired();
        builder.Property(module => module.Description).HasMaxLength(1000);
        builder.Property(module => module.RequiredRole).HasConversion<string>().HasMaxLength(40);
        builder.Property(module => module.DefaultRoute).HasMaxLength(300).IsRequired();
        builder.Property(module => module.Icon).HasMaxLength(120);

        builder.HasIndex(module => module.Key).IsUnique();
        builder.HasIndex(module => module.SortOrder);
    }
}

public sealed class PanelDefinitionConfiguration : IEntityTypeConfiguration<PanelDefinition>
{
    public void Configure(EntityTypeBuilder<PanelDefinition> builder)
    {
        builder.ToTable("panel_definitions");
        builder.ConfigureEntity();

        builder.Property(panel => panel.Key).HasMaxLength(120).IsRequired();
        builder.Property(panel => panel.Name).HasMaxLength(160).IsRequired();
        builder.Property(panel => panel.Route).HasMaxLength(300).IsRequired();
        builder.Property(panel => panel.DefaultDockArea).HasEnumStringConversion().IsRequired();
        builder.Property(panel => panel.DefaultPosition).HasMaxLength(80).IsRequired();
        builder.Property(panel => panel.RequiredPermission).HasMaxLength(160);

        builder.HasIndex(panel => panel.FeatureModuleId);
        builder.HasIndex(panel => panel.Key).IsUnique();
        builder.HasIndex(panel => new { panel.FeatureModuleId, panel.SortOrder });

        builder
            .HasOne(panel => panel.FeatureModule)
            .WithMany(module => module.PanelDefinitions)
            .HasForeignKey(panel => panel.FeatureModuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserLayoutConfiguration : IEntityTypeConfiguration<UserLayout>
{
    public void Configure(EntityTypeBuilder<UserLayout> builder)
    {
        builder.ToTable("user_layouts");
        builder.ConfigureEntity();

        builder.Property(layout => layout.Name).HasMaxLength(160).IsRequired();
        builder.Property(layout => layout.LayoutJson).HasColumnType("jsonb").IsRequired();
        builder.Property(layout => layout.ScopeType).HasEnumStringConversion().IsRequired();
        builder.Property(layout => layout.CreatedAt).IsRequired();
        builder.Property(layout => layout.UpdatedAt).IsRequired();

        builder.HasIndex(layout => layout.UserId);
        builder.HasIndex(layout => layout.WorkspaceId);
        builder.HasIndex(layout => new { layout.UserId, layout.ScopeType, layout.ScopeId });
        builder.HasIndex(layout => new { layout.TenantId, layout.UserId, layout.WorkspaceId, layout.Name }).IsUnique();

        builder
            .HasOne(layout => layout.User)
            .WithMany()
            .HasForeignKey(layout => layout.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(layout => layout.Workspace)
            .WithMany()
            .HasForeignKey(layout => layout.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CommandDefinitionConfiguration : IEntityTypeConfiguration<CommandDefinition>
{
    public void Configure(EntityTypeBuilder<CommandDefinition> builder)
    {
        builder.ToTable("command_definitions");
        builder.ConfigureEntity();

        builder.Property(command => command.Key).HasMaxLength(120).IsRequired();
        builder.Property(command => command.Name).HasMaxLength(160).IsRequired();
        builder.Property(command => command.Description).HasMaxLength(1000);
        builder.Property(command => command.Icon).HasMaxLength(120);
        builder.Property(command => command.ActionType).HasEnumStringConversion().IsRequired();
        builder.Property(command => command.Route).HasMaxLength(300);
        builder.Property(command => command.HandlerKey).HasMaxLength(160);
        builder.Property(command => command.RequiredPermission).HasMaxLength(160);
        builder.Property(command => command.ContextType).HasEnumStringConversion().IsRequired();

        builder.HasIndex(command => command.FeatureModuleId);
        builder.HasIndex(command => command.Key).IsUnique();
        builder.HasIndex(command => new { command.ContextType, command.SortOrder });

        builder
            .HasOne(command => command.FeatureModule)
            .WithMany(module => module.CommandDefinitions)
            .HasForeignKey(command => command.FeatureModuleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class RadialMenuProfileConfiguration : IEntityTypeConfiguration<RadialMenuProfile>
{
    public void Configure(EntityTypeBuilder<RadialMenuProfile> builder)
    {
        builder.ToTable("radial_menu_profiles");
        builder.ConfigureAuditableEntity();

        builder.Property(profile => profile.Name).HasMaxLength(160).IsRequired();
        builder.Property(profile => profile.ProfileKey).HasMaxLength(120).IsRequired();
        builder.Property(profile => profile.ContextType).HasEnumStringConversion().IsRequired();
        builder.Property(profile => profile.Scope).HasEnumStringConversion().IsRequired();

        builder.HasIndex(profile => profile.UserId);
        builder.HasIndex(profile => profile.WorkspaceId);
        builder.HasIndex(profile => new { profile.TenantId, profile.ProfileKey }).IsUnique();
        builder.HasIndex(profile => new { profile.TenantId, profile.UserId, profile.WorkspaceId, profile.Name }).IsUnique();

        builder
            .HasOne(profile => profile.User)
            .WithMany()
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(profile => profile.Workspace)
            .WithMany()
            .HasForeignKey(profile => profile.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class RadialMenuItemConfiguration : IEntityTypeConfiguration<RadialMenuItem>
{
    public void Configure(EntityTypeBuilder<RadialMenuItem> builder)
    {
        builder.ToTable("radial_menu_items");
        builder.ConfigureEntity();

        builder.Property(item => item.Label).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Icon).HasMaxLength(120);
        builder.Property(item => item.Direction).HasEnumStringConversion().IsRequired();
        builder.Property(item => item.CommandKey).HasMaxLength(120).IsRequired();
        builder.Property(item => item.AngleDegrees).HasPrecision(6, 2);
        builder.Property(item => item.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(item => item.RadialMenuProfileId);
        builder.HasIndex(item => item.CommandDefinitionId);
        builder.HasIndex(item => item.ParentItemId);
        builder.HasIndex(item => new { item.RadialMenuProfileId, item.SortOrder });

        builder
            .HasOne(item => item.RadialMenuProfile)
            .WithMany(profile => profile.Items)
            .HasForeignKey(item => item.RadialMenuProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(item => item.CommandDefinition)
            .WithMany()
            .HasForeignKey(item => item.CommandDefinitionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(item => item.ParentItem)
            .WithMany(parent => parent.ChildItems)
            .HasForeignKey(item => item.ParentItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
