using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260901093500_AddAuditFindingStructuredDecisions")]
public sealed class AddAuditFindingStructuredDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_finding_decisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PreviousDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ReviewerDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_finding_decisions", x => x.Id);
                table.ForeignKey(
                    "FK_audit_finding_decisions_artifact_findings_ArtifactFindingId",
                    x => x.ArtifactFindingId,
                    "artifact_findings",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_decisions_ArtifactFindingId",
            table: "audit_finding_decisions",
            column: "ArtifactFindingId");
        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_decisions_TenantId_ArtifactFindingId_CreatedAt",
            table: "audit_finding_decisions",
            columns: new[] { "TenantId", "ArtifactFindingId", "CreatedAt" });

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION reject_audit_finding_decision_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'audit_finding_decisions is append-only';
            END;
            $$;

            CREATE TRIGGER TR_audit_finding_decisions_append_only
            BEFORE UPDATE OR DELETE ON audit_finding_decisions
            FOR EACH ROW
            EXECUTE FUNCTION reject_audit_finding_decision_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("audit_finding_decisions");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_audit_finding_decision_mutation();");
    }
}
