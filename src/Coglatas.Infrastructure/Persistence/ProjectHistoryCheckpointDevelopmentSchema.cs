using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Provisions the immutable History checkpoint store for an explicitly composed development database.</summary>
public static class ProjectHistoryCheckpointDevelopmentSchema
{
    public static async Task ProvisionAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(context, cancellationToken);
        // ExecuteSqlRaw applies composite formatting before PostgreSQL sees the regular expressions.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE projectide_development.projectide_history_checkpoints (
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "BranchId" uuid NOT NULL,
                "RevisionId" uuid NOT NULL,
                "VersionId" uuid NOT NULL,
                "BranchSequence" bigint NOT NULL CHECK ("BranchSequence" >= 0),
                "Context" text NOT NULL CHECK (octet_length("Context") <= 4194304),
                "SourceDigest" text NOT NULL CHECK ("SourceDigest" ~ '^[a-f0-9]{{64}}$'),
                "InputDigest" text NOT NULL CHECK ("InputDigest" ~ '^[a-f0-9]{{64}}$'),
                "Parents" jsonb NOT NULL CHECK (jsonb_typeof("Parents") = 'array' AND jsonb_array_length("Parents") <= 8),
                "SourceBytes" bytea CHECK (octet_length("SourceBytes") <= 4194304),
                "InputBytes" bytea CHECK (octet_length("InputBytes") <= 4194304),
                "DeletedAtUtc" timestamp with time zone,
                PRIMARY KEY ("TenantId", "ProjectId", "BranchId", "RevisionId"),
                UNIQUE ("TenantId", "ProjectId", "BranchId", "BranchSequence"),
                CHECK (("DeletedAtUtc" IS NULL AND "SourceBytes" IS NOT NULL) OR
                       ("DeletedAtUtc" IS NOT NULL AND "SourceBytes" IS NULL AND "InputBytes" IS NULL))
            );

            CREATE FUNCTION projectide_development.protect_projectide_history_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF OLD."DeletedAtUtc" IS NOT NULL OR NEW."DeletedAtUtc" IS NULL OR
                    ROW(NEW."TenantId", NEW."ProjectId", NEW."BranchId", NEW."RevisionId", NEW."VersionId",
                        NEW."BranchSequence", NEW."Context", NEW."SourceDigest", NEW."InputDigest", NEW."Parents")
                    IS DISTINCT FROM
                    ROW(OLD."TenantId", OLD."ProjectId", OLD."BranchId", OLD."RevisionId", OLD."VersionId",
                        OLD."BranchSequence", OLD."Context", OLD."SourceDigest", OLD."InputDigest", OLD."Parents") THEN
                    RAISE EXCEPTION 'History checkpoints permit only a one-way tombstone' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER projectide_history_checkpoints_immutable BEFORE UPDATE ON projectide_development.projectide_history_checkpoints
                FOR EACH ROW EXECUTE FUNCTION projectide_development.protect_projectide_history_checkpoint();
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
