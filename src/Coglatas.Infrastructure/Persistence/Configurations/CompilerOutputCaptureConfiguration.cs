using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

internal sealed class CompilerOutputCaptureConfiguration : IEntityTypeConfiguration<CompilerOutputCaptureRow>
{
    public void Configure(EntityTypeBuilder<CompilerOutputCaptureRow> builder)
    {
        builder.ToTable("projectide_compiler_output_captures", table =>
            table.HasCheckConstraint("CK_projectide_compiler_output_capture_projection", $$"""
                (("DeletedAtUtc" IS NOT NULL AND "RunId" IS NULL AND "BindingDigest" IS NULL
                    AND "MaximumBodyBytes" IS NULL AND "Projection" IS NULL)
                OR ("DeletedAtUtc" IS NULL AND "RunId" IS NOT NULL AND "BindingDigest" ~ '^[0-9a-f]{64}$'
                    AND "MaximumBodyBytes" BETWEEN 1 AND {{ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes}}
                    AND jsonb_typeof("Projection") = 'object'
                    AND "Projection" - ARRAY['Mode', 'State', 'Execution', 'Coverage', 'Verdict', 'EvidenceComplete',
                        'SemanticModelWasCaptured', 'IrWasCaptured', 'SourceMapWasCaptured'] = '{}'::jsonb
                    AND jsonb_typeof("Projection" -> 'Mode') = 'number'
                    AND jsonb_typeof("Projection" -> 'State') = 'number'
                    AND jsonb_typeof("Projection" -> 'Execution') = 'number'
                    AND jsonb_typeof("Projection" -> 'Coverage') = 'number'
                    AND jsonb_typeof("Projection" -> 'Verdict') = 'number'
                    AND jsonb_typeof("Projection" -> 'EvidenceComplete') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'SemanticModelWasCaptured') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'IrWasCaptured') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'SourceMapWasCaptured') = 'boolean')) IS TRUE
                """));
        builder.HasKey(row => new { row.TenantId, row.ProjectId, row.CaptureId });
        builder.Property(row => row.Projection).HasColumnType("jsonb");
        builder.HasOne<CompilerRecordingEvidenceRow>().WithMany().HasForeignKey(row => new { row.TenantId, row.ProjectId, row.RunId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
