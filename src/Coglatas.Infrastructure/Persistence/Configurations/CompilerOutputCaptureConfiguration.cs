using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

internal sealed class CompilerOutputCaptureConfiguration : IEntityTypeConfiguration<CompilerOutputCaptureRow>
{
    public void Configure(EntityTypeBuilder<CompilerOutputCaptureRow> builder)
    {
        builder.ToTable("projectide_compiler_output_captures", ProjectIdeDevelopmentSchema.Name, table => table.ExcludeFromMigrations());
        builder.HasKey(row => new { row.TenantId, row.ProjectId, row.CaptureId });
        builder.Property(row => row.Projection).HasColumnType("jsonb");
        builder.HasOne<CompilerRecordingEvidenceRow>().WithMany().HasForeignKey(row => new { row.TenantId, row.ProjectId, row.RunId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
