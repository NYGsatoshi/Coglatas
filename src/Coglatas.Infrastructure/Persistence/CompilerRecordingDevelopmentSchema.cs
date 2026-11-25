using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Installs fixture-owned receipt persistence outside the application's migration path.</summary>
public static class CompilerRecordingDevelopmentSchema
{
    public static async Task ProvisionAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(context, cancellationToken);
        // ExecuteSqlRaw uses composite formatting for parameters, including when none are passed.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE projectide_development.projectide_compiler_recording_evidence (
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "RunId" uuid NOT NULL,
                "AttemptId" uuid NOT NULL,
                "ActorUserId" uuid NOT NULL,
                "RecordedAtUtc" timestamp with time zone NOT NULL,
                "Payload" jsonb NOT NULL,
                PRIMARY KEY ("TenantId", "ProjectId", "RunId"),
                UNIQUE ("TenantId", "ProjectId", "AttemptId"),
                FOREIGN KEY ("ProjectId", "TenantId") REFERENCES public.projects ("Id", "TenantId") ON DELETE RESTRICT,
                CONSTRAINT "CK_projectide_compiler_recording_evidence_binding" CHECK (
                    (jsonb_typeof("Payload") = 'object'
                    AND "Payload" - ARRAY['Source', 'SourceDigest', 'RunId', 'AttemptId', 'CompilerInputId',
                        'ExecutionInputDigest', 'MaterializationsDigest', 'ManifestDigest', 'ReceiptDigest', 'HostStatus',
                        'CaptureMode', 'OriginalCaptureState', 'CaptureDigest', 'OriginRunId', 'OriginReceiptDigest', 'Result'] = '{{}}'::jsonb
                    AND "Payload" ->> 'RunId' = "RunId"::text
                    AND "Payload" ->> 'AttemptId' = "AttemptId"::text
                    AND "Payload" -> 'Source' ->> 'TenantId' = "TenantId"::text
                    AND "Payload" -> 'Source' ->> 'ProjectId' = "ProjectId"::text
                    AND "Payload" ->> 'SourceDigest' ~ '^[0-9a-f]{{64}}$'
                    AND "Payload" ->> 'ManifestDigest' ~ '^[0-9a-f]{{64}}$'
                    AND "Payload" ->> 'ReceiptDigest' ~ '^[0-9a-f]{{64}}$') IS TRUE)
            );

            CREATE INDEX compiler_recording_evidence_project_scope
                ON projectide_development.projectide_compiler_recording_evidence ("ProjectId", "TenantId");

            CREATE FUNCTION projectide_development.prevent_compiler_recording_evidence_update()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Compiler recording evidence is immutable';
            END;
            $$;

            CREATE TRIGGER projectide_compiler_recording_evidence_immutable
                BEFORE UPDATE ON projectide_development.projectide_compiler_recording_evidence
                FOR EACH ROW EXECUTE FUNCTION projectide_development.prevent_compiler_recording_evidence_update();
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
