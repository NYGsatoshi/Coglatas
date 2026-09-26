using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationAndChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channels_groups_GroupId",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channels_WorkspaceId_Slug",
                table: "channels");

            migrationBuilder.RenameColumn(
                name: "Visibility",
                table: "groups",
                newName: "Status");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "workspaces",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "workspaces",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "workspaces",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PinnedAt",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PinnedByUserId",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "GroupType",
                table: "groups",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentGroupId",
                table: "groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "channels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "channels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "channels",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_CreatedByUserId",
                table: "workspaces",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_Status",
                table: "workspaces",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_posts_PinnedAt",
                table: "posts",
                column: "PinnedAt");

            migrationBuilder.CreateIndex(
                name: "IX_posts_PinnedByUserId",
                table: "posts",
                column: "PinnedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_CreatedByUserId",
                table: "groups",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_ParentGroupId",
                table: "groups",
                column: "ParentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_Status",
                table: "groups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_channels_CreatedByUserId",
                table: "channels",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_channels_GroupId_Slug",
                table: "channels",
                columns: new[] { "GroupId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_Status",
                table: "channels",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_channels_groups_GroupId",
                table: "channels",
                column: "GroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_channels_users_CreatedByUserId",
                table: "channels",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_groups_ParentGroupId",
                table: "groups",
                column: "ParentGroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_users_CreatedByUserId",
                table: "groups",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_posts_users_PinnedByUserId",
                table: "posts",
                column: "PinnedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_workspaces_users_CreatedByUserId",
                table: "workspaces",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channels_groups_GroupId",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_channels_users_CreatedByUserId",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_groups_ParentGroupId",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_users_CreatedByUserId",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_posts_users_PinnedByUserId",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "FK_workspaces_users_CreatedByUserId",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspaces_CreatedByUserId",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_workspaces_Status",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_posts_PinnedAt",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "IX_posts_PinnedByUserId",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "IX_groups_CreatedByUserId",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_groups_ParentGroupId",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_groups_Status",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_channels_CreatedByUserId",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channels_GroupId_Slug",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channels_Status",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "PinnedByUserId",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "GroupType",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "ParentGroupId",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "channels");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "groups",
                newName: "Visibility");

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "channels",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_channels_WorkspaceId_Slug",
                table: "channels",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_channels_groups_GroupId",
                table: "channels",
                column: "GroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
