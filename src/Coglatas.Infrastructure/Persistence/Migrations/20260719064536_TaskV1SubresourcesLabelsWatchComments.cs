using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskV1SubresourcesLabelsWatchComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_task_labels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortKey = table.Column<long>(type: "bigint", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_task_labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_task_labels_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortKey = table.Column<long>(type: "bigint", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_checklist_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_checklist_items_task_items_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "task_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_checklist_items_users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BodyPlainText = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    IsImportant = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleteReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_comments_task_items_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "task_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_comments_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_watch_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomaticSources = table.Column<int>(type: "integer", nullable: false),
                    IsExplicitOptOut = table.Column<bool>(type: "boolean", nullable: false),
                    IsWatching = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_watch_states", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_item_watch_states_task_items_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "task_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_item_watch_states_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_labels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_item_labels_project_task_labels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "project_task_labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_item_labels_task_items_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "task_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_task_labels_ProjectId",
                table: "project_task_labels",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_task_labels_TenantId",
                table: "project_task_labels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_project_task_labels_TenantId_ProjectId_Name",
                table: "project_task_labels",
                columns: new[] { "TenantId", "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_CompletedByUserId",
                table: "task_checklist_items",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_TaskItemId",
                table: "task_checklist_items",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_TenantId",
                table: "task_checklist_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_TenantId_TaskItemId_SortKey",
                table: "task_checklist_items",
                columns: new[] { "TenantId", "TaskItemId", "SortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_AuthorUserId",
                table: "task_comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_CreatedAt",
                table: "task_comments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_DeletedAt",
                table: "task_comments",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_DeletedByUserId",
                table: "task_comments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TaskItemId",
                table: "task_comments",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TenantId",
                table: "task_comments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TenantId_TaskItemId_CreatedAt",
                table: "task_comments",
                columns: new[] { "TenantId", "TaskItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_LabelId",
                table: "work_item_labels",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_TaskItemId",
                table: "work_item_labels",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_TenantId",
                table: "work_item_labels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_TenantId_TaskItemId_LabelId",
                table: "work_item_labels",
                columns: new[] { "TenantId", "TaskItemId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_item_watch_states_TaskItemId",
                table: "work_item_watch_states",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_watch_states_TenantId",
                table: "work_item_watch_states",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_watch_states_TenantId_TaskItemId_UserId",
                table: "work_item_watch_states",
                columns: new[] { "TenantId", "TaskItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_item_watch_states_UserId",
                table: "work_item_watch_states",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_checklist_items");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "work_item_labels");

            migrationBuilder.DropTable(
                name: "work_item_watch_states");

            migrationBuilder.DropTable(
                name: "project_task_labels");
        }
    }
}
