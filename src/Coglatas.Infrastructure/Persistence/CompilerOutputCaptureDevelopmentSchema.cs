using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Explicitly provisions the redacted capture table after the development Compiler evidence table.</summary>
public static class CompilerOutputCaptureDevelopmentSchema
{
    // ExecuteSqlRawAsync treats SQL text as a composite format string.
    private static readonly string SetupSql = $$$"""
        CREATE TABLE IF NOT EXISTS projectide_development.projectide_compiler_output_captures (
            "TenantId" uuid NOT NULL,
            "ProjectId" uuid NOT NULL,
            "CaptureId" uuid NOT NULL,
            "RunId" uuid,
            "BindingDigest" text,
            "CapturedAtUtc" timestamptz NOT NULL,
            "DeletedAtUtc" timestamptz,
            "MaximumBodyBytes" integer,
            "Projection" jsonb,
            CONSTRAINT pk_projectide_compiler_output_captures PRIMARY KEY ("TenantId", "ProjectId", "CaptureId"),
            CONSTRAINT fk_compiler_output_captures_recording FOREIGN KEY ("TenantId", "ProjectId", "RunId")
                REFERENCES projectide_development.projectide_compiler_recording_evidence ("TenantId", "ProjectId", "RunId") ON DELETE RESTRICT,
            CONSTRAINT ck_projectide_compiler_output_capture_projection CHECK (
                (("DeletedAtUtc" IS NOT NULL AND "RunId" IS NULL AND "BindingDigest" IS NULL
                    AND "MaximumBodyBytes" IS NULL AND "Projection" IS NULL)
                OR ("DeletedAtUtc" IS NULL AND "RunId" IS NOT NULL AND "BindingDigest" ~ '^[0-9a-f]{{64}}$'
                    AND "MaximumBodyBytes" BETWEEN 1 AND {{{ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes}}}
                    AND jsonb_typeof("Projection") = 'object'
                    AND "Projection" - ARRAY['Mode', 'State', 'Execution', 'Coverage', 'Verdict', 'EvidenceComplete',
                        'SemanticModelWasCaptured', 'IrWasCaptured', 'SourceMapWasCaptured'] = '{{}}'::jsonb
                    AND jsonb_typeof("Projection" -> 'Mode') = 'number'
                    AND jsonb_typeof("Projection" -> 'State') = 'number'
                    AND jsonb_typeof("Projection" -> 'Execution') = 'number'
                    AND jsonb_typeof("Projection" -> 'Coverage') = 'number'
                    AND jsonb_typeof("Projection" -> 'Verdict') = 'number'
                    AND jsonb_typeof("Projection" -> 'EvidenceComplete') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'SemanticModelWasCaptured') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'IrWasCaptured') = 'boolean'
                    AND jsonb_typeof("Projection" -> 'SourceMapWasCaptured') = 'boolean')) IS TRUE
            )
        );

        CREATE INDEX IF NOT EXISTS ix_compiler_output_captures_recording
            ON projectide_development.projectide_compiler_output_captures ("TenantId", "ProjectId", "RunId");

        CREATE OR REPLACE FUNCTION projectide_development.guard_projectide_compiler_output_capture()
        RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF OLD."DeletedAtUtc" IS NOT NULL OR NEW."DeletedAtUtc" IS NULL
                OR OLD."TenantId" <> NEW."TenantId" OR OLD."ProjectId" <> NEW."ProjectId"
                OR OLD."CaptureId" <> NEW."CaptureId" OR OLD."CapturedAtUtc" <> NEW."CapturedAtUtc" THEN
                RAISE EXCEPTION 'Compiler output capture can only be tombstoned';
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE OR REPLACE TRIGGER projectide_compiler_output_capture_immutable
            BEFORE UPDATE ON projectide_development.projectide_compiler_output_captures
            FOR EACH ROW EXECUTE FUNCTION projectide_development.guard_projectide_compiler_output_capture();
        """;

    public static async Task ProvisionAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync(SetupSql, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
