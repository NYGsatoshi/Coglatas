using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdeCompilerOutputCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projectide_compiler_output_captures",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    BindingDigest = table.Column<string>(type: "text", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaximumBodyBytes = table.Column<int>(type: "integer", nullable: true),
                    Projection = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projectide_compiler_output_captures", x => new { x.TenantId, x.ProjectId, x.CaptureId });
                    table.CheckConstraint("CK_projectide_compiler_output_capture_projection", "((\"DeletedAtUtc\" IS NOT NULL AND \"RunId\" IS NULL AND \"BindingDigest\" IS NULL\n    AND \"MaximumBodyBytes\" IS NULL AND \"Projection\" IS NULL)\nOR (\"DeletedAtUtc\" IS NULL AND \"RunId\" IS NOT NULL AND \"BindingDigest\" ~ '^[0-9a-f]{64}$'\n    AND \"MaximumBodyBytes\" BETWEEN 1 AND 33554432\n    AND jsonb_typeof(\"Projection\") = 'object'\n    AND \"Projection\" - ARRAY['Mode', 'State', 'Execution', 'Coverage', 'Verdict', 'EvidenceComplete',\n        'SemanticModelWasCaptured', 'IrWasCaptured', 'SourceMapWasCaptured'] = '{}'::jsonb\n    AND jsonb_typeof(\"Projection\" -> 'Mode') = 'number'\n    AND jsonb_typeof(\"Projection\" -> 'State') = 'number'\n    AND jsonb_typeof(\"Projection\" -> 'Execution') = 'number'\n    AND jsonb_typeof(\"Projection\" -> 'Coverage') = 'number'\n    AND jsonb_typeof(\"Projection\" -> 'Verdict') = 'number'\n    AND jsonb_typeof(\"Projection\" -> 'EvidenceComplete') = 'boolean'\n    AND jsonb_typeof(\"Projection\" -> 'SemanticModelWasCaptured') = 'boolean'\n    AND jsonb_typeof(\"Projection\" -> 'IrWasCaptured') = 'boolean'\n    AND jsonb_typeof(\"Projection\" -> 'SourceMapWasCaptured') = 'boolean')) IS TRUE");
                    table.ForeignKey(
                        name: "FK_projectide_compiler_output_captures_projectide_compiler_rec~",
                        columns: x => new { x.TenantId, x.ProjectId, x.RunId },
                        principalTable: "projectide_compiler_recording_evidence",
                        principalColumns: new[] { "TenantId", "ProjectId", "RunId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projectide_compiler_output_captures_TenantId_ProjectId_RunId",
                table: "projectide_compiler_output_captures",
                columns: new[] { "TenantId", "ProjectId", "RunId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_projectide_compiler_output_capture()
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

                CREATE TRIGGER projectide_compiler_output_capture_immutable
                    BEFORE UPDATE ON projectide_compiler_output_captures
                    FOR EACH ROW EXECUTE FUNCTION guard_projectide_compiler_output_capture();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projectide_compiler_output_captures");

            migrationBuilder.Sql("DROP FUNCTION guard_projectide_compiler_output_capture();");
        }
    }
}
