using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MyTasksProjectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_work_item_watch_states_TenantId_UserId_IsWatching_TaskItemId",
                table: "work_item_watch_states",
                columns: new[] { "TenantId", "UserId", "IsWatching", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_WorkspaceId_CreatedByUserId",
                table: "task_items",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_WorkspaceId_IsBlocked_Priority_Deadline~",
                table: "task_items",
                columns: new[] { "TenantId", "WorkspaceId", "IsBlocked", "Priority", "DeadlineAt" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_WorkspaceId_PrimaryAssigneeUserId",
                table: "task_items",
                columns: new[] { "TenantId", "WorkspaceId", "PrimaryAssigneeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_WorkspaceId_ReviewerUserId",
                table: "task_items",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_item_collaborators_TenantId_UserId_TaskItemId",
                table: "task_item_collaborators",
                columns: new[] { "TenantId", "UserId", "TaskItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_item_watch_states_TenantId_UserId_IsWatching_TaskItemId",
                table: "work_item_watch_states");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId_WorkspaceId_CreatedByUserId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId_WorkspaceId_IsBlocked_Priority_Deadline~",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId_WorkspaceId_PrimaryAssigneeUserId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId_WorkspaceId_ReviewerUserId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_item_collaborators_TenantId_UserId_TaskItemId",
                table: "task_item_collaborators");
        }
    }
}
