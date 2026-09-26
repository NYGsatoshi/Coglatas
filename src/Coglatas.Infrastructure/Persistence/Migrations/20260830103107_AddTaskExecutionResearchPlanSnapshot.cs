using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskExecutionResearchPlanSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SnapshotResearchPlanRevisionId",
                table: "task_execution_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SnapshotResearchPlanRevisionNo",
                table: "task_execution_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_research_plan_revisions_execution_snapshot_identity",
                table: "research_plan_revisions",
                columns: new[] { "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "RevisionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_plan_snapshot_lookup",
                table: "task_execution_runs",
                columns: new[] { "TenantId", "TaskItemId", "SnapshotResearchPlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_runs_plan_snapshot_scope",
                table: "task_execution_runs",
                columns: new[] { "SnapshotResearchPlanRevisionId", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "SnapshotResearchPlanRevisionNo" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_execution_runs_research_plan_snapshot",
                table: "task_execution_runs",
                sql: "(\"SnapshotResearchPlanRevisionId\" IS NULL AND \"SnapshotResearchPlanRevisionNo\" IS NULL) OR (\"SnapshotResearchPlanRevisionId\" IS NOT NULL AND \"SnapshotResearchPlanRevisionNo\" IS NOT NULL AND \"SnapshotResearchPlanRevisionNo\" > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_task_execution_runs_plan_snapshot_revision",
                table: "task_execution_runs",
                columns: new[] { "SnapshotResearchPlanRevisionId", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "SnapshotResearchPlanRevisionNo" },
                principalTable: "research_plan_revisions",
                principalColumns: new[] { "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "RevisionNo" },
                onDelete: ReferentialAction.Restrict);

            // The original #357 guard freezes accepted-run snapshot fields.
            // Recreate its column-specific trigger after adding the Research
            // Plan fields so a raw SQL update cannot exchange an accepted run's
            // valid same-scope revision for another revision.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS task_execution_run_scope_and_snapshot_guard_trigger ON task_execution_runs;

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
                        NEW."SnapshotProjectFilesEnabled" IS DISTINCT FROM OLD."SnapshotProjectFilesEnabled" OR
                        NEW."SnapshotResearchPlanRevisionId" IS DISTINCT FROM OLD."SnapshotResearchPlanRevisionId" OR
                        NEW."SnapshotResearchPlanRevisionNo" IS DISTINCT FROM OLD."SnapshotResearchPlanRevisionNo") THEN
                        RAISE EXCEPTION 'Task execution run snapshot is immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER task_execution_run_scope_and_snapshot_guard_trigger
                    BEFORE INSERT OR UPDATE OF "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "RequestedByUserId", "RequestedAtUtc", "SnapshotSchemaVersion", "SnapshotScopeOrigin", "SnapshotProjectScopeVersion", "SnapshotTaskOverrideVersion", "SnapshotWebEnabled", "SnapshotProjectFilesEnabled", "SnapshotResearchPlanRevisionId", "SnapshotResearchPlanRevisionNo"
                    ON task_execution_runs
                    FOR EACH ROW EXECUTE FUNCTION task_execution_run_scope_and_snapshot_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the #357 trigger shape before removing the additive
            // snapshot columns. The #461 runtime identity and lifecycle guards
            // are separate triggers and intentionally remain untouched.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS task_execution_run_scope_and_snapshot_guard_trigger ON task_execution_runs;

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
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_task_execution_runs_plan_snapshot_revision",
                table: "task_execution_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_execution_runs_plan_snapshot_lookup",
                table: "task_execution_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_execution_runs_plan_snapshot_scope",
                table: "task_execution_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_execution_runs_research_plan_snapshot",
                table: "task_execution_runs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_research_plan_revisions_execution_snapshot_identity",
                table: "research_plan_revisions");

            migrationBuilder.DropColumn(
                name: "SnapshotResearchPlanRevisionId",
                table: "task_execution_runs");

            migrationBuilder.DropColumn(
                name: "SnapshotResearchPlanRevisionNo",
                table: "task_execution_runs");
        }
    }
}
