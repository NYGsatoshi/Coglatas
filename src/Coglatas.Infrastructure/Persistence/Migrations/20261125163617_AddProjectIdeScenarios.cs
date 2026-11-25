using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdeScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projectide_scenarios",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Identity = table.Column<string>(type: "jsonb", nullable: false),
                    CanonicalOverlay = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projectide_scenarios", x => new { x.TenantId, x.ProjectId, x.ScenarioId });
                    table.CheckConstraint("CK_projectide_scenarios_source_scope", "(jsonb_typeof(\"Identity\") = 'object'\nAND \"Identity\" ->> 'Id' = \"ScenarioId\"::text\nAND \"Identity\" ->> 'Revision' = \"RevisionId\"::text\nAND \"Identity\" #>> '{Basis,Identity,TenantId}' = \"TenantId\"::text\nAND \"Identity\" #>> '{Basis,Identity,ProjectId}' = \"ProjectId\"::text\nAND \"Identity\" #>> '{Input,Identity,TenantId}' = \"TenantId\"::text\nAND \"Identity\" #>> '{Input,Identity,ProjectId}' = \"ProjectId\"::text\nAND \"Identity\" #>> '{Input,Identity,ScenarioId}' = \"ScenarioId\"::text\nAND \"Identity\" #>> '{Input,Identity,ScenarioRevisionId}' = \"RevisionId\"::text\nAND \"Identity\" #>> '{Input,Identity,OverlayDigest}' ~ '^[0-9a-f]{64}$') IS TRUE");
                    table.ForeignKey(
                        name: "FK_projectide_scenarios_projects_ProjectId_TenantId",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projectide_scenarios_ProjectId_TenantId",
                table: "projectide_scenarios",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_projectide_scenario_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'A saved Scenario version is immutable';
                END;
                $$;

                CREATE TRIGGER projectide_scenarios_immutable
                    BEFORE UPDATE ON projectide_scenarios
                    FOR EACH ROW EXECUTE FUNCTION prevent_projectide_scenario_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projectide_scenarios");

            migrationBuilder.Sql("DROP FUNCTION prevent_projectide_scenario_update();");
        }
    }
}
