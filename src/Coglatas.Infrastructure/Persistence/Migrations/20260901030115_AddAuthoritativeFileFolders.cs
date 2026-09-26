using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoritativeFileFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleteReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_folders_file_folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "file_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_folders_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_folder_placements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_folder_placements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_folder_placements_file_folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "file_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_folder_placements_file_objects_FileObjectId",
                        column: x => x.FileObjectId,
                        principalTable: "file_objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_folder_placements_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_CreatedAt",
                table: "file_folder_placements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_FileObjectId",
                table: "file_folder_placements",
                column: "FileObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_FolderId",
                table: "file_folder_placements",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_TenantId",
                table: "file_folder_placements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_TenantId_WorkspaceId_FolderId",
                table: "file_folder_placements",
                columns: new[] { "TenantId", "WorkspaceId", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_file_folder_placements_WorkspaceId",
                table: "file_folder_placements",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_CreatedAt",
                table: "file_folders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_DeletedAt",
                table: "file_folders",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_DeletedByUserId",
                table: "file_folders",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_ParentFolderId",
                table: "file_folders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_TenantId",
                table: "file_folders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_TenantId_WorkspaceId_DeletedAt",
                table: "file_folders",
                columns: new[] { "TenantId", "WorkspaceId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_TenantId_WorkspaceId_ParentFolderId_SortOrder",
                table: "file_folders",
                columns: new[] { "TenantId", "WorkspaceId", "ParentFolderId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_file_folders_WorkspaceId",
                table: "file_folders",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_folder_placements");

            migrationBuilder.DropTable(
                name: "file_folders");
        }
    }
}
