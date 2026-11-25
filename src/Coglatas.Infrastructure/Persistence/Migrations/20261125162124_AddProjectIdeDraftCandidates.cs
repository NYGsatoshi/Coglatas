using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdeDraftCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_ide_draft_candidates",
                columns: table => new
                {
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    InputBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    SourceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_ide_draft_candidates", x => x.CandidateId);
                    table.UniqueConstraint("AK_project_ide_draft_candidates_TenantId_ProjectId_BranchId_Pr~", x => new { x.TenantId, x.ProjectId, x.BranchId, x.ProposalId, x.CandidateId });
                    table.CheckConstraint("CK_project_ide_draft_candidate_bytes", " octet_length(\"SourceBytes\") BETWEEN 1 AND 4194304 AND octet_length(\"InputBytes\") BETWEEN 1 AND 4194304 ");
                    table.CheckConstraint("CK_project_ide_draft_candidate_digest", " \"SourceDigest\" ~ '^[0-9a-f]{64}$' AND \"InputDigest\" ~ '^[0-9a-f]{64}$' ");
                    table.CheckConstraint("CK_project_ide_draft_candidate_predecessor", " \"CandidateId\" <> \"PredecessorId\" ");
                    table.ForeignKey(
                        name: "FK_project_ide_draft_candidates_project_ide_draft_candidates_T~",
                        columns: x => new { x.TenantId, x.ProjectId, x.BranchId, x.ProposalId, x.PredecessorId },
                        principalTable: "project_ide_draft_candidates",
                        principalColumns: new[] { "TenantId", "ProjectId", "BranchId", "ProposalId", "CandidateId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_ide_draft_candidates_projects_ProjectId_TenantId",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_ide_draft_candidates_PredecessorId",
                table: "project_ide_draft_candidates",
                column: "PredecessorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_ide_draft_candidates_ProjectId_TenantId",
                table: "project_ide_draft_candidates",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_ide_draft_candidates_TenantId_ProjectId_BranchId_P~1",
                table: "project_ide_draft_candidates",
                columns: new[] { "TenantId", "ProjectId", "BranchId", "ProposalId", "PredecessorId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_ide_draft_candidates_TenantId_ProjectId_BranchId_Pr~",
                table: "project_ide_draft_candidates",
                columns: new[] { "TenantId", "ProjectId", "BranchId", "ProposalId" },
                unique: true,
                filter: " \"PredecessorId\" IS NULL ");

            migrationBuilder.Sql("""
                CREATE FUNCTION forbid_project_ide_draft_candidate_update() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ProjectIDE Draft candidates are immutable';
                END;
                $$;
                CREATE TRIGGER project_ide_draft_candidate_immutable
                    BEFORE UPDATE ON project_ide_draft_candidates
                    FOR EACH ROW EXECUTE FUNCTION forbid_project_ide_draft_candidate_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_ide_draft_candidates");
            migrationBuilder.Sql("DROP FUNCTION forbid_project_ide_draft_candidate_update();");
        }
    }
}
