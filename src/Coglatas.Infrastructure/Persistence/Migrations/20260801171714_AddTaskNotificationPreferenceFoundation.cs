using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskNotificationPreferenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DefaultTaskDeadlineDigestLocalTime",
                table: "workspaces",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));

            migrationBuilder.AddColumn<long>(
                name: "TaskNotificationSettingsVersion",
                table: "workspaces",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TaskDeadlineDigestLocalTime",
                table: "workspace_members",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaskNotificationPreferenceVersion",
                table: "workspace_members",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "LogicalKey",
                table: "notifications",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_UserId_LogicalKey",
                table: "notifications",
                columns: new[] { "TenantId", "UserId", "LogicalKey" },
                unique: true,
                filter: "\"LogicalKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_UserId_LogicalKey",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "DefaultTaskDeadlineDigestLocalTime",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "TaskNotificationSettingsVersion",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "TaskDeadlineDigestLocalTime",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "TaskNotificationPreferenceVersion",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "LogicalKey",
                table: "notifications");
        }
    }
}
