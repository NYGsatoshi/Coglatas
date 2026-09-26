using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskV1WorkflowCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewResolvedAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewResolvedByUserId",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReturnReason",
                table: "task_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "task_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewSubmittedAt",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_items_ProjectId_TargetGroupId_PrimaryAssigneeUserId_Wo~",
                table: "task_items",
                columns: new[] { "ProjectId", "TargetGroupId", "PrimaryAssigneeUserId", "WorkflowStageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_items_ProjectId_TargetGroupId_PrimaryAssigneeUserId_Wo~",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedAt",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedByUserId",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewReturnReason",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "ReviewSubmittedAt",
                table: "task_items");
        }
    }
}
