using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830150000_AddTaskExecutionMaterializedSources")]
public sealed class AddTaskExecutionMaterializedSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "task_execution_materialized_sources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskExecutionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                SchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MediaType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                MaterializedByteCount = table.Column<long>(type: "bigint", nullable: false),
                MaterializedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_execution_materialized_sources", x => x.Id);
                table.CheckConstraint(
                    "CK_task_execution_materialized_sources_schema",
                    "\"SchemaVersion\" = 1");
                table.CheckConstraint(
                    "CK_task_execution_materialized_sources_media_type",
                    "\"MediaType\" IN ('text/plain', 'text/markdown')");
                table.CheckConstraint(
                    "CK_task_execution_materialized_sources_byte_count",
                    "\"MaterializedByteCount\" >= 0 AND \"MaterializedByteCount\" <= 262144");
                table.CheckConstraint(
                    "CK_task_execution_materialized_sources_hash",
                    "length(\"ContentSha256\") = 64");
                table.ForeignKey(
                    name: "FK_task_execution_materialized_sources_attachments_AttachmentId",
                    column: x => x.AttachmentId,
                    principalTable: "attachments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_task_execution_materialized_sources_file_objects_FileObjectId",
                    column: x => x.FileObjectId,
                    principalTable: "file_objects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_task_execution_materialized_sources_task_execution_runs_TaskExecutionRunId",
                    column: x => x.TaskExecutionRunId,
                    principalTable: "task_execution_runs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_task_execution_materialized_sources_AttachmentId",
            table: "task_execution_materialized_sources",
            column: "AttachmentId");
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_materialized_sources_FileObjectId",
            table: "task_execution_materialized_sources",
            column: "FileObjectId");
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_materialized_sources_TaskExecutionRunId_AttachmentId",
            table: "task_execution_materialized_sources",
            columns: new[] { "TaskExecutionRunId", "AttachmentId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_materialized_sources_TenantId_ProjectId_TaskItemId",
            table: "task_execution_materialized_sources",
            columns: new[] { "TenantId", "ProjectId", "TaskItemId" });
        migrationBuilder.CreateIndex(
            name: "IX_task_execution_materialized_sources_TenantId_TaskExecutionRunId",
            table: "task_execution_materialized_sources",
            columns: new[] { "TenantId", "TaskExecutionRunId" });

        // Keep the provenance metadata-only, scope-consistent, and immutable
        // even for raw SQL and platform-scope callers.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION task_execution_materialized_source_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP IN ('UPDATE', 'DELETE') THEN
                    RAISE EXCEPTION 'Task execution materialized source provenance is immutable';
                END IF;

                IF NOT EXISTS (
                    SELECT 1
                    FROM task_execution_runs run
                    JOIN attachments attachment
                      ON attachment."Id" = NEW."AttachmentId"
                    JOIN file_objects file_object
                      ON file_object."Id" = NEW."FileObjectId"
                    WHERE run."Id" = NEW."TaskExecutionRunId"
                      AND run."TenantId" = NEW."TenantId"
                      AND run."WorkspaceId" = NEW."WorkspaceId"
                      AND run."ProjectId" = NEW."ProjectId"
                      AND run."TaskItemId" = NEW."TaskItemId"
                      AND run."RuntimeProvider" = 'FirstPartyProjectFilesRuntimeV1'
                      AND run."RuntimeContractVersion" = 1
                      AND run."SnapshotWebEnabled" = FALSE
                      AND run."SnapshotProjectFilesEnabled" = TRUE
                      AND run."Status" = 'Running'
                      AND run."StartedAtUtc" IS NOT NULL
                      AND NEW."MaterializedAtUtc" >= run."StartedAtUtc"
                      AND attachment."TenantId" = NEW."TenantId"
                      AND attachment."WorkspaceId" = NEW."WorkspaceId"
                      AND attachment."FileObjectId" = NEW."FileObjectId"
                      AND attachment."OwnerType" = 'TaskItem'
                      AND attachment."OwnerId" = NEW."TaskItemId"
                      AND attachment."DeletedAt" IS NULL
                      AND attachment."ScanStatus" = 'Clean'
                      AND file_object."TenantId" = NEW."TenantId"
                      AND file_object."WorkspaceId" = NEW."WorkspaceId"
                      AND file_object."ProjectId" = NEW."ProjectId"
                      AND file_object."DeletedAt" IS NULL
                      AND file_object."Status" = 'Active'
                      AND (
                          file_object."HashSha256" IS NULL OR
                          lower(file_object."HashSha256") = NEW."ContentSha256"
                      )
                ) THEN
                    RAISE EXCEPTION 'Task execution materialized source scope is invalid';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER task_execution_materialized_source_guard_trigger
                BEFORE INSERT OR UPDATE OR DELETE
                ON task_execution_materialized_sources
                FOR EACH ROW EXECUTE FUNCTION task_execution_materialized_source_guard();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_materialized_source_guard_trigger ON task_execution_materialized_sources;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_materialized_source_guard();");
        migrationBuilder.DropTable(name: "task_execution_materialized_sources");
    }
}
