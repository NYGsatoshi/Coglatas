using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830220000_AddTaskExecutionResults")]
public sealed class AddTaskExecutionResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "task_execution_results",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskExecutionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                SchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                BodyMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_execution_results", x => x.Id);
                table.CheckConstraint("CK_task_execution_results_schema", "\"SchemaVersion\" = 1");
                table.CheckConstraint("CK_task_execution_results_status", "\"Status\" = 'Succeeded'");
                table.CheckConstraint("CK_task_execution_results_title", "char_length(\"Title\") BETWEEN 1 AND 200");
                table.CheckConstraint("CK_task_execution_results_body", "char_length(\"BodyMarkdown\") BETWEEN 1 AND 20000");
                table.CheckConstraint("CK_task_execution_results_hash", "\"ContentSha256\" ~ '^[0-9a-f]{64}$'");
                table.ForeignKey(
                    name: "FK_task_execution_results_task_execution_runs_TaskExecutionRunId",
                    column: x => x.TaskExecutionRunId,
                    principalTable: "task_execution_runs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "task_execution_result_sources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskExecutionResultId = table.Column<Guid>(type: "uuid", nullable: false),
                MaterializedSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                Ordinal = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_execution_result_sources", x => x.Id);
                table.CheckConstraint(
                    "CK_task_execution_result_sources_ordinal",
                    "\"Ordinal\" BETWEEN 1 AND 16");
                table.ForeignKey(
                    name: "FK_task_execution_result_sources_results_TaskExecutionResultId",
                    column: x => x.TaskExecutionResultId,
                    principalTable: "task_execution_results",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_task_execution_result_sources_sources_MaterializedSourceId",
                    column: x => x.MaterializedSourceId,
                    principalTable: "task_execution_materialized_sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_task_execution_results_TaskExecutionRunId",
            table: "task_execution_results",
            column: "TaskExecutionRunId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_results_TenantId_ProjectId_TaskItemId",
            table: "task_execution_results",
            columns: new[] { "TenantId", "ProjectId", "TaskItemId" });
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_results_TenantId_TaskExecutionRunId",
            table: "task_execution_results",
            columns: new[] { "TenantId", "TaskExecutionRunId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_task_execution_result_sources_MaterializedSourceId",
            table: "task_execution_result_sources",
            column: "MaterializedSourceId");
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_result_sources_TaskExecutionResultId_Ordinal",
            table: "task_execution_result_sources",
            columns: new[] { "TaskExecutionResultId", "Ordinal" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_result_sources_TenantId_Result_Source",
            table: "task_execution_result_sources",
            columns: new[] { "TenantId", "TaskExecutionResultId", "MaterializedSourceId" },
            unique: true);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION task_execution_result_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP IN ('UPDATE', 'DELETE') THEN
                    RAISE EXCEPTION 'Task execution result is immutable';
                END IF;

                IF NOT EXISTS (
                    SELECT 1
                    FROM task_execution_runs run
                    WHERE run."Id" = NEW."TaskExecutionRunId"
                      AND run."TenantId" = NEW."TenantId"
                      AND run."WorkspaceId" = NEW."WorkspaceId"
                      AND run."ProjectId" = NEW."ProjectId"
                      AND run."TaskItemId" = NEW."TaskItemId"
                      AND run."RuntimeProvider" = 'FirstPartyProjectFilesRuntimeV1'
                      AND run."RuntimeContractVersion" = 1
                      AND run."Status" = 'Running'
                      AND run."StartedAtUtc" IS NOT NULL
                      AND NEW."CompletedAtUtc" >= run."StartedAtUtc"
                      AND NEW."CreatedAtUtc" = NEW."CompletedAtUtc"
                ) THEN
                    RAISE EXCEPTION 'Task execution result scope is invalid';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER task_execution_result_guard_trigger
                BEFORE INSERT OR UPDATE OR DELETE
                ON task_execution_results
                FOR EACH ROW EXECUTE FUNCTION task_execution_result_guard();

            CREATE OR REPLACE FUNCTION task_execution_result_source_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP IN ('UPDATE', 'DELETE') THEN
                    RAISE EXCEPTION 'Task execution result source reference is immutable';
                END IF;

                IF NOT EXISTS (
                    SELECT 1
                    FROM task_execution_results result
                    JOIN task_execution_materialized_sources source
                      ON source."Id" = NEW."MaterializedSourceId"
                    WHERE result."Id" = NEW."TaskExecutionResultId"
                      AND result."TenantId" = NEW."TenantId"
                      AND source."TenantId" = NEW."TenantId"
                      AND source."TaskExecutionRunId" = result."TaskExecutionRunId"
                      AND source."WorkspaceId" = result."WorkspaceId"
                      AND source."ProjectId" = result."ProjectId"
                      AND source."TaskItemId" = result."TaskItemId"
                ) THEN
                    RAISE EXCEPTION 'Task execution result source scope is invalid';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER task_execution_result_source_guard_trigger
                BEFORE INSERT OR UPDATE OR DELETE
                ON task_execution_result_sources
                FOR EACH ROW EXECUTE FUNCTION task_execution_result_source_guard();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_result_source_guard_trigger ON task_execution_result_sources;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_result_source_guard();");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_result_guard_trigger ON task_execution_results;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_result_guard();");
        migrationBuilder.DropTable(name: "task_execution_result_sources");
        migrationBuilder.DropTable(name: "task_execution_results");
    }
}
