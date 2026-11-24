using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class SurveyFeedbackExportRequestConfiguration : IEntityTypeConfiguration<SurveyFeedbackExportRequest>
{
    public void Configure(EntityTypeBuilder<SurveyFeedbackExportRequest> builder)
    {
        builder.ToTable("survey_feedback_export_requests", table =>
            table.HasCheckConstraint("ck_survey_feedback_export_requests_mode", "\"Mode\"::text = ANY(ARRAY['aggregate'::text, 'detail'::text])"));
        builder.ConfigureAuditableEntity();
        builder.Property(item => item.Mode).HasMaxLength(16).IsRequired();
        builder.Property(item => item.ColumnsJson).HasMaxLength(240).IsRequired();
        builder.Property(item => item.SourceDigest).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ReceiptRequestId).HasMaxLength(128).IsRequired();
        builder.HasAlternateKey(item => new { item.Id, item.TenantId, item.RequesterId });
        builder.HasIndex(item => new { item.TenantId, item.RequesterId, item.ExpiresAtUtc });
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(item => new { item.PresentationId, item.TenantId })
            .HasPrincipalKey(item => new { item.Id, item.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(item => item.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyFeedbackExportGrantConfiguration : IEntityTypeConfiguration<SurveyFeedbackExportGrant>
{
    public void Configure(EntityTypeBuilder<SurveyFeedbackExportGrant> builder)
    {
        builder.ToTable("survey_feedback_export_grants");
        builder.ConfigureAuditableEntity();
        builder.Property(item => item.ReceiptRequestId).HasMaxLength(128).IsRequired();
        builder.HasIndex(item => new { item.TenantId, item.RecipientUserId, item.ExpiresAtUtc });
        builder.HasOne<SurveyFeedbackExportRequest>().WithMany()
            .HasForeignKey(item => new { item.ExportRequestId, item.TenantId, item.RecipientUserId })
            .HasPrincipalKey(item => new { item.Id, item.TenantId, item.RequesterId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
