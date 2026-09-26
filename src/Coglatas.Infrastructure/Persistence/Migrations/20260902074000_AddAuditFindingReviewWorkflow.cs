using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260902074000_AddAuditFindingReviewWorkflow")]
public sealed class AddAuditFindingReviewWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WorkflowStatus",
            table: "artifact_findings",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Open");

        migrationBuilder.AddColumn<DateOnly>(
            name: "DueDate",
            table: "artifact_findings",
            type: "date",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "audit_finding_workflow_history",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                FromWorkflowStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ToWorkflowStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FromOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ToOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                FromDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                ToDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_finding_workflow_history", x => x.Id);
                table.ForeignKey(
                    "FK_audit_finding_workflow_history_finding",
                    x => x.ArtifactFindingId,
                    "artifact_findings",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_artifact_findings_workflow_due_owner",
            table: "artifact_findings",
            columns: new[] { "TenantId", "WorkflowStatus", "DueDate", "OwnerUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_workflow_history_finding",
            table: "audit_finding_workflow_history",
            column: "ArtifactFindingId");

        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_workflow_history_tenant_finding_created",
            table: "audit_finding_workflow_history",
            columns: new[] { "TenantId", "ArtifactFindingId", "CreatedAt" });

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION reject_audit_finding_workflow_history_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'audit_finding_workflow_history is append-only';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER audit_finding_workflow_history_append_only
            BEFORE UPDATE OR DELETE ON audit_finding_workflow_history
            FOR EACH ROW EXECUTE FUNCTION reject_audit_finding_workflow_history_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS audit_finding_workflow_history_append_only ON audit_finding_workflow_history;
            DROP FUNCTION IF EXISTS reject_audit_finding_workflow_history_mutation();
            """);

        migrationBuilder.DropTable("audit_finding_workflow_history");

        migrationBuilder.DropIndex(
            name: "IX_artifact_findings_workflow_due_owner",
            table: "artifact_findings");

        migrationBuilder.DropColumn(
            name: "DueDate",
            table: "artifact_findings");

        migrationBuilder.DropColumn(
            name: "WorkflowStatus",
            table: "artifact_findings");
    }
}
