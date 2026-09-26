using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260829110500_AddArtifactClaimsEvidence")]
public sealed class AddArtifactClaimsEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "artifact_claims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                Ordinal = table.Column<int>(type: "integer", nullable: false),
                Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                CitationPresent = table.Column<bool>(type: "boolean", nullable: false),
                SupportStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ReviewStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifact_claims", x => x.Id);
                table.CheckConstraint("CK_artifact_claims_ordinal", "\"Ordinal\" > 0");
                table.ForeignKey(
                    name: "FK_artifact_claims_artifact_versions_ArtifactVersionId",
                    column: x => x.ArtifactVersionId,
                    principalTable: "artifact_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "artifact_evidence",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                Ordinal = table.Column<int>(type: "integer", nullable: false),
                SourceKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SourceReference = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                SourceTitleSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                PassageSnapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                LocationSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                SourceEventAuditId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifact_evidence", x => x.Id);
                table.CheckConstraint("CK_artifact_evidence_ordinal", "\"Ordinal\" > 0");
                table.ForeignKey(
                    name: "FK_artifact_evidence_artifact_claims_ArtifactClaimId",
                    column: x => x.ArtifactClaimId,
                    principalTable: "artifact_claims",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_artifact_claims_ArtifactVersionId",
            table: "artifact_claims",
            column: "ArtifactVersionId");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_claims_CreatedAt",
            table: "artifact_claims",
            column: "CreatedAt");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_claims_TenantId",
            table: "artifact_claims",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_claims_TenantId_ArtifactVersionId_Ordinal",
            table: "artifact_claims",
            columns: new[] { "TenantId", "ArtifactVersionId", "Ordinal" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_artifact_evidence_ArtifactClaimId",
            table: "artifact_evidence",
            column: "ArtifactClaimId");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_evidence_CreatedAt",
            table: "artifact_evidence",
            column: "CreatedAt");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_evidence_SourceEventAuditId",
            table: "artifact_evidence",
            column: "SourceEventAuditId");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_evidence_TenantId",
            table: "artifact_evidence",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_artifact_evidence_TenantId_ArtifactClaimId_Ordinal",
            table: "artifact_evidence",
            columns: new[] { "TenantId", "ArtifactClaimId", "Ordinal" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "artifact_evidence");
        migrationBuilder.DropTable(name: "artifact_claims");
    }
}
