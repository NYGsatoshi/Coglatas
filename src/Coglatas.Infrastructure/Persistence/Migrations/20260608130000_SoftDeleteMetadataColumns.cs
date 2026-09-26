using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteMetadataColumns : Migration
    {
        private static readonly string[] Tables =
        [
            "activity_events",
            "announcements",
            "artifacts",
            "artifact_versions",
            "attachments",
            "channels",
            "comments",
            "feedback",
            "groups",
            "internal_forms",
            "messages",
            "milestones",
            "posts",
            "post_threads",
            "projects",
            "task_items",
            "tenants",
            "users",
            "workspaces"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "DeleteReason",
                    table: table,
                    type: "character varying(500)",
                    maxLength: 500,
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "DeletedByUserId",
                    table: table,
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: $"IX_{table}_DeletedByUserId",
                    table: table,
                    column: "DeletedByUserId");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (var i = Tables.Length - 1; i >= 0; i--)
            {
                var table = Tables[i];

                migrationBuilder.DropIndex(
                    name: $"IX_{table}_DeletedByUserId",
                    table: table);

                migrationBuilder.DropColumn(
                    name: "DeletedByUserId",
                    table: table);

                migrationBuilder.DropColumn(
                    name: "DeleteReason",
                    table: table);
            }
        }
    }
}
