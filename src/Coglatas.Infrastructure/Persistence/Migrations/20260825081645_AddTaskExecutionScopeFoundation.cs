using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskExecutionScopeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_execution_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ProjectFilesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_execution_scopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_execution_scopes_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_execution_scopes_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing Projects receive the same fail-closed default as new
            // canonical creates. The policy is deliberately empty: this
            // foundation must not infer source selection from legacy files or
            // Project metadata.
            migrationBuilder.Sql("""
                INSERT INTO project_execution_scopes (
                    "Id", "TenantId", "WorkspaceId", "ProjectId",
                    "WebEnabled", "ProjectFilesEnabled", "VersionNo",
                    "UpdatedByUserId", "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(),
                    project."TenantId",
                    project."WorkspaceId",
                    project."Id",
                    FALSE,
                    FALSE,
                    1,
                    project."CreatedByUserId",
                    CURRENT_TIMESTAMP,
                    NULL
                FROM projects AS project;
                """);

            migrationBuilder.CreateTable(
                name: "task_execution_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    SnapshotSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SnapshotScopeOrigin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SnapshotProjectScopeVersion = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotTaskOverrideVersion = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotWebEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotProjectFilesEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_execution_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_execution_runs_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_execution_runs_task_items_TaskItemId_ProjectId",
                        columns: x => new { x.TaskItemId, x.ProjectId },
                        principalTable: "task_items",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_execution_runs_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_execution_scope_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ProjectFilesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_execution_scope_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_execution_scope_overrides_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_execution_scope_overrides_task_items_TaskItemId_Projec~",
                        columns: x => new { x.TaskItemId, x.ProjectId },
                        principalTable: "task_items",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_execution_scope_overrides_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_execution_scopes_CreatedAt",
                table: "project_execution_scopes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_project_execution_scopes_ProjectId",
                table: "project_execution_scopes",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_execution_scopes_TenantId",
                table: "project_execution_scopes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_project_execution_scopes_TenantId_WorkspaceId",
                table: "project_execution_scopes",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_execution_scopes_UpdatedByUserId",
                table: "project_execution_scopes",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_ProjectId",
                table: "task_execution_runs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_RequestedByUserId",
                table: "task_execution_runs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_TaskItemId_ProjectId",
                table: "task_execution_runs",
                columns: new[] { "TaskItemId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_TenantId",
                table: "task_execution_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_TenantId_ProjectId_RequestedAtUtc",
                table: "task_execution_runs",
                columns: new[] { "TenantId", "ProjectId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_TenantId_TaskItemId_RequestedAtUtc",
                table: "task_execution_runs",
                columns: new[] { "TenantId", "TaskItemId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_CreatedAt",
                table: "task_execution_scope_overrides",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_ProjectId",
                table: "task_execution_scope_overrides",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_TaskItemId",
                table: "task_execution_scope_overrides",
                column: "TaskItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_TaskItemId_ProjectId",
                table: "task_execution_scope_overrides",
                columns: new[] { "TaskItemId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_TenantId",
                table: "task_execution_scope_overrides",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_TenantId_WorkspaceId_Project~",
                table: "task_execution_scope_overrides",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_scope_overrides_UpdatedByUserId",
                table: "task_execution_scope_overrides",
                column: "UpdatedByUserId");

            // UUID FKs alone cannot prove that duplicated Tenant/Workspace
            // columns remain aligned with the parent Project/Task graph. Keep
            // the application tenant filter as the normal boundary and make
            // raw SQL/platform-scope writes fail closed as well. The run guard
            // also freezes the accepted snapshot fields while allowing a later,
            // separately approved runtime to update lifecycle-only columns.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION project_execution_scope_scope_guard() RETURNS trigger AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM projects AS project
                        WHERE project."Id" = NEW."ProjectId"
                          AND project."TenantId" = NEW."TenantId"
                          AND project."WorkspaceId" = NEW."WorkspaceId") THEN
                        RAISE EXCEPTION 'Project execution scope tenant/workspace/project mismatch';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER project_execution_scope_scope_guard_trigger
                    BEFORE INSERT OR UPDATE OF "TenantId", "WorkspaceId", "ProjectId"
                    ON project_execution_scopes
                    FOR EACH ROW EXECUTE FUNCTION project_execution_scope_scope_guard();

                CREATE OR REPLACE FUNCTION project_execution_scope_delete_guard() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Project execution scopes are persistent defaults and cannot be deleted';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER project_execution_scope_delete_guard_trigger
                    BEFORE DELETE ON project_execution_scopes
                    FOR EACH ROW EXECUTE FUNCTION project_execution_scope_delete_guard();

                CREATE OR REPLACE FUNCTION task_execution_scope_override_scope_guard() RETURNS trigger AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM task_items AS task
                        WHERE task."Id" = NEW."TaskItemId"
                          AND task."ProjectId" = NEW."ProjectId"
                          AND task."TenantId" = NEW."TenantId"
                          AND task."WorkspaceId" = NEW."WorkspaceId") THEN
                        RAISE EXCEPTION 'Task execution scope override tenant/workspace/project mismatch';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_scope_override_scope_guard_trigger
                    BEFORE INSERT OR UPDATE OF "TenantId", "WorkspaceId", "ProjectId", "TaskItemId"
                    ON task_execution_scope_overrides
                    FOR EACH ROW EXECUTE FUNCTION task_execution_scope_override_scope_guard();

                CREATE OR REPLACE FUNCTION task_execution_run_scope_and_snapshot_guard() RETURNS trigger AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM task_items AS task
                        WHERE task."Id" = NEW."TaskItemId"
                          AND task."ProjectId" = NEW."ProjectId"
                          AND task."TenantId" = NEW."TenantId"
                          AND task."WorkspaceId" = NEW."WorkspaceId") THEN
                        RAISE EXCEPTION 'Task execution run tenant/workspace/project mismatch';
                    END IF;

                    IF TG_OP = 'UPDATE' AND (
                        NEW."Id" IS DISTINCT FROM OLD."Id" OR
                        NEW."TenantId" IS DISTINCT FROM OLD."TenantId" OR
                        NEW."WorkspaceId" IS DISTINCT FROM OLD."WorkspaceId" OR
                        NEW."ProjectId" IS DISTINCT FROM OLD."ProjectId" OR
                        NEW."TaskItemId" IS DISTINCT FROM OLD."TaskItemId" OR
                        NEW."RequestedByUserId" IS DISTINCT FROM OLD."RequestedByUserId" OR
                        NEW."RequestedAtUtc" IS DISTINCT FROM OLD."RequestedAtUtc" OR
                        NEW."SnapshotSchemaVersion" IS DISTINCT FROM OLD."SnapshotSchemaVersion" OR
                        NEW."SnapshotScopeOrigin" IS DISTINCT FROM OLD."SnapshotScopeOrigin" OR
                        NEW."SnapshotProjectScopeVersion" IS DISTINCT FROM OLD."SnapshotProjectScopeVersion" OR
                        NEW."SnapshotTaskOverrideVersion" IS DISTINCT FROM OLD."SnapshotTaskOverrideVersion" OR
                        NEW."SnapshotWebEnabled" IS DISTINCT FROM OLD."SnapshotWebEnabled" OR
                        NEW."SnapshotProjectFilesEnabled" IS DISTINCT FROM OLD."SnapshotProjectFilesEnabled") THEN
                        RAISE EXCEPTION 'Task execution run snapshot is immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_run_scope_and_snapshot_guard_trigger
                    BEFORE INSERT OR UPDATE OF "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "RequestedByUserId", "RequestedAtUtc", "SnapshotSchemaVersion", "SnapshotScopeOrigin", "SnapshotProjectScopeVersion", "SnapshotTaskOverrideVersion", "SnapshotWebEnabled", "SnapshotProjectFilesEnabled"
                    ON task_execution_runs
                    FOR EACH ROW EXECUTE FUNCTION task_execution_run_scope_and_snapshot_guard();

                CREATE OR REPLACE FUNCTION task_execution_run_delete_guard() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Task execution runs are append-only foundation records and cannot be deleted';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_run_delete_guard_trigger
                    BEFORE DELETE ON task_execution_runs
                    FOR EACH ROW EXECUTE FUNCTION task_execution_run_delete_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_run_delete_guard_trigger ON task_execution_runs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_run_delete_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_run_scope_and_snapshot_guard_trigger ON task_execution_runs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_run_scope_and_snapshot_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_execution_scope_override_scope_guard_trigger ON task_execution_scope_overrides;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_execution_scope_override_scope_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS project_execution_scope_delete_guard_trigger ON project_execution_scopes;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS project_execution_scope_delete_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS project_execution_scope_scope_guard_trigger ON project_execution_scopes;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS project_execution_scope_scope_guard();");

            migrationBuilder.DropTable(
                name: "project_execution_scopes");

            migrationBuilder.DropTable(
                name: "task_execution_runs");

            migrationBuilder.DropTable(
                name: "task_execution_scope_overrides");
        }
    }
}
