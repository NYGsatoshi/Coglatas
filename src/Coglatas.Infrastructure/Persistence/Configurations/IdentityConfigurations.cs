using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.SystemRole).HasEnumStringConversion().IsRequired();
        builder.Property(user => user.Status).HasEnumStringConversion().IsRequired();
        builder.Property(user => user.FailedLoginAttempts).IsRequired();

        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.HasIndex(user => user.SystemRole);
        builder.HasIndex(user => user.Status);
        builder.HasIndex(user => user.LockoutEndAt);
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.ConfigureAuditableEntity();

        builder.Property(session => session.SessionKeyHash).HasMaxLength(512).IsRequired();
        builder.Property(session => session.ExpiresAt).IsRequired();

        builder.HasIndex(session => session.UserId);
        builder.HasIndex(session => session.ExpiresAt);
        builder.HasIndex(session => session.RevokedAt);

        builder
            .HasOne(session => session.User)
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("invites");
        builder.ConfigureAuditableEntity();

        builder.Property(invite => invite.Email).HasMaxLength(320).IsRequired();
        builder.Property(invite => invite.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(invite => invite.TokenHash).HasMaxLength(512).IsRequired();
        builder.Property(invite => invite.Role).HasEnumStringConversion().IsRequired();

        builder.HasIndex(invite => invite.WorkspaceId);
        builder.HasIndex(invite => invite.NormalizedEmail);
        builder.HasIndex(invite => invite.TokenHash).IsUnique();
        builder.HasIndex(invite => invite.ExpiresAt);
        builder.HasIndex(invite => invite.AcceptedAt);
        builder.HasIndex(invite => invite.RevokedAt);

        builder
            .HasOne(invite => invite.Workspace)
            .WithMany()
            .HasForeignKey(invite => invite.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(invite => invite.InvitedByUser)
            .WithMany()
            .HasForeignKey(invite => invite.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
