using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(activityEvent => activityEvent.Title).HasMaxLength(240).IsRequired();
        builder.Property(activityEvent => activityEvent.Description).HasMaxLength(4000);
        builder.Property(activityEvent => activityEvent.Location).HasMaxLength(500);
        builder.Property(activityEvent => activityEvent.BringItemsText).HasMaxLength(2000);
        builder.Property(activityEvent => activityEvent.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(activityEvent => activityEvent.WorkspaceId);
        builder.HasIndex(activityEvent => activityEvent.GroupId);
        builder.HasIndex(activityEvent => activityEvent.ProjectId);
        builder.HasIndex(activityEvent => activityEvent.CreatedByUserId);
        builder.HasIndex(activityEvent => activityEvent.Status);
        builder.HasIndex(activityEvent => activityEvent.StartsAt);
        builder.HasIndex(activityEvent => activityEvent.EndsAt);
        builder.HasIndex(activityEvent => activityEvent.AttendanceDeadline);

        builder
            .HasOne(activityEvent => activityEvent.Workspace)
            .WithMany()
            .HasForeignKey(activityEvent => activityEvent.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(activityEvent => activityEvent.Group)
            .WithMany()
            .HasForeignKey(activityEvent => activityEvent.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(activityEvent => activityEvent.Project)
            .WithMany()
            .HasForeignKey(activityEvent => activityEvent.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(activityEvent => activityEvent.CreatedByUser)
            .WithMany()
            .HasForeignKey(activityEvent => activityEvent.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EventAttendanceConfiguration : IEntityTypeConfiguration<EventAttendance>
{
    public void Configure(EntityTypeBuilder<EventAttendance> builder)
    {
        builder.ToTable("event_attendances");
        builder.ConfigureAuditableEntity();

        builder.Property(attendance => attendance.Status).HasEnumStringConversion().IsRequired();
        builder.Property(attendance => attendance.Comment).HasMaxLength(2000);

        builder.HasIndex(attendance => attendance.EventId);
        builder.HasIndex(attendance => attendance.UserId);
        builder.HasIndex(attendance => new { attendance.TenantId, attendance.EventId, attendance.UserId }).IsUnique();
        builder.HasIndex(attendance => attendance.Status);

        builder
            .HasOne(attendance => attendance.Event)
            .WithMany(activityEvent => activityEvent.Attendances)
            .HasForeignKey(attendance => attendance.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attendance => attendance.User)
            .WithMany()
            .HasForeignKey(attendance => attendance.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
