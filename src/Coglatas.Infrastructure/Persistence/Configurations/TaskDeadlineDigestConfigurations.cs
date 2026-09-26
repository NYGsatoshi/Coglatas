using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class TaskDeadlineDigestJobConfiguration : IEntityTypeConfiguration<TaskDeadlineDigestJob>
{
    public void Configure(EntityTypeBuilder<TaskDeadlineDigestJob> builder)
    {
        builder.ToTable("task_deadline_digest_jobs", table =>
        {
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_policy_version",
                "\"PolicyVersion\" > 0");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_attempt_counts",
                "\"AttemptCount\" >= 0 AND \"AutomaticAttemptCount\" >= 0 AND \"AutomaticAttemptCount\" <= 3 AND \"AutomaticAttemptCount\" <= \"AttemptCount\" AND \"AttemptSequence\" >= 0");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_failed_after_three",
                "\"Status\" <> 'Failed' OR \"AutomaticAttemptCount\" = 3");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_claim_fields",
                "(\"Status\" = 'Claimed' AND \"ClaimOwner\" IS NOT NULL AND \"ClaimToken\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"ClaimExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Claimed' AND \"ClaimOwner\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimedAt\" IS NULL AND \"ClaimExpiresAt\" IS NULL)");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_claim_expiry",
                "\"ClaimExpiresAt\" IS NULL OR \"ClaimExpiresAt\" > \"ClaimedAt\"");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_completion",
                "((\"Status\" IN ('Succeeded', 'Failed')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_jobs_next_attempt",
                "\"Status\" <> 'Pending' OR \"NextAttemptAt\" IS NOT NULL");
        });
        builder.ConfigureAuditableEntity();

        builder.Property(job => job.LocalDate).IsRequired();
        builder.Property(job => job.PolicyVersion).IsRequired();
        builder.Property(job => job.Status)
            .HasEnumStringConversion()
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(job => job.AttemptCount).IsRequired();
        builder.Property(job => job.AutomaticAttemptCount).IsRequired();
        builder.Property(job => job.AttemptSequence).IsRequired();
        builder.Property(job => job.ScheduledForUtc).IsRequired();
        builder.Property(job => job.ClaimOwner).HasMaxLength(160);
        builder.Property(job => job.ClaimToken).IsConcurrencyToken();
        builder.Property(job => job.LastErrorCode).HasMaxLength(100);

        builder.HasIndex(job => new
            {
                job.TenantId,
                job.WorkspaceId,
                job.UserId,
                job.LocalDate,
                job.PolicyVersion
            })
            .HasDatabaseName("IX_task_deadline_digest_jobs_identity")
            .IsUnique();
        builder.HasIndex(job => new { job.TenantId, job.NextAttemptAt, job.CreatedAt, job.Id })
            .HasDatabaseName("IX_task_deadline_digest_jobs_due")
            .HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(job => new { job.TenantId, job.ClaimExpiresAt, job.Id })
            .HasDatabaseName("IX_task_deadline_digest_jobs_claim_expiry")
            .HasFilter("\"Status\" = 'Claimed'");

        builder
            .HasOne(job => job.Workspace)
            .WithMany()
            .HasForeignKey(job => job.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(job => job.User)
            .WithMany()
            .HasForeignKey(job => job.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(job => job.Notification)
            .WithMany()
            .HasForeignKey(job => job.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskDeadlineDigestAttemptConfiguration : IEntityTypeConfiguration<TaskDeadlineDigestAttempt>
{
    public void Configure(EntityTypeBuilder<TaskDeadlineDigestAttempt> builder)
    {
        builder.ToTable("task_deadline_digest_attempts", table =>
        {
            table.HasCheckConstraint(
                "CK_task_deadline_digest_attempts_number",
                "\"AttemptNumber\" > 0");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_attempts_restart",
                "(\"Trigger\" = 'Automatic' AND \"RestartedFromAttemptId\" IS NULL AND \"RequestedByUserId\" IS NULL) OR (\"Trigger\" = 'OperatorRestart' AND \"RestartedFromAttemptId\" IS NOT NULL AND \"RequestedByUserId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_attempts_claim_fields",
                "(\"Status\" = 'Claimed' AND \"ClaimOwner\" IS NOT NULL AND \"ClaimToken\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"ClaimExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Claimed' AND \"ClaimOwner\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimedAt\" IS NULL AND \"ClaimExpiresAt\" IS NULL)");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_attempts_claim_expiry",
                "\"ClaimExpiresAt\" IS NULL OR \"ClaimExpiresAt\" > \"ClaimedAt\"");
            table.HasCheckConstraint(
                "CK_task_deadline_digest_attempts_completion",
                "((\"Status\" IN ('Succeeded', 'Failed', 'Expired', 'Deferred')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");
        });
        builder.ConfigureAuditableEntity();

        builder.Property(attempt => attempt.AttemptNumber).IsRequired();
        builder.Property(attempt => attempt.Trigger).HasEnumStringConversion().IsRequired();
        builder.Property(attempt => attempt.Status)
            .HasEnumStringConversion()
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(attempt => attempt.ClaimOwner).HasMaxLength(160);
        builder.Property(attempt => attempt.ClaimToken).IsConcurrencyToken();
        builder.Property(attempt => attempt.LastErrorCode).HasMaxLength(100);

        builder.HasIndex(attempt => new { attempt.JobId, attempt.AttemptNumber })
            .HasDatabaseName("IX_task_deadline_digest_attempts_job_number")
            .IsUnique();
        builder.HasIndex(attempt => attempt.JobId)
            .HasDatabaseName("IX_task_deadline_digest_attempts_one_active")
            .HasFilter("\"Status\" IN ('Pending', 'Claimed')")
            .IsUnique();

        builder
            .HasOne(attempt => attempt.Job)
            .WithMany(job => job.Attempts)
            .HasForeignKey(attempt => attempt.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attempt => attempt.RestartedFromAttempt)
            .WithMany(attempt => attempt.RestartAttempts)
            .HasForeignKey(attempt => attempt.RestartedFromAttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(attempt => attempt.RequestedByUser)
            .WithMany()
            .HasForeignKey(attempt => attempt.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
