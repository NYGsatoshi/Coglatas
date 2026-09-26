using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_follow_ups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_follow_ups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_follow_ups_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_message_follow_ups_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_CreatedAt",
                table: "message_follow_ups",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_MessageId",
                table: "message_follow_ups",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_TenantId",
                table: "message_follow_ups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_TenantId_UserId_CreatedAt_Id",
                table: "message_follow_ups",
                columns: new[] { "TenantId", "UserId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_TenantId_UserId_MessageId",
                table: "message_follow_ups",
                columns: new[] { "TenantId", "UserId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_follow_ups_UserId",
                table: "message_follow_ups",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_follow_ups");
        }
    }
}
