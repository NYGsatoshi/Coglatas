using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingRealtimeContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastReadSequence",
                table: "read_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "StateVersion",
                table: "read_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "messages",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_messages_TenantId_ConversationId_AuthorUserId_ClientRequest~",
                table: "messages",
                columns: new[] { "TenantId", "ConversationId", "AuthorUserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_TenantId_ConversationId_AuthorUserId_ClientRequest~",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "LastReadSequence",
                table: "read_states");

            migrationBuilder.DropColumn(
                name: "StateVersion",
                table: "read_states");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "messages");
        }
    }
}
