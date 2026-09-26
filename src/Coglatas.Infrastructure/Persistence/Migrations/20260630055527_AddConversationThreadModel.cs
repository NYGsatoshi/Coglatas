using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationThreadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE messages AS m
                SET "WorkspaceId" = c."WorkspaceId"
                FROM conversations AS c
                WHERE m."ConversationId" = c."Id";
                """);

            migrationBuilder.Sql(
                """
                UPDATE conversations
                SET "Type" = 'DirectMessage'
                WHERE "Type" = 'Direct';
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkspaceId",
                table: "messages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentConversationId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootConversationId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_TenantId_WorkspaceId_CreatedAt",
                table: "messages",
                columns: new[] { "TenantId", "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_WorkspaceId",
                table: "messages",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_ParentConversationId",
                table: "conversations",
                column: "ParentConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_ProjectId",
                table: "conversations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_RootConversationId",
                table: "conversations",
                column: "RootConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_ParentConversationId",
                table: "conversations",
                columns: new[] { "TenantId", "ParentConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_WorkspaceId_ProjectId",
                table: "conversations",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_conversations_ParentConversationId",
                table: "conversations",
                column: "ParentConversationId",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_conversations_RootConversationId",
                table: "conversations",
                column: "RootConversationId",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_projects_ProjectId",
                table: "conversations",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_messages_workspaces_WorkspaceId",
                table: "messages",
                column: "WorkspaceId",
                principalTable: "workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_conversations_ParentConversationId",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_conversations_conversations_RootConversationId",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_conversations_projects_ProjectId",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_messages_workspaces_WorkspaceId",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_TenantId_WorkspaceId_CreatedAt",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_WorkspaceId",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_conversations_ParentConversationId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_ProjectId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_RootConversationId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_ParentConversationId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_WorkspaceId_ProjectId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ParentConversationId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "RootConversationId",
                table: "conversations");
        }
    }
}
