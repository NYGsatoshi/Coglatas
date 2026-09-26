using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskV1DomainPersistenceCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualStartAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "task_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "task_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadlineAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedEffortMinutes",
                table: "task_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "task_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "task_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Task");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentTaskItemId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedEndDate",
                table: "task_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedStartDate",
                table: "task_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryAssigneeUserId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewerUserId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SortKey",
                table: "task_items",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetGroupId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VersionNo",
                table: "task_items",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowStageId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "task_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_task_items_Id_ProjectId",
                table: "task_items",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "task_item_collaborators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_item_collaborators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_item_collaborators_task_items_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "task_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_item_collaborators_users_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_item_collaborators_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_migration_inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    FindingCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_migration_inventory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_workflow_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReviewEnforcementEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_workflow_definitions", x => x.Id);
                    table.UniqueConstraint("AK_task_workflow_definitions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_task_workflow_definitions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_workflow_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InternalCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortKey = table.Column<long>(type: "bigint", nullable: false),
                    WipWarningLimit = table.Column<int>(type: "integer", nullable: true),
                    IsInitialStage = table.Column<bool>(type: "boolean", nullable: false),
                    IsTerminalStage = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_workflow_stages", x => x.Id);
                    table.UniqueConstraint("AK_task_workflow_stages_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_task_workflow_stages_wip", "\"WipWarningLimit\" IS NULL OR \"WipWarningLimit\" > 0");
                    table.ForeignKey(
                        name: "FK_task_workflow_stages_task_workflow_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "task_workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ParentTaskItemId",
                table: "task_items",
                column: "ParentTaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ParentTaskItemId_ProjectId",
                table: "task_items",
                columns: new[] { "ParentTaskItemId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_PrimaryAssigneeUserId",
                table: "task_items",
                column: "PrimaryAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ProjectId_DeadlineAt",
                table: "task_items",
                columns: new[] { "ProjectId", "DeadlineAt" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ProjectId_IsBlocked",
                table: "task_items",
                columns: new[] { "ProjectId", "IsBlocked" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ProjectId_PlannedEndDate",
                table: "task_items",
                columns: new[] { "ProjectId", "PlannedEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ProjectId_WorkflowStageId_SortKey",
                table: "task_items",
                columns: new[] { "ProjectId", "WorkflowStageId", "SortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ReviewerUserId",
                table: "task_items",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TargetGroupId",
                table: "task_items",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_WorkspaceId_ProjectId",
                table: "task_items",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_WorkflowStageId_ProjectId",
                table: "task_items",
                columns: new[] { "WorkflowStageId", "ProjectId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_items_effort",
                table: "task_items",
                sql: "\"EstimatedEffortMinutes\" IS NULL OR \"EstimatedEffortMinutes\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_items_planned_dates",
                table: "task_items",
                sql: "\"PlannedEndDate\" IS NULL OR \"PlannedStartDate\" IS NULL OR \"PlannedEndDate\" >= \"PlannedStartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_items_reviewer_not_primary",
                table: "task_items",
                sql: "\"ReviewerUserId\" IS NULL OR \"PrimaryAssigneeUserId\" IS NULL OR \"ReviewerUserId\" <> \"PrimaryAssigneeUserId\"");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_ProjectId_PredecessorTaskItemId",
                table: "task_dependencies",
                columns: new[] { "ProjectId", "PredecessorTaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_ProjectId_SuccessorTaskItemId",
                table: "task_dependencies",
                columns: new[] { "ProjectId", "SuccessorTaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_AddedByUserId",
                table: "task_item_collaborators",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_TaskItemId",
                table: "task_item_collaborators",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_TenantId",
                table: "task_item_collaborators",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_TenantId_TaskItemId_UserId",
                table: "task_item_collaborators",
                columns: new[] { "TenantId", "TaskItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_UserId",
                table: "task_item_collaborators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_migration_inventory_ProjectId",
                table: "task_migration_inventory",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_task_migration_inventory_TaskItemId",
                table: "task_migration_inventory",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_migration_inventory_TenantId",
                table: "task_migration_inventory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_migration_inventory_TenantId_FindingCode",
                table: "task_migration_inventory",
                columns: new[] { "TenantId", "FindingCode" });

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_definitions_ProjectId",
                table: "task_workflow_definitions",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_definitions_TenantId",
                table: "task_workflow_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_definitions_TenantId_WorkspaceId_ProjectId",
                table: "task_workflow_definitions",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_stages_DefinitionId_SortKey",
                table: "task_workflow_stages",
                columns: new[] { "DefinitionId", "SortKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_stages_ProjectId_InternalCategory",
                table: "task_workflow_stages",
                columns: new[] { "ProjectId", "InternalCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_task_workflow_stages_TenantId",
                table: "task_workflow_stages",
                column: "TenantId");

            // Compatibility conversion is additive: legacy fields and rows remain available for rollback and later command cutover.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                UPDATE task_items AS task
                SET "WorkspaceId" = project."WorkspaceId", "Kind" = 'Task',
                    "Priority" = CASE WHEN task."Priority" = 'Normal' THEN 'Medium' ELSE task."Priority" END,
                    "PlannedStartDate" = task."StartDate", "PlannedEndDate" = task."DueDate",
                    "SortKey" = ranked.sort_key, "VersionNo" = 1
                FROM projects AS project
                CROSS JOIN (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "ProjectId" ORDER BY "SortOrder", "CreatedAt", "Id") * 1000 AS sort_key FROM task_items) AS ranked
                WHERE project."Id" = task."ProjectId"
                  AND ranked."Id" = task."Id";

                INSERT INTO task_workflow_definitions ("Id", "TenantId", "WorkspaceId", "ProjectId", "Name", "ReviewEnforcementEnabled", "VersionNo")
                SELECT gen_random_uuid(), project."TenantId", project."WorkspaceId", project."Id", 'Default', TRUE, 1 FROM projects AS project;

                INSERT INTO task_workflow_stages ("Id", "TenantId", "WorkspaceId", "ProjectId", "DefinitionId", "Name", "InternalCategory", "SortKey", "WipWarningLimit", "IsInitialStage", "IsTerminalStage", "VersionNo")
                SELECT gen_random_uuid(), definition."TenantId", definition."WorkspaceId", definition."ProjectId", definition."Id", stage."Name", stage."Category", stage."SortKey", NULL, stage."IsInitial", stage."IsTerminal", 1
                FROM task_workflow_definitions AS definition
                CROSS JOIN (VALUES ('Backlog', 'Backlog', 1000::bigint, TRUE, FALSE), ('Todo', 'Todo', 2000::bigint, FALSE, FALSE), ('In Progress', 'InProgress', 3000::bigint, FALSE, FALSE), ('Review', 'Review', 4000::bigint, FALSE, FALSE), ('Done', 'Done', 5000::bigint, FALSE, TRUE), ('Cancelled', 'Cancelled', 6000::bigint, FALSE, TRUE)) AS stage("Name", "Category", "SortKey", "IsInitial", "IsTerminal");

                UPDATE task_items AS task
                SET "IsBlocked" = task."Status" = 'Blocked',
                    "BlockedReason" = CASE WHEN task."Status" = 'Blocked' THEN 'Migrated from legacy Blocked status.' ELSE NULL END,
                    "WorkflowStageId" = (SELECT stage."Id" FROM task_workflow_stages AS stage WHERE stage."ProjectId" = task."ProjectId" AND stage."InternalCategory" = CASE task."Status" WHEN 'NotStarted' THEN 'Todo' WHEN 'InProgress' THEN 'InProgress' WHEN 'WaitingReview' THEN 'Review' WHEN 'Completed' THEN 'Done' WHEN 'Cancelled' THEN 'Cancelled' WHEN 'Blocked' THEN CASE WHEN (SELECT COUNT(*) FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Assignee') = 1 AND NOT EXISTS (SELECT 1 FROM task_assignments AS assignee JOIN task_assignments AS reviewer ON reviewer."TaskItemId" = assignee."TaskItemId" AND reviewer."Role" = 'Reviewer' AND reviewer."UserId" = assignee."UserId" WHERE assignee."TaskItemId" = task."Id" AND assignee."Role" = 'Assignee') THEN 'InProgress' ELSE 'Todo' END ELSE 'Todo' END ORDER BY stage."SortKey" LIMIT 1);

                INSERT INTO task_migration_inventory ("Id", "TenantId", "ProjectId", "TaskItemId", "FindingCode", "Details", "CreatedAt")
                SELECT gen_random_uuid(), task."TenantId", task."ProjectId", task."Id", 'MultiplePrimaryAssigneeCandidates', 'Legacy Assignee rows retained; canonical primary assignee was not selected.', NOW() FROM task_items AS task WHERE (SELECT COUNT(*) FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Assignee') > 1;
                INSERT INTO task_migration_inventory ("Id", "TenantId", "ProjectId", "TaskItemId", "FindingCode", "Details", "CreatedAt")
                SELECT gen_random_uuid(), task."TenantId", task."ProjectId", task."Id", 'MultipleReviewerCandidates', 'Legacy Reviewer rows retained; canonical reviewer was not selected.', NOW() FROM task_items AS task WHERE (SELECT COUNT(*) FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Reviewer') > 1;
                INSERT INTO task_migration_inventory ("Id", "TenantId", "ProjectId", "TaskItemId", "FindingCode", "Details", "CreatedAt")
                SELECT DISTINCT gen_random_uuid(), task."TenantId", task."ProjectId", task."Id", 'ReviewerEqualsPrimaryAssignee', 'Canonical reviewer is left null; legacy assignment rows are retained for operator review.', NOW() FROM task_items AS task JOIN task_assignments AS assignee ON assignee."TaskItemId" = task."Id" AND assignee."Role" = 'Assignee' JOIN task_assignments AS reviewer ON reviewer."TaskItemId" = task."Id" AND reviewer."Role" = 'Reviewer' AND reviewer."UserId" = assignee."UserId";
                INSERT INTO task_migration_inventory ("Id", "TenantId", "ProjectId", "TaskItemId", "FindingCode", "Details", "CreatedAt")
                SELECT gen_random_uuid(), task."TenantId", task."ProjectId", task."Id", 'LegacyAssignmentOwner', 'Legacy Owner assignment is retained and is not interpreted as Project ownership.', NOW() FROM task_items AS task WHERE EXISTS (SELECT 1 FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Owner');
                INSERT INTO task_migration_inventory ("Id", "TenantId", "ProjectId", "TaskItemId", "FindingCode", "Details", "CreatedAt")
                SELECT gen_random_uuid(), dependency."TenantId", dependency."ProjectId", dependency."SuccessorTaskItemId", 'LegacyNonFinishToStartDependency', 'SS, FF, or SF dependency retained without conversion.', NOW() FROM task_dependencies AS dependency WHERE dependency."DependencyType" <> 'FinishToStart';

                UPDATE task_items AS task SET "PrimaryAssigneeUserId" = assignment."UserId" FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Assignee' AND (SELECT COUNT(*) FROM task_assignments AS candidate WHERE candidate."TaskItemId" = task."Id" AND candidate."Role" = 'Assignee') = 1 AND EXISTS (SELECT 1 FROM tenant_users AS tenant_user WHERE tenant_user."TenantId" = task."TenantId" AND tenant_user."UserId" = assignment."UserId" AND tenant_user."Status" = 'Active');
                UPDATE task_items AS task SET "ReviewerUserId" = assignment."UserId" FROM task_assignments AS assignment WHERE assignment."TaskItemId" = task."Id" AND assignment."Role" = 'Reviewer' AND (SELECT COUNT(*) FROM task_assignments AS candidate WHERE candidate."TaskItemId" = task."Id" AND candidate."Role" = 'Reviewer') = 1 AND (task."PrimaryAssigneeUserId" IS NULL OR task."PrimaryAssigneeUserId" <> assignment."UserId") AND EXISTS (SELECT 1 FROM tenant_users AS tenant_user WHERE tenant_user."TenantId" = task."TenantId" AND tenant_user."UserId" = assignment."UserId" AND tenant_user."Status" = 'Active');
                INSERT INTO task_item_collaborators ("Id", "TenantId", "TaskItemId", "UserId", "AddedAt", "AddedByUserId") SELECT gen_random_uuid(), assignment."TenantId", assignment."TaskItemId", assignment."UserId", assignment."AssignedAt", assignment."AssignedByUserId" FROM task_assignments AS assignment WHERE assignment."Role" = 'Support' ON CONFLICT ("TenantId", "TaskItemId", "UserId") DO NOTHING;

                CREATE OR REPLACE FUNCTION task_items_v1_scope_guard() RETURNS trigger AS $$
                DECLARE parent_parent_id uuid;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM projects WHERE "Id" = NEW."ProjectId" AND "TenantId" = NEW."TenantId" AND "WorkspaceId" = NEW."WorkspaceId") THEN RAISE EXCEPTION 'Task tenant/workspace/project scope mismatch'; END IF;
                    IF NEW."TargetGroupId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM groups WHERE "Id" = NEW."TargetGroupId" AND "TenantId" = NEW."TenantId" AND "WorkspaceId" = NEW."WorkspaceId") THEN RAISE EXCEPTION 'Task target group scope mismatch'; END IF;
                    IF NEW."PrimaryAssigneeUserId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tenant_users WHERE "TenantId" = NEW."TenantId" AND "UserId" = NEW."PrimaryAssigneeUserId" AND "Status" = 'Active') THEN RAISE EXCEPTION 'Task primary assignee tenant scope mismatch'; END IF;
                    IF NEW."ReviewerUserId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tenant_users WHERE "TenantId" = NEW."TenantId" AND "UserId" = NEW."ReviewerUserId" AND "Status" = 'Active') THEN RAISE EXCEPTION 'Task reviewer tenant scope mismatch'; END IF;
                    IF NEW."WorkflowStageId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM task_workflow_stages WHERE "Id" = NEW."WorkflowStageId" AND "ProjectId" = NEW."ProjectId" AND "TenantId" = NEW."TenantId" AND "WorkspaceId" = NEW."WorkspaceId") THEN RAISE EXCEPTION 'Task workflow stage scope mismatch'; END IF;
                    IF NEW."ParentTaskItemId" = NEW."Id" THEN RAISE EXCEPTION 'Task cannot be its own parent'; END IF;
                    IF NEW."ParentTaskItemId" IS NOT NULL THEN SELECT "ParentTaskItemId" INTO parent_parent_id FROM task_items WHERE "Id" = NEW."ParentTaskItemId"; IF parent_parent_id IS NOT NULL THEN RAISE EXCEPTION 'Task hierarchy cannot exceed one child level'; END IF; END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER task_items_v1_scope_guard_trigger BEFORE INSERT OR UPDATE OF "TenantId", "WorkspaceId", "ProjectId", "TargetGroupId", "PrimaryAssigneeUserId", "ReviewerUserId", "WorkflowStageId", "ParentTaskItemId" ON task_items FOR EACH ROW EXECUTE FUNCTION task_items_v1_scope_guard();
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_task_items_groups_TargetGroupId",
                table: "task_items",
                column: "TargetGroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_task_items_task_items_ParentTaskItemId_ProjectId",
                table: "task_items",
                columns: new[] { "ParentTaskItemId", "ProjectId" },
                principalTable: "task_items",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_task_items_task_workflow_stages_WorkflowStageId_ProjectId",
                table: "task_items",
                columns: new[] { "WorkflowStageId", "ProjectId" },
                principalTable: "task_workflow_stages",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_task_items_users_PrimaryAssigneeUserId",
                table: "task_items",
                column: "PrimaryAssigneeUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_task_items_users_ReviewerUserId",
                table: "task_items",
                column: "ReviewerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS task_items_v1_scope_guard_trigger ON task_items;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS task_items_v1_scope_guard();");

            migrationBuilder.DropForeignKey(
                name: "FK_task_items_groups_TargetGroupId",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "FK_task_items_task_items_ParentTaskItemId_ProjectId",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "FK_task_items_task_workflow_stages_WorkflowStageId_ProjectId",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "FK_task_items_users_PrimaryAssigneeUserId",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "FK_task_items_users_ReviewerUserId",
                table: "task_items");

            migrationBuilder.DropTable(
                name: "task_item_collaborators");

            migrationBuilder.DropTable(
                name: "task_migration_inventory");

            migrationBuilder.DropTable(
                name: "task_workflow_stages");

            migrationBuilder.DropTable(
                name: "task_workflow_definitions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_task_items_Id_ProjectId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ParentTaskItemId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ParentTaskItemId_ProjectId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_PrimaryAssigneeUserId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ProjectId_DeadlineAt",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ProjectId_IsBlocked",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ProjectId_PlannedEndDate",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ProjectId_WorkflowStageId_SortKey",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_ReviewerUserId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TargetGroupId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId_WorkspaceId_ProjectId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_WorkflowStageId_ProjectId",
                table: "task_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_items_effort",
                table: "task_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_items_planned_dates",
                table: "task_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_items_reviewer_not_primary",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_ProjectId_PredecessorTaskItemId",
                table: "task_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_ProjectId_SuccessorTaskItemId",
                table: "task_dependencies");

            migrationBuilder.DropColumn(
                name: "ActualStartAt",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "DeadlineAt",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "EstimatedEffortMinutes",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ParentTaskItemId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "PlannedEndDate",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "PlannedStartDate",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "PrimaryAssigneeUserId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewerUserId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "SortKey",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "TargetGroupId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "VersionNo",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "WorkflowStageId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "task_items");
        }
    }
}
