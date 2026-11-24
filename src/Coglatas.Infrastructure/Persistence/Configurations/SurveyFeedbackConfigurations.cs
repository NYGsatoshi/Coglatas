using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class FeedbackInvitationPolicyConfiguration : IEntityTypeConfiguration<FeedbackInvitationPolicy>
{
    public void Configure(EntityTypeBuilder<FeedbackInvitationPolicy> builder)
    {
        builder.ToTable("feedback_invitation_policies", table =>
            table.HasCheckConstraint("ck_feedback_invitation_policy_windows",
                "\"DecisionWindowSeconds\" > 0 AND \"MaximumActiveSeconds\" > 0"));
        builder.ConfigureAuditableEntity();
        builder.Property(policy => policy.VersionNo).IsConcurrencyToken();
        builder.HasIndex(policy => new { policy.TenantId, policy.EventId }).IsUnique();
        builder.HasOne<ActivityEvent>().WithMany().HasForeignKey(policy => new { policy.TenantId, policy.EventId })
            .HasPrincipalKey(activity => new { activity.TenantId, activity.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FeedbackViewerGrantConfiguration : IEntityTypeConfiguration<FeedbackViewerGrant>
{
    public void Configure(EntityTypeBuilder<FeedbackViewerGrant> builder)
    {
        builder.ToTable("feedback_viewer_grants");
        builder.ConfigureAuditableEntity();
        builder.Property(grant => grant.VersionNo).IsConcurrencyToken();
        builder.Property(grant => grant.TerminationReason).HasMaxLength(1000);
        builder.HasIndex(grant => new { grant.TenantId, grant.RecipientUserId, grant.PresentationId, grant.Status });
        builder.HasIndex(grant => new { grant.TenantId, grant.PresentationId, grant.RecipientUserId })
            .IsUnique().HasFilter("\"Status\" IN (0, 1)");
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(grant => new { grant.TenantId, grant.PresentationId })
            .HasPrincipalKey(presentation => new { presentation.TenantId, presentation.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeedbackInvitationPolicy>().WithMany()
            .HasForeignKey(grant => new { grant.TenantId, grant.InvitationPolicyId })
            .HasPrincipalKey(policy => new { policy.TenantId, policy.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(grant => grant.InviterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(grant => grant.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnswerVisibilityDecisionConfiguration : IEntityTypeConfiguration<AnswerVisibilityDecision>
{
    public void Configure(EntityTypeBuilder<AnswerVisibilityDecision> builder)
    {
        builder.ToTable("answer_visibility_decisions");
        builder.ConfigureAuditableEntity();
        builder.Property(decision => decision.VersionNo).IsConcurrencyToken();
        builder.Property(decision => decision.Reason).HasMaxLength(1000);
        builder.HasIndex(decision => new { decision.TenantId, decision.FormSubmissionValueId }).IsUnique();
        builder.HasOne<FormSubmissionValue>().WithMany()
            .HasForeignKey(decision => new { decision.TenantId, decision.FormSubmissionValueId })
            .HasPrincipalKey(answer => new { answer.TenantId, answer.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FeedbackReportCaseConfiguration : IEntityTypeConfiguration<FeedbackReportCase>
{
    public void Configure(EntityTypeBuilder<FeedbackReportCase> builder)
    {
        builder.ToTable("feedback_report_cases");
        builder.ConfigureAuditableEntity();
        builder.Property(report => report.VersionNo).IsConcurrencyToken();
        builder.Property(report => report.Reason).HasMaxLength(1000);
        builder.Property(report => report.DispositionReason).HasMaxLength(1000);
        builder.HasIndex(report => new { report.TenantId, report.PresentationId, report.Status });
        builder.HasOne<FormSubmissionValue>().WithMany()
            .HasForeignKey(report => new { report.TenantId, report.FormSubmissionValueId })
            .HasPrincipalKey(answer => new { answer.TenantId, answer.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(report => new { report.TenantId, report.PresentationId })
            .HasPrincipalKey(presentation => new { presentation.TenantId, presentation.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FeedbackReadStateConfiguration : IEntityTypeConfiguration<FeedbackReadState>
{
    public void Configure(EntityTypeBuilder<FeedbackReadState> builder)
    {
        builder.ToTable("feedback_read_states");
        builder.ConfigureAuditableEntity();
        builder.HasIndex(state => new { state.TenantId, state.UserId, state.FormSubmissionId }).IsUnique();
        builder.HasOne<FormSubmission>().WithMany().HasForeignKey(state => new { state.TenantId, state.FormSubmissionId })
            .HasPrincipalKey(submission => new { submission.TenantId, submission.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeacherPrivateNoteConfiguration : IEntityTypeConfiguration<TeacherPrivateNote>
{
    public void Configure(EntityTypeBuilder<TeacherPrivateNote> builder)
    {
        builder.ToTable("teacher_private_notes");
        builder.ConfigureAuditableEntity();
        builder.Property(note => note.Text).HasMaxLength(10000);
        builder.HasIndex(note => new { note.TenantId, note.PresentationId, note.AuthorUserId });
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(note => new { note.TenantId, note.PresentationId })
            .HasPrincipalKey(presentation => new { presentation.TenantId, presentation.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SharedTeacherFeedbackConfiguration : IEntityTypeConfiguration<SharedTeacherFeedback>
{
    public void Configure(EntityTypeBuilder<SharedTeacherFeedback> builder)
    {
        builder.ToTable("shared_teacher_feedback");
        builder.ConfigureAuditableEntity();
        builder.Property(note => note.Text).HasMaxLength(10000);
        builder.HasIndex(note => new { note.TenantId, note.PresentationId });
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(note => new { note.TenantId, note.PresentationId })
            .HasPrincipalKey(presentation => new { presentation.TenantId, presentation.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
