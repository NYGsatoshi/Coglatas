using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260831120000_AddStructuredArtifactReports")]
public sealed class AddStructuredArtifactReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "LogicalClaimId", table: "artifact_claims", type: "uuid", nullable: true);
        migrationBuilder.Sql("UPDATE artifact_claims SET \"LogicalClaimId\" = \"Id\" WHERE \"LogicalClaimId\" IS NULL;");
        migrationBuilder.AlterColumn<Guid>(name: "LogicalClaimId", table: "artifact_claims", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        migrationBuilder.CreateIndex(name: "IX_artifact_claims_TenantId_LogicalClaimId", table: "artifact_claims", columns: new[] { "TenantId", "LogicalClaimId" });

        migrationBuilder.CreateTable(name: "artifact_report_documents", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), TenantId = table.Column<Guid>(type: "uuid", nullable: false), ArtifactVersionId = table.Column<Guid>(type: "uuid", nullable: false), SchemaVersion = table.Column<int>(type: "integer", nullable: false), Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_artifact_report_documents", x => x.Id); table.ForeignKey("FK_artifact_report_documents_artifact_versions_ArtifactVersionId", x => x.ArtifactVersionId, "artifact_versions", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateTable(name: "artifact_report_sections", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), LogicalSectionId = table.Column<Guid>(type: "uuid", nullable: false), TenantId = table.Column<Guid>(type: "uuid", nullable: false), ArtifactReportDocumentId = table.Column<Guid>(type: "uuid", nullable: false), Ordinal = table.Column<int>(type: "integer", nullable: false), Heading = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false), BodyText = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_artifact_report_sections", x => x.Id); table.CheckConstraint("CK_artifact_report_sections_ordinal", "\"Ordinal\" > 0"); table.ForeignKey("FK_artifact_report_sections_artifact_report_documents_ArtifactReportDocumentId", x => x.ArtifactReportDocumentId, "artifact_report_documents", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateTable(name: "artifact_report_citations", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), TenantId = table.Column<Guid>(type: "uuid", nullable: false), ArtifactReportSectionId = table.Column<Guid>(type: "uuid", nullable: false), Ordinal = table.Column<int>(type: "integer", nullable: false), AnchorStartUtf16 = table.Column<int>(type: "integer", nullable: false), AnchorLengthUtf16 = table.Column<int>(type: "integer", nullable: false), ArtifactClaimId = table.Column<Guid>(type: "uuid", nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_artifact_report_citations", x => x.Id); table.CheckConstraint("CK_artifact_report_citations_anchor", "\"Ordinal\" > 0 AND \"AnchorStartUtf16\" >= 0 AND \"AnchorLengthUtf16\" > 0"); table.ForeignKey("FK_artifact_report_citations_artifact_claims_ArtifactClaimId", x => x.ArtifactClaimId, "artifact_claims", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_artifact_report_citations_artifact_report_sections_ArtifactReportSectionId", x => x.ArtifactReportSectionId, "artifact_report_sections", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex(name: "IX_artifact_report_documents_ArtifactVersionId", table: "artifact_report_documents", column: "ArtifactVersionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_artifact_report_documents_TenantId", table: "artifact_report_documents", column: "TenantId");
        migrationBuilder.CreateIndex(name: "IX_artifact_report_sections_TenantId_ArtifactReportDocumentId_Ordinal", table: "artifact_report_sections", columns: new[] { "TenantId", "ArtifactReportDocumentId", "Ordinal" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_artifact_report_sections_ArtifactReportDocumentId", table: "artifact_report_sections", column: "ArtifactReportDocumentId");
        migrationBuilder.CreateIndex(name: "IX_artifact_report_citations_TenantId_ArtifactReportSectionId_Ordinal", table: "artifact_report_citations", columns: new[] { "TenantId", "ArtifactReportSectionId", "Ordinal" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_artifact_report_citations_ArtifactClaimId", table: "artifact_report_citations", column: "ArtifactClaimId");
        migrationBuilder.CreateIndex(name: "IX_artifact_report_citations_ArtifactReportSectionId", table: "artifact_report_citations", column: "ArtifactReportSectionId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("artifact_report_citations"); migrationBuilder.DropTable("artifact_report_sections"); migrationBuilder.DropTable("artifact_report_documents"); migrationBuilder.DropIndex("IX_artifact_claims_TenantId_LogicalClaimId", "artifact_claims"); migrationBuilder.DropColumn("LogicalClaimId", "artifact_claims");
    }
}
