using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageThreadRootContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ThreadRootMessageId",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_thread_replies",
                table: "messages",
                columns: new[] { "TenantId", "ConversationId", "ThreadRootMessageId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_ThreadRootMessageId",
                table: "messages",
                column: "ThreadRootMessageId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_messages_thread_root_not_self",
                table: "messages",
                sql: "\"ThreadRootMessageId\" IS NULL OR \"ThreadRootMessageId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_messages_ThreadRootMessageId",
                table: "messages",
                column: "ThreadRootMessageId",
                principalTable: "messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_messages_messages_ThreadRootMessageId",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_thread_replies",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_ThreadRootMessageId",
                table: "messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_messages_thread_root_not_self",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ThreadRootMessageId",
                table: "messages");
        }
    }
}
