using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MessagingAndProductionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualHours",
                table: "task_assignments",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedHours",
                table: "task_assignments",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "read_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastReadMessageId",
                table: "read_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "projects",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "milestones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "conversations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeftAt",
                table: "conversation_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "conversation_members",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "attachments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerType",
                table: "attachments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoredFileName",
                table: "attachments",
                type: "character varying(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UploadedByUserId",
                table: "attachments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "message_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_attachments_attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_message_attachments_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_read_states_ConversationId",
                table: "read_states",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_read_states_LastReadMessageId",
                table: "read_states",
                column: "LastReadMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_read_states_UserId_ConversationId",
                table: "read_states",
                columns: new[] { "UserId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerUserId",
                table: "projects",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_milestones_DeletedAt",
                table: "milestones",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_messages_EditedAt",
                table: "messages",
                column: "EditedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_CreatedByUserId",
                table: "conversations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_LeftAt",
                table: "conversation_members",
                column: "LeftAt");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_OwnerType_OwnerId",
                table: "attachments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_UploadedByUserId",
                table: "attachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_AttachmentId",
                table: "message_attachments",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_MessageId_AttachmentId",
                table: "message_attachments",
                columns: new[] { "MessageId", "AttachmentId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_attachments_users_UploadedByUserId",
                table: "attachments",
                column: "UploadedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_users_CreatedByUserId",
                table: "conversations",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_projects_users_OwnerUserId",
                table: "projects",
                column: "OwnerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_read_states_conversations_ConversationId",
                table: "read_states",
                column: "ConversationId",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_read_states_messages_LastReadMessageId",
                table: "read_states",
                column: "LastReadMessageId",
                principalTable: "messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attachments_users_UploadedByUserId",
                table: "attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_conversations_users_CreatedByUserId",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_projects_users_OwnerUserId",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "FK_read_states_conversations_ConversationId",
                table: "read_states");

            migrationBuilder.DropForeignKey(
                name: "FK_read_states_messages_LastReadMessageId",
                table: "read_states");

            migrationBuilder.DropTable(
                name: "message_attachments");

            migrationBuilder.DropIndex(
                name: "IX_read_states_ConversationId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_LastReadMessageId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_read_states_UserId_ConversationId",
                table: "read_states");

            migrationBuilder.DropIndex(
                name: "IX_projects_OwnerUserId",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_milestones_DeletedAt",
                table: "milestones");

            migrationBuilder.DropIndex(
                name: "IX_messages_EditedAt",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_conversations_CreatedByUserId",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversation_members_LeftAt",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "IX_attachments_OwnerType_OwnerId",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "IX_attachments_UploadedByUserId",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "ActualHours",
                table: "task_assignments");

            migrationBuilder.DropColumn(
                name: "EstimatedHours",
                table: "task_assignments");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "read_states");

            migrationBuilder.DropColumn(
                name: "LastReadMessageId",
                table: "read_states");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "milestones");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "OwnerType",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "StoredFileName",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "attachments");
        }
    }
}
