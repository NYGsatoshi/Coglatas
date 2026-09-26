using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workspaces_Slug",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspace_members_WorkspaceId_UserId",
                table: "workspace_members");

            migrationBuilder.DropIndex(
                name: "IX_user_layouts_UserId_WorkspaceId_Name",
                table: "user_layouts");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_PredecessorTaskItemId_SuccessorTaskItemId",
                table: "task_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_task_assignments_TaskItemId_UserId_Role",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "IX_read_states_UserId_ConversationId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_UserId_ScopeType_ScopeId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_ProfileKey",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_UserId_WorkspaceId_Name",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_projects_WorkspaceId_Slug",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_project_members_ProjectId_UserId",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "IX_message_attachments_MessageId_AttachmentId",
                table: "message_attachments");

            migrationBuilder.DropIndex(
                name: "IX_groups_WorkspaceId_Slug",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_group_members_GroupId_UserId",
                table: "group_members");

            migrationBuilder.DropIndex(
                name: "IX_form_responses_FormId_RespondentUserId",
                table: "form_responses");

            migrationBuilder.DropIndex(
                name: "IX_form_answers_FormResponseId_FormQuestionId",
                table: "form_answers");

            migrationBuilder.DropIndex(
                name: "IX_event_attendances_EventId_UserId",
                table: "event_attendances");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_ConversationId_UserId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_channels_GroupId_Slug",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channel_members_ChannelId_UserId",
                table: "channel_members");

            migrationBuilder.DropIndex(
                name: "IX_artifact_versions_ArtifactId_VersionNumber",
                table: "artifact_versions");

            migrationBuilder.DropIndex(
                name: "IX_announcement_reads_AnnouncementId_UserId",
                table: "announcement_reads");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "workspaces",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "workspace_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "user_layouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "task_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "task_dependencies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "task_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "security_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "read_states",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "radial_menu_profiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "radial_menu_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "projects",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "project_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "posts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "post_threads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "milestones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "message_attachments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "invites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "internal_forms",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "group_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "form_responses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "form_questions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "form_answers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "file_scan_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "feedback",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "event_attendances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "conversations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "conversation_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "comments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "channels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "channel_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "attachments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "artifacts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "artifact_versions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "announcements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "announcement_reads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "activity_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "activity_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PrimaryDomain = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "tenants",
                columns: new[] { "Id", "Name", "Slug", "DisplayName", "PrimaryDomain", "Status", "PlanId", "CreatedAt", "UpdatedAt", "DeletedAt" },
                values: new object[]
                {
                    new Guid("11111111-1111-1111-1111-111111111111"),
                    "Default Tenant",
                    "default",
                    "Default Tenant",
                    null,
                    "Active",
                    null,
                    new DateTimeOffset(new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero),
                    null,
                    null
                });

            migrationBuilder.CreateTable(
                name: "tenant_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_users_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tenant_users_users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tenant_users_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId",
                table: "workspaces",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId_Slug",
                table: "workspaces",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_TenantId",
                table: "workspace_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_TenantId_WorkspaceId_UserId",
                table: "workspace_members",
                columns: new[] { "TenantId", "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_layouts_TenantId",
                table: "user_layouts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_user_layouts_TenantId_UserId_WorkspaceId_Name",
                table: "user_layouts",
                columns: new[] { "TenantId", "UserId", "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_items_TenantId",
                table: "task_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_PredecessorTaskItemId",
                table: "task_dependencies",
                column: "PredecessorTaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_TenantId",
                table: "task_dependencies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_TenantId_PredecessorTaskItemId_SuccessorT~",
                table: "task_dependencies",
                columns: new[] { "TenantId", "PredecessorTaskItemId", "SuccessorTaskItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TaskItemId",
                table: "task_assignments",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TenantId",
                table: "task_assignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TenantId_TaskItemId_UserId_Role",
                table: "task_assignments",
                columns: new[] { "TenantId", "TaskItemId", "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_events_TenantId",
                table: "security_events",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_read_states_TenantId",
                table: "read_states",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_read_states_TenantId_UserId_ConversationId",
                table: "read_states",
                columns: new[] { "TenantId", "UserId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_read_states_TenantId_UserId_ScopeType_ScopeId",
                table: "read_states",
                columns: new[] { "TenantId", "UserId", "ScopeType", "ScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_read_states_UserId",
                table: "read_states",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_TenantId",
                table: "radial_menu_profiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_TenantId_ProfileKey",
                table: "radial_menu_profiles",
                columns: new[] { "TenantId", "ProfileKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_TenantId_UserId_WorkspaceId_Name",
                table: "radial_menu_profiles",
                columns: new[] { "TenantId", "UserId", "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_items_TenantId",
                table: "radial_menu_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId",
                table: "projects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_WorkspaceId_Slug",
                table: "projects",
                columns: new[] { "TenantId", "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_members_ProjectId",
                table: "project_members",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_members_TenantId",
                table: "project_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_project_members_TenantId_ProjectId_UserId",
                table: "project_members",
                columns: new[] { "TenantId", "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_posts_TenantId",
                table: "posts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_post_threads_TenantId",
                table: "post_threads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId",
                table: "notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_milestones_TenantId",
                table: "milestones",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_messages_TenantId",
                table: "messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_MessageId",
                table: "message_attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_TenantId",
                table: "message_attachments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_TenantId_MessageId_AttachmentId",
                table: "message_attachments",
                columns: new[] { "TenantId", "MessageId", "AttachmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invites_TenantId",
                table: "invites",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_TenantId",
                table: "internal_forms",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_TenantId",
                table: "groups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_TenantId_WorkspaceId_Slug",
                table: "groups",
                columns: new[] { "TenantId", "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_members_GroupId",
                table: "group_members",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_group_members_TenantId",
                table: "group_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_group_members_TenantId_GroupId_UserId",
                table: "group_members",
                columns: new[] { "TenantId", "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_TenantId",
                table: "form_responses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_TenantId_FormId_RespondentUserId",
                table: "form_responses",
                columns: new[] { "TenantId", "FormId", "RespondentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_questions_TenantId",
                table: "form_questions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_TenantId",
                table: "form_answers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_TenantId_FormResponseId_FormQuestionId",
                table: "form_answers",
                columns: new[] { "TenantId", "FormResponseId", "FormQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_results_TenantId",
                table: "file_scan_results",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_TenantId",
                table: "feedback",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_TenantId",
                table: "event_attendances",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_TenantId_EventId_UserId",
                table: "event_attendances",
                columns: new[] { "TenantId", "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId",
                table: "conversations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_ConversationId",
                table: "conversation_members",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_TenantId",
                table: "conversation_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_TenantId_ConversationId_UserId",
                table: "conversation_members",
                columns: new[] { "TenantId", "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_TenantId",
                table: "comments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_channels_TenantId",
                table: "channels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_channels_TenantId_GroupId_Slug",
                table: "channels",
                columns: new[] { "TenantId", "GroupId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_ChannelId",
                table: "channel_members",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_TenantId",
                table: "channel_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_TenantId_ChannelId_UserId",
                table: "channel_members",
                columns: new[] { "TenantId", "ChannelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId",
                table: "audit_logs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_TenantId",
                table: "attachments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_TenantId",
                table: "artifacts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_ArtifactId",
                table: "artifact_versions",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_TenantId",
                table: "artifact_versions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_TenantId_ArtifactId_VersionNumber",
                table: "artifact_versions",
                columns: new[] { "TenantId", "ArtifactId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcements_TenantId",
                table: "announcements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_reads_AnnouncementId",
                table: "announcement_reads",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_reads_TenantId",
                table: "announcement_reads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_reads_TenantId_AnnouncementId_UserId",
                table: "announcement_reads",
                columns: new[] { "TenantId", "AnnouncementId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_activity_logs_TenantId",
                table: "activity_logs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_TenantId",
                table: "activity_events",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_CreatedAt",
                table: "tenant_users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_InvitedByUserId",
                table: "tenant_users",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_Status",
                table: "tenant_users",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_TenantId",
                table: "tenant_users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_TenantId_UserId",
                table: "tenant_users",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_TenantId_UserId_Status",
                table: "tenant_users",
                columns: new[] { "TenantId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_UserId",
                table: "tenant_users",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_CreatedAt",
                table: "tenants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_DeletedAt",
                table: "tenants",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_PrimaryDomain",
                table: "tenants",
                column: "PrimaryDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Slug",
                table: "tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Status",
                table: "tenants",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_users");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_workspaces_TenantId",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspaces_TenantId_Slug",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspace_members_TenantId",
                table: "workspace_members");

            migrationBuilder.DropIndex(
                name: "IX_workspace_members_TenantId_WorkspaceId_UserId",
                table: "workspace_members");

            migrationBuilder.DropIndex(
                name: "IX_user_layouts_TenantId",
                table: "user_layouts");

            migrationBuilder.DropIndex(
                name: "IX_user_layouts_TenantId_UserId_WorkspaceId_Name",
                table: "user_layouts");

            migrationBuilder.DropIndex(
                name: "IX_task_items_TenantId",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_PredecessorTaskItemId",
                table: "task_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_TenantId",
                table: "task_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_task_dependencies_TenantId_PredecessorTaskItemId_SuccessorT~",
                table: "task_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_task_assignments_TaskItemId",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "IX_task_assignments_TenantId",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "IX_task_assignments_TenantId_TaskItemId_UserId_Role",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "IX_security_events_TenantId",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_read_states_TenantId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_TenantId_UserId_ConversationId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_TenantId_UserId_ScopeType_ScopeId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_UserId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_TenantId",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_TenantId_ProfileKey",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_TenantId_UserId_WorkspaceId_Name",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_items_TenantId",
                table: "radial_menu_items");

            migrationBuilder.DropIndex(
                name: "IX_projects_TenantId",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_projects_TenantId_WorkspaceId_Slug",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_project_members_ProjectId",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "IX_project_members_TenantId",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "IX_project_members_TenantId_ProjectId_UserId",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "IX_posts_TenantId",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "IX_post_threads_TenantId",
                table: "post_threads");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_milestones_TenantId",
                table: "milestones");

            migrationBuilder.DropIndex(
                name: "IX_messages_TenantId",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_message_attachments_MessageId",
                table: "message_attachments");

            migrationBuilder.DropIndex(
                name: "IX_message_attachments_TenantId",
                table: "message_attachments");

            migrationBuilder.DropIndex(
                name: "IX_message_attachments_TenantId_MessageId_AttachmentId",
                table: "message_attachments");

            migrationBuilder.DropIndex(
                name: "IX_invites_TenantId",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_internal_forms_TenantId",
                table: "internal_forms");

            migrationBuilder.DropIndex(
                name: "IX_groups_TenantId",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_groups_TenantId_WorkspaceId_Slug",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_group_members_GroupId",
                table: "group_members");

            migrationBuilder.DropIndex(
                name: "IX_group_members_TenantId",
                table: "group_members");

            migrationBuilder.DropIndex(
                name: "IX_group_members_TenantId_GroupId_UserId",
                table: "group_members");

            migrationBuilder.DropIndex(
                name: "IX_form_responses_TenantId",
                table: "form_responses");

            migrationBuilder.DropIndex(
                name: "IX_form_responses_TenantId_FormId_RespondentUserId",
                table: "form_responses");

            migrationBuilder.DropIndex(
                name: "IX_form_questions_TenantId",
                table: "form_questions");

            migrationBuilder.DropIndex(
                name: "IX_form_answers_TenantId",
                table: "form_answers");

            migrationBuilder.DropIndex(
                name: "IX_form_answers_TenantId_FormResponseId_FormQuestionId",
                table: "form_answers");

            migrationBuilder.DropIndex(
                name: "IX_file_scan_results_TenantId",
                table: "file_scan_results");

            migrationBuilder.DropIndex(
                name: "IX_feedback_TenantId",
                table: "feedback");

            migrationBuilder.DropIndex(
                name: "IX_event_attendances_TenantId",
                table: "event_attendances");

            migrationBuilder.DropIndex(
                name: "IX_event_attendances_TenantId_EventId_UserId",
                table: "event_attendances");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_ConversationId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_TenantId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_TenantId_ConversationId_UserId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_comments_TenantId",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "IX_channels_TenantId",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channels_TenantId_GroupId_Slug",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channel_members_ChannelId",
                table: "channel_members");

            migrationBuilder.DropIndex(
                name: "IX_channel_members_TenantId",
                table: "channel_members");

            migrationBuilder.DropIndex(
                name: "IX_channel_members_TenantId_ChannelId_UserId",
                table: "channel_members");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_TenantId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_attachments_TenantId",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "IX_artifacts_TenantId",
                table: "artifacts");

            migrationBuilder.DropIndex(
                name: "IX_artifact_versions_ArtifactId",
                table: "artifact_versions");

            migrationBuilder.DropIndex(
                name: "IX_artifact_versions_TenantId",
                table: "artifact_versions");

            migrationBuilder.DropIndex(
                name: "IX_artifact_versions_TenantId_ArtifactId_VersionNumber",
                table: "artifact_versions");

            migrationBuilder.DropIndex(
                name: "IX_announcements_TenantId",
                table: "announcements");

            migrationBuilder.DropIndex(
                name: "IX_announcement_reads_AnnouncementId",
                table: "announcement_reads");

            migrationBuilder.DropIndex(
                name: "IX_announcement_reads_TenantId",
                table: "announcement_reads");

            migrationBuilder.DropIndex(
                name: "IX_announcement_reads_TenantId_AnnouncementId_UserId",
                table: "announcement_reads");

            migrationBuilder.DropIndex(
                name: "IX_activity_logs_TenantId",
                table: "activity_logs");

            migrationBuilder.DropIndex(
                name: "IX_activity_events_TenantId",
                table: "activity_events");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "user_layouts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "task_dependencies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "task_assignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "read_states");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "radial_menu_profiles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "radial_menu_items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "project_members");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "post_threads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "milestones");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "message_attachments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "invites");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "internal_forms");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "form_answers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "file_scan_results");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "feedback");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "event_attendances");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "channel_members");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "artifact_versions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "announcement_reads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "activity_logs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "activity_events");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_Slug",
                table: "workspaces",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_WorkspaceId_UserId",
                table: "workspace_members",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_layouts_UserId_WorkspaceId_Name",
                table: "user_layouts",
                columns: new[] { "UserId", "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_PredecessorTaskItemId_SuccessorTaskItemId",
                table: "task_dependencies",
                columns: new[] { "PredecessorTaskItemId", "SuccessorTaskItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_TaskItemId_UserId_Role",
                table: "task_assignments",
                columns: new[] { "TaskItemId", "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_read_states_UserId_ConversationId",
                table: "read_states",
                columns: new[] { "UserId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_read_states_UserId_ScopeType_ScopeId",
                table: "read_states",
                columns: new[] { "UserId", "ScopeType", "ScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_ProfileKey",
                table: "radial_menu_profiles",
                column: "ProfileKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_UserId_WorkspaceId_Name",
                table: "radial_menu_profiles",
                columns: new[] { "UserId", "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_WorkspaceId_Slug",
                table: "projects",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_members_ProjectId_UserId",
                table: "project_members",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_MessageId_AttachmentId",
                table: "message_attachments",
                columns: new[] { "MessageId", "AttachmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_groups_WorkspaceId_Slug",
                table: "groups",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_members_GroupId_UserId",
                table: "group_members",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_FormId_RespondentUserId",
                table: "form_responses",
                columns: new[] { "FormId", "RespondentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_FormResponseId_FormQuestionId",
                table: "form_answers",
                columns: new[] { "FormResponseId", "FormQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_EventId_UserId",
                table: "event_attendances",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_ConversationId_UserId",
                table: "conversation_members",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_GroupId_Slug",
                table: "channels",
                columns: new[] { "GroupId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_ChannelId_UserId",
                table: "channel_members",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_ArtifactId_VersionNumber",
                table: "artifact_versions",
                columns: new[] { "ArtifactId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcement_reads_AnnouncementId_UserId",
                table: "announcement_reads",
                columns: new[] { "AnnouncementId", "UserId" },
                unique: true);
        }
    }
}
