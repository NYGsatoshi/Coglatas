using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class PresentationConfiguration : IEntityTypeConfiguration<Presentation>
{
    public void Configure(EntityTypeBuilder<Presentation> builder)
    {
        builder.ToTable("presentations", table =>
            table.HasCheckConstraint("CK_presentations_time", "\"StartsAtUtc\" < \"EndsAtUtc\""));
        builder.ConfigureEntity();
        builder.Property(presentation => presentation.Title).HasMaxLength(240).IsRequired();
        builder.Property(presentation => presentation.Venue).HasMaxLength(500);
        builder.Property(presentation => presentation.VersionNo).IsConcurrencyToken();
        builder.HasAlternateKey(presentation => new { presentation.Id, presentation.TenantId });
        builder.HasAlternateKey(presentation => new { presentation.Id, presentation.TenantId, presentation.EventId });

        builder.HasOne<ActivityEvent>().WithMany().HasForeignKey(presentation => new { presentation.EventId, presentation.TenantId })
            .HasPrincipalKey(activityEvent => new { activityEvent.Id, activityEvent.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(presentation => new { presentation.ProjectId, presentation.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PresentationParticipantConfiguration : IEntityTypeConfiguration<PresentationParticipant>
{
    public void Configure(EntityTypeBuilder<PresentationParticipant> builder)
    {
        builder.ToTable("presentation_participants", table =>
            table.HasCheckConstraint("CK_presentation_participants_subject", """num_nonnulls("UserId", "PendingRosterRef") = 1"""));
        builder.ConfigureEntity();
        builder.Property(participant => participant.Role).HasEnumStringConversion().IsRequired();
        builder.Property(participant => participant.PendingRosterRef).HasMaxLength(240);
        builder.HasIndex(participant => new { participant.TenantId, participant.PresentationId, participant.PendingRosterRef, participant.Role }).IsUnique()
            .HasFilter(""" "PendingRosterRef" IS NOT NULL """);
        builder.HasIndex(participant => new { participant.TenantId, participant.PresentationId, participant.UserId, participant.Role }).IsUnique();
        builder.HasIndex(participant => new { participant.TenantId, participant.UserId, participant.Role });
        builder.HasOne<Presentation>().WithMany().HasForeignKey(participant => new { participant.PresentationId, participant.TenantId })
            .HasPrincipalKey(presentation => new { presentation.Id, presentation.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TenantUser>().WithMany().HasForeignKey(participant => new { participant.TenantId, participant.UserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EventFeedbackSettingsConfiguration : IEntityTypeConfiguration<EventFeedbackSettings>
{
    public void Configure(EntityTypeBuilder<EventFeedbackSettings> builder)
    {
        builder.ToTable("event_feedback_settings", table =>
            table.HasCheckConstraint("CK_event_feedback_settings_window", "\"StartsAtUtc\" < \"EndsAtUtc\""));
        builder.ConfigureEntity();
        builder.Property(settings => settings.NameMode).HasEnumStringConversion().IsRequired();
        builder.Property(settings => settings.AccountBinding).HasEnumStringConversion().IsRequired();
        builder.Property(settings => settings.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(settings => settings.VersionNo).IsConcurrencyToken();
        builder.HasIndex(settings => new { settings.TenantId, settings.EventId }).IsUnique();
        builder.HasOne<ActivityEvent>().WithMany().HasForeignKey(settings => new { settings.EventId, settings.TenantId })
            .HasPrincipalKey(activityEvent => new { activityEvent.Id, activityEvent.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PresentationFeedbackSettingsConfiguration : IEntityTypeConfiguration<PresentationFeedbackSettings>
{
    public void Configure(EntityTypeBuilder<PresentationFeedbackSettings> builder)
    {
        builder.ToTable("presentation_feedback_settings");
        builder.ConfigureEntity();
        builder.Property(settings => settings.VersionNo).IsConcurrencyToken();
        builder.HasIndex(settings => new { settings.TenantId, settings.PresentationId }).IsUnique();
        builder.HasOne<Presentation>().WithMany().HasForeignKey(settings => new { settings.PresentationId, settings.TenantId })
            .HasPrincipalKey(presentation => new { presentation.Id, presentation.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
