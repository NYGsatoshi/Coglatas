using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdeCompilerRecordingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projectide_compiler_recording_evidence",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projectide_compiler_recording_evidence", x => new { x.TenantId, x.ProjectId, x.RunId });
                    table.CheckConstraint("CK_projectide_compiler_recording_evidence_binding", "(jsonb_typeof(\"Payload\") = 'object'\nAND \"Payload\" - ARRAY['Source', 'SourceDigest', 'RunId', 'AttemptId', 'CompilerInputId',\n    'ExecutionInputDigest', 'MaterializationsDigest', 'ManifestDigest', 'ReceiptDigest', 'HostStatus',\n    'CaptureMode', 'OriginalCaptureState', 'CaptureDigest', 'OriginRunId', 'OriginReceiptDigest', 'Result'] = '{}'::jsonb\nAND \"Payload\" ->> 'RunId' = \"RunId\"::text\nAND \"Payload\" ->> 'AttemptId' = \"AttemptId\"::text\nAND \"Payload\" -> 'Source' ->> 'TenantId' = \"TenantId\"::text\nAND \"Payload\" -> 'Source' ->> 'ProjectId' = \"ProjectId\"::text\nAND \"Payload\" ->> 'SourceDigest' ~ '^[0-9a-f]{64}$'\nAND \"Payload\" ->> 'ManifestDigest' ~ '^[0-9a-f]{64}$'\nAND \"Payload\" ->> 'ReceiptDigest' ~ '^[0-9a-f]{64}$') IS TRUE");
                    table.ForeignKey(
                        name: "FK_projectide_compiler_recording_evidence_projects_ProjectId_T~",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projectide_compiler_recording_evidence_ProjectId_TenantId",
                table: "projectide_compiler_recording_evidence",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_projectide_compiler_recording_evidence_TenantId_ProjectId_A~",
                table: "projectide_compiler_recording_evidence",
                columns: new[] { "TenantId", "ProjectId", "AttemptId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_projectide_compiler_recording_evidence_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Compiler recording evidence is immutable';
                END;
                $$;

                CREATE TRIGGER projectide_compiler_recording_evidence_immutable
                    BEFORE UPDATE ON projectide_compiler_recording_evidence
                    FOR EACH ROW EXECUTE FUNCTION prevent_projectide_compiler_recording_evidence_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projectide_compiler_recording_evidence");

            migrationBuilder.Sql("DROP FUNCTION prevent_projectide_compiler_recording_evidence_update();");
        }
    }
}
