using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

internal sealed class CompilerRecordingEvidenceConfiguration : IEntityTypeConfiguration<CompilerRecordingEvidenceRow>
{
    public void Configure(EntityTypeBuilder<CompilerRecordingEvidenceRow> builder)
    {
        builder.ToTable("projectide_compiler_recording_evidence", table =>
            table.HasCheckConstraint("CK_projectide_compiler_recording_evidence_binding", """
                (jsonb_typeof("Payload") = 'object'
                AND "Payload" - ARRAY['Source', 'SourceDigest', 'RunId', 'AttemptId', 'CompilerInputId',
                    'ExecutionInputDigest', 'MaterializationsDigest', 'ManifestDigest', 'ReceiptDigest', 'HostStatus',
                    'CaptureMode', 'OriginalCaptureState', 'CaptureDigest', 'OriginRunId', 'OriginReceiptDigest', 'Result'] = '{}'::jsonb
                AND "Payload" ->> 'RunId' = "RunId"::text
                AND "Payload" ->> 'AttemptId' = "AttemptId"::text
                AND "Payload" -> 'Source' ->> 'TenantId' = "TenantId"::text
                AND "Payload" -> 'Source' ->> 'ProjectId' = "ProjectId"::text
                AND "Payload" ->> 'SourceDigest' ~ '^[0-9a-f]{64}$'
                AND "Payload" ->> 'ManifestDigest' ~ '^[0-9a-f]{64}$'
                AND "Payload" ->> 'ReceiptDigest' ~ '^[0-9a-f]{64}$') IS TRUE
                """));
        builder.HasKey(row => new { row.TenantId, row.ProjectId, row.RunId });
        builder.HasIndex(row => new { row.TenantId, row.ProjectId, row.AttemptId }).IsUnique();
        builder.Property(row => row.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(row => new { row.ProjectId, row.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
