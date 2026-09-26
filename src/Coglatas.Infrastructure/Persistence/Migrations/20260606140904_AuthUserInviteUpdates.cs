using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthUserInviteUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invites_users_CreatedByUserId",
                table: "invites");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "invites",
                newName: "InvitedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_invites_CreatedByUserId",
                table: "invites",
                newName: "IX_invites_InvitedByUserId");

            migrationBuilder.AddColumn<string>(
                name: "SystemRole",
                table: "users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "invites",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_SystemRole",
                table: "users",
                column: "SystemRole");

            migrationBuilder.CreateIndex(
                name: "IX_invites_AcceptedAt",
                table: "invites",
                column: "AcceptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_invites_RevokedAt",
                table: "invites",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_invites_TokenHash",
                table: "invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_invites_users_InvitedByUserId",
                table: "invites",
                column: "InvitedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invites_users_InvitedByUserId",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_users_SystemRole",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_invites_AcceptedAt",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_invites_RevokedAt",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_invites_TokenHash",
                table: "invites");

            migrationBuilder.DropColumn(
                name: "SystemRole",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "invites");

            migrationBuilder.RenameColumn(
                name: "InvitedByUserId",
                table: "invites",
                newName: "CreatedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_invites_InvitedByUserId",
                table: "invites",
                newName: "IX_invites_CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_invites_users_CreatedByUserId",
                table: "invites",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
