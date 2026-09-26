using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ArtifactFindingConfiguration : IEntityTypeConfiguration<ArtifactFinding>
{
    public void Configure(EntityTypeBuilder<ArtifactFinding> builder)
    {
        builder.ToTable("artifact_findings", table =>
        {
            table.ExcludeFromMigrations();
            table.HasCheckConstraint(
                "CK_artifact_findings_confidence",
                "\"ConfidencePercent\" >= 0 AND \"ConfidencePercent\" <= 100");
        });
        builder.ConfigureAuditableEntity();
        builder.Property(finding => finding.Severity).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(finding => finding.DetectorKey).HasMaxLength(128).IsRequired();
        builder.Property(finding => finding.PolicyVersion).HasMaxLength(128).IsRequired();
        builder.Property(finding => finding.Status).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(finding => finding.WorkflowStatus).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(finding => finding.DueDate).HasColumnType("date");
        builder.Property(finding => finding.ResolutionReason).HasMaxLength(1000);
        builder.HasIndex(finding => finding.ArtifactClaimId).IsUnique();
        builder.HasIndex(finding => new { finding.TenantId, finding.Status, finding.Severity });
        builder.HasIndex(finding => new { finding.TenantId, finding.WorkflowStatus, finding.DueDate, finding.OwnerUserId });
        builder.HasOne(finding => finding.ArtifactClaim)
            .WithOne(claim => claim.Finding)
            .HasForeignKey<ArtifactFinding>(finding => finding.ArtifactClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditFindingHistoryConfiguration : IEntityTypeConfiguration<AuditFindingHistory>
{
    public void Configure(EntityTypeBuilder<AuditFindingHistory> builder)
    {
        builder.ToTable("audit_finding_history", table => table.ExcludeFromMigrations());
        // The shared helper is intentionally constrained to non-nullable enums.
        // FromStatus is null for the initial Open history entry, so let EF Core's
        // built-in nullable enum-to-string converter preserve that null value.
        builder.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(history => history.ToStatus).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(history => history.Reason).HasMaxLength(1000);
        builder.HasIndex(history => new { history.TenantId, history.ArtifactFindingId, history.CreatedAt });
        builder.HasOne(history => history.Finding)
            .WithMany(finding => finding.History)
            .HasForeignKey(history => history.ArtifactFindingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditFindingWorkflowHistoryConfiguration : IEntityTypeConfiguration<AuditFindingWorkflowHistory>
{
    public void Configure(EntityTypeBuilder<AuditFindingWorkflowHistory> builder)
    {
        builder.ToTable("audit_finding_workflow_history", table => table.ExcludeFromMigrations());
        builder.ConfigureAuditableEntity();
        builder.Property(history => history.FromWorkflowStatus).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(history => history.ToWorkflowStatus).HasEnumStringConversion().HasMaxLength(32).IsRequired();
        builder.Property(history => history.FromDueDate).HasColumnType("date");
        builder.Property(history => history.ToDueDate).HasColumnType("date");
        builder.HasIndex(history => new { history.TenantId, history.ArtifactFindingId, history.CreatedAt });
        builder.HasOne(history => history.Finding)
            .WithMany(finding => finding.WorkflowHistory)
            .HasForeignKey(history => history.ArtifactFindingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
