using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileAccessGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SharingPolicy",
                table: "file_objects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                // Preserve existing File inventory behavior on upgrade. New
                // direct Workspace uploads use the model default (Private)
                // after this backfill has completed.
                defaultValue: "Workspace");

            migrationBuilder.AlterColumn<string>(
                name: "SharingPolicy",
                table: "file_objects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Private",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "Workspace");

            migrationBuilder.AddColumn<long>(
                name: "SharingVersion",
                table: "file_objects",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "file_access_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_access_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_access_grants_file_objects_FileObjectId",
                        column: x => x.FileObjectId,
                        principalTable: "file_objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_access_grants_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_access_grants_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_access_grants_users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_access_grants_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_objects_TenantId_WorkspaceId_SharingPolicy",
                table: "file_objects",
                columns: new[] { "TenantId", "WorkspaceId", "SharingPolicy" });

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_CreatedAt",
                table: "file_access_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_FileObjectId",
                table: "file_access_grants",
                column: "FileObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_GrantedByUserId",
                table: "file_access_grants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_RecipientUserId",
                table: "file_access_grants",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_RevokedByUserId",
                table: "file_access_grants",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_TenantId",
                table: "file_access_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_TenantId_FileObjectId_RecipientUserId",
                table: "file_access_grants",
                columns: new[] { "TenantId", "FileObjectId", "RecipientUserId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_TenantId_RecipientUserId_RevokedAt",
                table: "file_access_grants",
                columns: new[] { "TenantId", "RecipientUserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_TenantId_WorkspaceId_FileObjectId_Revoke~",
                table: "file_access_grants",
                columns: new[] { "TenantId", "WorkspaceId", "FileObjectId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_file_access_grants_WorkspaceId",
                table: "file_access_grants",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_access_grants");

            migrationBuilder.DropIndex(
                name: "IX_file_objects_TenantId_WorkspaceId_SharingPolicy",
                table: "file_objects");

            migrationBuilder.DropColumn(
                name: "SharingPolicy",
                table: "file_objects");

            migrationBuilder.DropColumn(
                name: "SharingVersion",
                table: "file_objects");
        }
    }
}
