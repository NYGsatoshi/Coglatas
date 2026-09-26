using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260901030000_AddAuditFindingTriage")]
public sealed class AddAuditFindingTriage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "artifact_findings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ConfidencePercent = table.Column<int>(type: "integer", nullable: false),
                DetectorKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PolicyVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ResolutionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifact_findings", x => x.Id);
                table.CheckConstraint(
                    "CK_artifact_findings_confidence",
                    "\"ConfidencePercent\" >= 0 AND \"ConfidencePercent\" <= 100");
                table.ForeignKey(
                    "FK_artifact_findings_artifact_claims_ArtifactClaimId",
                    x => x.ArtifactClaimId,
                    "artifact_claims",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "audit_finding_history",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_finding_history", x => x.Id);
                table.ForeignKey(
                    "FK_audit_finding_history_artifact_findings_ArtifactFindingId",
                    x => x.ArtifactFindingId,
                    "artifact_findings",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_artifact_findings_ArtifactClaimId",
            table: "artifact_findings",
            column: "ArtifactClaimId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_artifact_findings_TenantId_Status_Severity",
            table: "artifact_findings",
            columns: new[] { "TenantId", "Status", "Severity" });
        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_history_TenantId_ArtifactFindingId_CreatedAt",
            table: "audit_finding_history",
            columns: new[] { "TenantId", "ArtifactFindingId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_audit_finding_history_ArtifactFindingId",
            table: "audit_finding_history",
            column: "ArtifactFindingId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("audit_finding_history");
        migrationBuilder.DropTable("artifact_findings");
    }
}
