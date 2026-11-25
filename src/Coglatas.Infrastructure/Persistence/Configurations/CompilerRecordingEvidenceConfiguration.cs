using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

internal sealed class CompilerRecordingEvidenceConfiguration : IEntityTypeConfiguration<CompilerRecordingEvidenceRow>
{
    public void Configure(EntityTypeBuilder<CompilerRecordingEvidenceRow> builder)
    {
        builder.ToTable("projectide_compiler_recording_evidence", ProjectIdeDevelopmentSchema.Name, table => table.ExcludeFromMigrations());
        builder.HasKey(row => new { row.TenantId, row.ProjectId, row.RunId });
        builder.HasIndex(row => new { row.TenantId, row.ProjectId, row.AttemptId }).IsUnique();
        builder.Property(row => row.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(row => new { row.ProjectId, row.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
