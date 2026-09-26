using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ArtifactClaimConfiguration : IEntityTypeConfiguration<ArtifactClaim>
{
    public void Configure(EntityTypeBuilder<ArtifactClaim> builder)
    {
        // These append-only audit-evidence tables are intentionally managed by
        // the explicit contract migration. Excluding them from automatic model
        // differ operations keeps later unrelated migrations from rewriting the
        // immutable evidence schema while the checked-in migration remains the
        // deployment owner.
        builder.ToTable("artifact_claims", table =>
        {
            table.ExcludeFromMigrations();
            table.HasCheckConstraint("CK_artifact_claims_ordinal", "\"Ordinal\" > 0");
        });
        builder.ConfigureAuditableEntity();
        builder.Property(claim => claim.LogicalClaimId).IsRequired();
        builder.Property(claim => claim.Text).HasMaxLength(4000).IsRequired();
        builder.Property(claim => claim.SupportStatus).HasEnumStringConversion().IsRequired();
        builder.Property(claim => claim.ReviewStatus).HasEnumStringConversion().IsRequired();
        builder.HasIndex(claim => new { claim.TenantId, claim.ArtifactVersionId, claim.Ordinal }).IsUnique();
        builder.HasIndex(claim => claim.ArtifactVersionId);
        builder.HasOne(claim => claim.ArtifactVersion)
            .WithMany()
            .HasForeignKey(claim => claim.ArtifactVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactReportDocumentConfiguration : IEntityTypeConfiguration<ArtifactReportDocument>
{
    public void Configure(EntityTypeBuilder<ArtifactReportDocument> builder)
    {
        builder.ToTable("artifact_report_documents", t => t.ExcludeFromMigrations());
        builder.ConfigureAuditableEntity();
        builder.Property(x => x.Title).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => x.ArtifactVersionId).IsUnique();
        builder.HasOne(x => x.ArtifactVersion).WithMany().HasForeignKey(x => x.ArtifactVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactReportSectionConfiguration : IEntityTypeConfiguration<ArtifactReportSection>
{
    public void Configure(EntityTypeBuilder<ArtifactReportSection> builder)
    {
        builder.ToTable("artifact_report_sections", t =>
        {
            t.ExcludeFromMigrations();
            t.HasCheckConstraint("CK_artifact_report_sections_ordinal", "\"Ordinal\" > 0");
        });
        builder.ConfigureAuditableEntity();
        builder.Property(x => x.Heading).HasMaxLength(512).IsRequired();
        builder.Property(x => x.BodyText).HasMaxLength(50000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ArtifactReportDocumentId, x.Ordinal }).IsUnique();
        builder.HasOne(x => x.Document).WithMany(x => x.Sections).HasForeignKey(x => x.ArtifactReportDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactReportCitationConfiguration : IEntityTypeConfiguration<ArtifactReportCitation>
{
    public void Configure(EntityTypeBuilder<ArtifactReportCitation> builder)
    {
        builder.ToTable("artifact_report_citations", t =>
        {
            t.ExcludeFromMigrations();
            t.HasCheckConstraint("CK_artifact_report_citations_anchor", "\"Ordinal\" > 0 AND \"AnchorStartUtf16\" >= 0 AND \"AnchorLengthUtf16\" > 0");
        });
        builder.ConfigureAuditableEntity();
        builder.HasIndex(x => new { x.TenantId, x.ArtifactReportSectionId, x.Ordinal }).IsUnique();
        builder.HasOne(x => x.Section).WithMany(x => x.Citations).HasForeignKey(x => x.ArtifactReportSectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Claim).WithMany().HasForeignKey(x => x.ArtifactClaimId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactEvidenceConfiguration : IEntityTypeConfiguration<ArtifactEvidence>
{
    public void Configure(EntityTypeBuilder<ArtifactEvidence> builder)
    {
        builder.ToTable("artifact_evidence", table =>
        {
            table.ExcludeFromMigrations();
            table.HasCheckConstraint("CK_artifact_evidence_ordinal", "\"Ordinal\" > 0");
        });
        builder.ConfigureAuditableEntity();
        builder.Property(evidence => evidence.SourceKind).HasEnumStringConversion().IsRequired();
        builder.Property(evidence => evidence.SourceReference).HasMaxLength(2048).IsRequired();
        builder.Property(evidence => evidence.SourceTitleSnapshot).HasMaxLength(512);
        builder.Property(evidence => evidence.SourcePublisherSnapshot).HasMaxLength(512);
        builder.Property(evidence => evidence.SourceTypeSnapshot).HasMaxLength(128);
        builder.Property(evidence => evidence.SourceClassification).HasEnumStringConversion().IsRequired();
        builder.Property(evidence => evidence.ContentHashSnapshot).HasMaxLength(256);
        builder.Property(evidence => evidence.SourceVersionSnapshot).HasMaxLength(256);
        builder.Property(evidence => evidence.VerificationStatus).HasEnumStringConversion().IsRequired();
        builder.Property(evidence => evidence.PassageSnapshot).HasMaxLength(4000).IsRequired();
        builder.Property(evidence => evidence.LocationSnapshot).HasMaxLength(512);
        builder.HasIndex(evidence => new { evidence.TenantId, evidence.ArtifactClaimId, evidence.Ordinal }).IsUnique();
        builder.HasIndex(evidence => evidence.ArtifactClaimId);
        builder.HasIndex(evidence => evidence.SourceEventAuditId);
        builder.HasOne(evidence => evidence.ArtifactClaim)
            .WithMany(claim => claim.Evidence)
            .HasForeignKey(evidence => evidence.ArtifactClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
