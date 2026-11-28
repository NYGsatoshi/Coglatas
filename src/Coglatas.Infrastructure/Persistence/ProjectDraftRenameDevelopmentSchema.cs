using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Explicit Draft Rename storage setup for isolated development databases.</summary>
public static class ProjectDraftRenameDevelopmentSchema
{
    public static async Task CreateAsync(AppDbContext database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(database, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE projectide_development.project_ide_draft_candidates (
                "CandidateId" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "BranchId" uuid NOT NULL,
                "ProposalId" uuid NOT NULL,
                "PredecessorId" uuid UNIQUE,
                "SourceBytes" bytea NOT NULL,
                "InputBytes" bytea NOT NULL,
                "SourceDigest" varchar(64) NOT NULL,
                "InputDigest" varchar(64) NOT NULL,
                CONSTRAINT draft_candidate_scope UNIQUE ("TenantId", "ProjectId", "BranchId", "ProposalId", "CandidateId"),
                CONSTRAINT draft_candidate_parent FOREIGN KEY ("TenantId", "ProjectId", "BranchId", "ProposalId", "PredecessorId")
                    REFERENCES projectide_development.project_ide_draft_candidates
                        ("TenantId", "ProjectId", "BranchId", "ProposalId", "CandidateId") ON DELETE RESTRICT,
                CONSTRAINT draft_candidate_project FOREIGN KEY ("ProjectId", "TenantId")
                    REFERENCES public.projects ("Id", "TenantId") ON DELETE RESTRICT,
                CONSTRAINT draft_candidate_digest CHECK ("SourceDigest" ~ '^[0-9a-f]{{64}}$' AND "InputDigest" ~ '^[0-9a-f]{{64}}$'),
                CONSTRAINT draft_candidate_byte_bound CHECK (
                    octet_length("SourceBytes") BETWEEN 1 AND 4194304 AND octet_length("InputBytes") BETWEEN 1 AND 4194304),
                CONSTRAINT draft_candidate_distinct_predecessor CHECK ("CandidateId" <> "PredecessorId")
            );

            CREATE UNIQUE INDEX draft_candidate_root ON projectide_development.project_ide_draft_candidates
                ("TenantId", "ProjectId", "BranchId", "ProposalId") WHERE "PredecessorId" IS NULL;
            CREATE INDEX draft_candidate_project_scope ON projectide_development.project_ide_draft_candidates ("ProjectId", "TenantId");
            CREATE INDEX draft_candidate_parent_scope ON projectide_development.project_ide_draft_candidates
                ("TenantId", "ProjectId", "BranchId", "ProposalId", "PredecessorId");

            CREATE FUNCTION projectide_development.forbid_draft_candidate_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'ProjectIDE Draft candidates are immutable';
            END;
            $$;
            CREATE TRIGGER project_ide_draft_candidate_immutable
                BEFORE UPDATE ON projectide_development.project_ide_draft_candidates
                FOR EACH ROW EXECUTE FUNCTION projectide_development.forbid_draft_candidate_update();
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
