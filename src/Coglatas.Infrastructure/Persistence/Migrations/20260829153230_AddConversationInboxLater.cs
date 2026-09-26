using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationInboxLater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLater",
                table: "conversation_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_TenantId_UserId_IsLater",
                table: "conversation_members",
                columns: new[] { "TenantId", "UserId", "IsLater" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_members_TenantId_UserId_IsLater",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "IsLater",
                table: "conversation_members");
        }
    }
}
