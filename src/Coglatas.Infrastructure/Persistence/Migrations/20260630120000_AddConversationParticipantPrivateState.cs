using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260630120000_AddConversationParticipantPrivateState")]
    public partial class AddConversationParticipantPrivateState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastOpenedAt",
                table: "conversation_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReadAt",
                table: "conversation_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnreadCursorMessageId",
                table: "conversation_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_LastOpenedAt",
                table: "conversation_members",
                column: "LastOpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_LastReadAt",
                table: "conversation_members",
                column: "LastReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_UnreadCursorMessageId",
                table: "conversation_members",
                column: "UnreadCursorMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_members_messages_UnreadCursorMessageId",
                table: "conversation_members",
                column: "UnreadCursorMessageId",
                principalTable: "messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversation_members_messages_UnreadCursorMessageId",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_LastOpenedAt",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_LastReadAt",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_UnreadCursorMessageId",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "LastOpenedAt",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "LastReadAt",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "UnreadCursorMessageId",
                table: "conversation_members");
        }
    }
}
