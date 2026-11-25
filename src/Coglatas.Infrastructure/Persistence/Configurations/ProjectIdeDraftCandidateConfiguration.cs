using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

internal sealed class ProjectIdeDraftCandidateConfiguration : IEntityTypeConfiguration<ProjectIdeDraftCandidate>
{
    public void Configure(EntityTypeBuilder<ProjectIdeDraftCandidate> builder)
    {
        builder.ToTable("project_ide_draft_candidates", table =>
        {
            table.HasCheckConstraint("CK_project_ide_draft_candidate_digest",
                """ "SourceDigest" ~ '^[0-9a-f]{64}$' AND "InputDigest" ~ '^[0-9a-f]{64}$' """);
            table.HasCheckConstraint("CK_project_ide_draft_candidate_bytes",
                """ octet_length("SourceBytes") BETWEEN 1 AND 4194304 AND octet_length("InputBytes") BETWEEN 1 AND 4194304 """);
            table.HasCheckConstraint("CK_project_ide_draft_candidate_predecessor", """ "CandidateId" <> "PredecessorId" """);
        });
        builder.HasKey(row => row.CandidateId);
        builder.HasAlternateKey(row => new { row.TenantId, row.ProjectId, row.BranchId, row.ProposalId, row.CandidateId });
        builder.Property(row => row.SourceDigest).HasMaxLength(64);
        builder.Property(row => row.InputDigest).HasMaxLength(64);
        builder.HasIndex(row => new { row.TenantId, row.ProjectId, row.BranchId, row.ProposalId })
            .IsUnique().HasFilter(""" "PredecessorId" IS NULL """);
        builder.HasIndex(row => row.PredecessorId).IsUnique();
        builder.HasOne<ProjectIdeDraftCandidate>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.ProjectId, row.BranchId, row.ProposalId, row.PredecessorId })
            .HasPrincipalKey(row => new { row.TenantId, row.ProjectId, row.BranchId, row.ProposalId, row.CandidateId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(row => new { row.ProjectId, row.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
