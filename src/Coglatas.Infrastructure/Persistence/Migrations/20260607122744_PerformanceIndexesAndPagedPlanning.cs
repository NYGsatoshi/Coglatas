using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexesAndPagedPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId_CreatedAt",
                table: "workspaces",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId_Status",
                table: "workspaces",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_DueDate",
                table: "task_items",
                columns: new[] { "TenantId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId_ProjectId_Status",
                table: "task_items",
                columns: new[] { "TenantId", "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TenantId_TaskItemId",
                table: "task_assignments",
                columns: new[] { "TenantId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TenantId_UserId",
                table: "task_assignments",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_TenantId_CreatedAt",
                table: "security_events",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_TenantId_EventType",
                table: "security_events",
                columns: new[] { "TenantId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_CreatedAt",
                table: "projects",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_GroupId_Status",
                table: "projects",
                columns: new[] { "TenantId", "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_Status",
                table: "projects",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_posts_TenantId_ChannelId_CreatedAt",
                table: "posts",
                columns: new[] { "TenantId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_post_threads_TenantId_PostId_CreatedAt",
                table: "post_threads",
                columns: new[] { "TenantId", "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_UserId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "TenantId", "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_TenantId_ConversationId_CreatedAt",
                table: "messages",
                columns: new[] { "TenantId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_groups_TenantId_Status",
                table: "groups",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_groups_TenantId_WorkspaceId",
                table: "groups",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_UpdatedAt",
                table: "conversations",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_WorkspaceId",
                table: "conversations",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_TenantId_UserId",
                table: "conversation_members",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_comments_TenantId_TargetType_TargetId_CreatedAt",
                table: "comments",
                columns: new[] { "TenantId", "TargetType", "TargetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_channels_TenantId_GroupId",
                table: "channels",
                columns: new[] { "TenantId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_channels_TenantId_Status",
                table: "channels",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId_Action",
                table: "audit_logs",
                columns: new[] { "TenantId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId_ActorUserId",
                table: "audit_logs",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId_CreatedAt",
                table: "audit_logs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_TenantId_ProjectId",
                table: "artifacts",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_TenantId_Status",
                table: "artifacts",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_workspaces_TenantId_CreatedAt", table: "workspaces");
            migrationBuilder.DropIndex(name: "IX_workspaces_TenantId_Status", table: "workspaces");
            migrationBuilder.DropIndex(name: "IX_task_items_TenantId_DueDate", table: "task_items");
            migrationBuilder.DropIndex(name: "IX_task_items_TenantId_ProjectId_Status", table: "task_items");
            migrationBuilder.DropIndex(name: "IX_task_assignments_TenantId_TaskItemId", table: "task_assignments");
            migrationBuilder.DropIndex(name: "IX_task_assignments_TenantId_UserId", table: "task_assignments");
            migrationBuilder.DropIndex(name: "IX_security_events_TenantId_CreatedAt", table: "security_events");
            migrationBuilder.DropIndex(name: "IX_security_events_TenantId_EventType", table: "security_events");
            migrationBuilder.DropIndex(name: "IX_projects_TenantId_CreatedAt", table: "projects");
            migrationBuilder.DropIndex(name: "IX_projects_TenantId_GroupId_Status", table: "projects");
            migrationBuilder.DropIndex(name: "IX_projects_TenantId_Status", table: "projects");
            migrationBuilder.DropIndex(name: "IX_posts_TenantId_ChannelId_CreatedAt", table: "posts");
            migrationBuilder.DropIndex(name: "IX_post_threads_TenantId_PostId_CreatedAt", table: "post_threads");
            migrationBuilder.DropIndex(name: "IX_notifications_TenantId_UserId_IsRead_CreatedAt", table: "notifications");
            migrationBuilder.DropIndex(name: "IX_messages_TenantId_ConversationId_CreatedAt", table: "messages");
            migrationBuilder.DropIndex(name: "IX_groups_TenantId_Status", table: "groups");
            migrationBuilder.DropIndex(name: "IX_groups_TenantId_WorkspaceId", table: "groups");
            migrationBuilder.DropIndex(name: "IX_conversations_TenantId_UpdatedAt", table: "conversations");
            migrationBuilder.DropIndex(name: "IX_conversations_TenantId_WorkspaceId", table: "conversations");
            migrationBuilder.DropIndex(name: "IX_conversation_members_TenantId_UserId", table: "conversation_members");
            migrationBuilder.DropIndex(name: "IX_comments_TenantId_TargetType_TargetId_CreatedAt", table: "comments");
            migrationBuilder.DropIndex(name: "IX_channels_TenantId_GroupId", table: "channels");
            migrationBuilder.DropIndex(name: "IX_channels_TenantId_Status", table: "channels");
            migrationBuilder.DropIndex(name: "IX_audit_logs_TenantId_Action", table: "audit_logs");
            migrationBuilder.DropIndex(name: "IX_audit_logs_TenantId_ActorUserId", table: "audit_logs");
            migrationBuilder.DropIndex(name: "IX_audit_logs_TenantId_CreatedAt", table: "audit_logs");
            migrationBuilder.DropIndex(name: "IX_artifacts_TenantId_ProjectId", table: "artifacts");
            migrationBuilder.DropIndex(name: "IX_artifacts_TenantId_Status", table: "artifacts");
        }
    }
}
