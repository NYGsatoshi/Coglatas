using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationParticipantAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanCreateThread",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageMembers",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanPost",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanRead",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "conversation_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemovedByUserId",
                table: "conversation_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_RemovedAt",
                table: "conversation_members",
                column: "RemovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_RemovedByUserId",
                table: "conversation_members",
                column: "RemovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_members_users_RemovedByUserId",
                table: "conversation_members",
                column: "RemovedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversation_members_users_RemovedByUserId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_RemovedAt",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_RemovedByUserId",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "CanCreateThread",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "CanManageMembers",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "CanPost",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "CanRead",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "RemovedByUserId",
                table: "conversation_members");
        }
    }
}
