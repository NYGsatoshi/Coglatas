using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventsAttendanceCalendarBasics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_announcements_users_CreatedByUserId",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_announcements_workspaces_WorkspaceId",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_users_RecipientUserId",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_workspaces_WorkspaceId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_RecipientUserId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_SourceType_SourceId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_WorkspaceId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_TargetType_TargetId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "audit_logs");

            migrationBuilder.RenameColumn(
                name: "WorkspaceId",
                table: "notifications",
                newName: "RelatedEntityId");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "notifications",
                newName: "NotificationType");

            migrationBuilder.RenameColumn(
                name: "SourceId",
                table: "notifications",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "announcements",
                newName: "AuthorUserId");

            migrationBuilder.RenameIndex(
                name: "IX_announcements_CreatedByUserId",
                table: "announcements",
                newName: "IX_announcements_AuthorUserId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "user_layouts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "ScopeId",
                table: "user_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "user_layouts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContextType",
                table: "radial_menu_profiles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProfileKey",
                table: "radial_menu_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommandKey",
                table: "radial_menu_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "radial_menu_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "panel_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "DefaultHeight",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPosition",
                table: "panel_definitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DefaultWidth",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosable",
                table: "panel_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDockable",
                table: "panel_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "panel_definitions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "panel_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityType",
                table: "notifications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "feature_modules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "DefaultRoute",
                table: "feature_modules",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "feature_modules",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredRole",
                table: "feature_modules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "feature_modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "command_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContextType",
                table: "command_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "command_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "command_definitions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "command_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "command_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntityId",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "audit_logs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "audit_logs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "audit_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactType",
                table: "artifacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "artifacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "artifact_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkspaceId",
                table: "announcements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "announcements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "announcements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "announcements",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttendanceDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    BringItemsText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activity_events_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_events_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_events_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_events_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_security_events_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "event_attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_attendances_activity_events_EventId",
                        column: x => x.EventId,
                        principalTable: "activity_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_attendances_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_layouts_UserId_ScopeType_ScopeId",
                table: "user_layouts",
                columns: new[] { "UserId", "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_ProfileKey",
                table: "radial_menu_profiles",
                column: "ProfileKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_panel_definitions_FeatureModuleId_SortOrder",
                table: "panel_definitions",
                columns: new[] { "FeatureModuleId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_DeletedAt",
                table: "notifications",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RelatedEntityType_RelatedEntityId",
                table: "notifications",
                columns: new[] { "RelatedEntityType", "RelatedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId",
                table: "notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_IsRead_DeletedAt",
                table: "notifications",
                columns: new[] { "UserId", "IsRead", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_command_definitions_ContextType_SortOrder",
                table: "command_definitions",
                columns: new[] { "ContextType", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Action",
                table: "audit_logs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_GroupId",
                table: "audit_logs",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ProjectId",
                table: "audit_logs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_Status",
                table: "artifacts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_DeletedAt",
                table: "artifact_versions",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_ChannelId",
                table: "announcements",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_ExpiresAt",
                table: "announcements",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_IsPinned",
                table: "announcements",
                column: "IsPinned");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_AttendanceDeadline",
                table: "activity_events",
                column: "AttendanceDeadline");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_CreatedAt",
                table: "activity_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_CreatedByUserId",
                table: "activity_events",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_DeletedAt",
                table: "activity_events",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_EndsAt",
                table: "activity_events",
                column: "EndsAt");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_GroupId",
                table: "activity_events",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_ProjectId",
                table: "activity_events",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_StartsAt",
                table: "activity_events",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_Status",
                table: "activity_events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_WorkspaceId",
                table: "activity_events",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_CreatedAt",
                table: "event_attendances",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_EventId",
                table: "event_attendances",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_EventId_UserId",
                table: "event_attendances",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_Status",
                table: "event_attendances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_event_attendances_UserId",
                table: "event_attendances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_CreatedAt",
                table: "security_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_Email",
                table: "security_events",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_EventType",
                table: "security_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_Severity",
                table: "security_events",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_security_events_UserId",
                table: "security_events",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_channels_ChannelId",
                table: "announcements",
                column: "ChannelId",
                principalTable: "channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_users_AuthorUserId",
                table: "announcements",
                column: "AuthorUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_workspaces_WorkspaceId",
                table: "announcements",
                column: "WorkspaceId",
                principalTable: "workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_groups_GroupId",
                table: "audit_logs",
                column: "GroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_projects_ProjectId",
                table: "audit_logs",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_users_UserId",
                table: "notifications",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_announcements_channels_ChannelId",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_announcements_users_AuthorUserId",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_announcements_workspaces_WorkspaceId",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_groups_GroupId",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_projects_ProjectId",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_users_UserId",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "event_attendances");

            migrationBuilder.DropTable(
                name: "security_events");

            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropIndex(
                name: "IX_user_layouts_UserId_ScopeType_ScopeId",
                table: "user_layouts");

            migrationBuilder.DropIndex(
                name: "IX_radial_menu_profiles_ProfileKey",
                table: "radial_menu_profiles");

            migrationBuilder.DropIndex(
                name: "IX_panel_definitions_FeatureModuleId_SortOrder",
                table: "panel_definitions");

            migrationBuilder.DropIndex(
                name: "IX_notifications_DeletedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_RelatedEntityType_RelatedEntityId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId_IsRead_DeletedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_command_definitions_ContextType_SortOrder",
                table: "command_definitions");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_Action",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_GroupId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_ProjectId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_artifacts_Status",
                table: "artifacts");

            migrationBuilder.DropIndex(
                name: "IX_artifact_versions_DeletedAt",
                table: "artifact_versions");

            migrationBuilder.DropIndex(
                name: "IX_announcements_ChannelId",
                table: "announcements");

            migrationBuilder.DropIndex(
                name: "IX_announcements_ExpiresAt",
                table: "announcements");

            migrationBuilder.DropIndex(
                name: "IX_announcements_IsPinned",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "user_layouts");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "user_layouts");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "user_layouts");

            migrationBuilder.DropColumn(
                name: "ContextType",
                table: "radial_menu_profiles");

            migrationBuilder.DropColumn(
                name: "ProfileKey",
                table: "radial_menu_profiles");

            migrationBuilder.DropColumn(
                name: "CommandKey",
                table: "radial_menu_items");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "radial_menu_items");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "DefaultHeight",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "DefaultPosition",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "DefaultWidth",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "IsClosable",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "IsDockable",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "RequiredPermission",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "panel_definitions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "RelatedEntityType",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "feature_modules");

            migrationBuilder.DropColumn(
                name: "DefaultRoute",
                table: "feature_modules");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "feature_modules");

            migrationBuilder.DropColumn(
                name: "RequiredRole",
                table: "feature_modules");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "feature_modules");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "ContextType",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "RequiredPermission",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "command_definitions");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "ArtifactType",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "artifact_versions");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "announcements");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "notifications",
                newName: "SourceId");

            migrationBuilder.RenameColumn(
                name: "RelatedEntityId",
                table: "notifications",
                newName: "WorkspaceId");

            migrationBuilder.RenameColumn(
                name: "NotificationType",
                table: "notifications",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "AuthorUserId",
                table: "announcements",
                newName: "CreatedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_announcements_AuthorUserId",
                table: "announcements",
                newName: "IX_announcements_CreatedByUserId");

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientUserId",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "notifications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "audit_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkspaceId",
                table: "announcements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId",
                table: "notifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_SourceType_SourceId",
                table: "notifications",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_WorkspaceId",
                table: "notifications",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TargetType_TargetId",
                table: "audit_logs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_users_CreatedByUserId",
                table: "announcements",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_workspaces_WorkspaceId",
                table: "announcements",
                column: "WorkspaceId",
                principalTable: "workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_users_RecipientUserId",
                table: "notifications",
                column: "RecipientUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_workspaces_WorkspaceId",
                table: "notifications",
                column: "WorkspaceId",
                principalTable: "workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
