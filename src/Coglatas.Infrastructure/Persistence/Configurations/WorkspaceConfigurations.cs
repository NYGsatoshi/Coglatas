using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(workspace => workspace.Name).HasMaxLength(160).IsRequired();
        builder.Property(workspace => workspace.Slug).HasMaxLength(120).IsRequired();
        builder.Property(workspace => workspace.Description).HasMaxLength(2000);
        builder.Property(workspace => workspace.Icon).HasMaxLength(120);
        builder.Property(workspace => workspace.TimeZone).HasMaxLength(80);
        builder.Property(workspace => workspace.DefaultTaskDeadlineDigestLocalTime)
            .HasColumnType("time without time zone")
            .HasDefaultValue(new TimeOnly(8, 0))
            .IsRequired();
        builder.Property(workspace => workspace.TaskNotificationSettingsVersion)
            .HasDefaultValue(1L)
            .IsRequired();
        builder.Property(workspace => workspace.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(workspace => new { workspace.TenantId, workspace.Slug }).IsUnique();
        builder.HasIndex(workspace => new { workspace.TenantId, workspace.Status });
        builder.HasIndex(workspace => new { workspace.TenantId, workspace.CreatedAt });
        builder.HasIndex(workspace => workspace.Status);
        builder.HasIndex(workspace => workspace.CreatedByUserId);

        builder
            .HasOne(workspace => workspace.CreatedByUser)
            .WithMany()
            .HasForeignKey(workspace => workspace.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");
        builder.ConfigureAuditableEntity();

        builder.Property(member => member.Role).HasEnumStringConversion().IsRequired();
        builder.Property(member => member.Status).HasEnumStringConversion().IsRequired();
        builder.Property(member => member.TaskDeadlineDigestLocalTime)
            .HasColumnType("time without time zone");
        builder.Property(member => member.TaskNotificationPreferenceVersion)
            .HasDefaultValue(1L)
            .IsRequired();

        builder.HasIndex(member => new { member.TenantId, member.WorkspaceId, member.UserId }).IsUnique();
        builder.HasIndex(member => new { member.WorkspaceId, member.Role });
        builder.HasIndex(member => member.UserId);

        builder
            .HasOne(member => member.Workspace)
            .WithMany(workspace => workspace.Members)
            .HasForeignKey(member => member.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(group => group.Name).HasMaxLength(160).IsRequired();
        builder.Property(group => group.Slug).HasMaxLength(120).IsRequired();
        builder.Property(group => group.Description).HasMaxLength(2000);
        builder.Property(group => group.GroupType).HasEnumStringConversion().IsRequired();
        builder.Property(group => group.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(group => group.WorkspaceId);
        builder.HasIndex(group => group.ParentGroupId);
        builder.HasIndex(group => group.CreatedByUserId);
        builder.HasIndex(group => group.Status);
        builder.HasIndex(group => new { group.TenantId, group.WorkspaceId, group.Slug }).IsUnique();
        builder.HasIndex(group => new { group.TenantId, group.WorkspaceId });
        builder.HasIndex(group => new { group.TenantId, group.Status });

        builder
            .HasOne(group => group.Workspace)
            .WithMany(workspace => workspace.Groups)
            .HasForeignKey(group => group.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(group => group.ParentGroup)
            .WithMany(parent => parent.ChildGroups)
            .HasForeignKey(group => group.ParentGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(group => group.CreatedByUser)
            .WithMany()
            .HasForeignKey(group => group.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");
        builder.ConfigureAuditableEntity();

        builder.Property(member => member.Role).HasEnumStringConversion().IsRequired();
        builder.Property(member => member.JoinedAt).IsRequired();

        builder.HasIndex(member => new { member.TenantId, member.GroupId, member.UserId }).IsUnique();
        builder.HasIndex(member => member.UserId);

        builder
            .HasOne(member => member.Group)
            .WithMany(group => group.Members)
            .HasForeignKey(member => member.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
