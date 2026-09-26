using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileDownloadGrantBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE file_objects
                ADD COLUMN IF NOT EXISTS "Classification" character varying(40);
                """);

            migrationBuilder.AddColumn<string>(
                name: "BuildAuthorizationState",
                table: "export_package_grants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DownloadAuthorizationState",
                table: "export_package_grants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExportType",
                table: "export_package_grants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IncludedClassifications",
                table: "export_package_grants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ReasonRequired",
                table: "export_package_grants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedScopeId",
                table: "export_package_grants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "RequestedScopeType",
                table: "export_package_grants",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "export_package_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "file_download_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetScopeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AllowedOperation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_download_grants", x => x.Id);
                });

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_file_objects_Classification"
                ON file_objects ("Classification");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_RequestedScopeType_RequestedScopeId",
                table: "export_package_grants",
                columns: new[] { "RequestedScopeType", "RequestedScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_RevokedAt",
                table: "export_package_grants",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_ActorUserId",
                table: "file_download_grants",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_AttachmentId",
                table: "file_download_grants",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_CreatedAt",
                table: "file_download_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_ExpiresAt",
                table: "file_download_grants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_FileObjectId",
                table: "file_download_grants",
                column: "FileObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_RevokedAt",
                table: "file_download_grants",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_TargetScopeType_TargetScopeId",
                table: "file_download_grants",
                columns: new[] { "TargetScopeType", "TargetScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_TenantId",
                table: "file_download_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_TokenHash",
                table: "file_download_grants",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_file_download_grants_WorkspaceId",
                table: "file_download_grants",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_download_grants");

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_file_objects_Classification";
                """);

            migrationBuilder.DropIndex(
                name: "IX_export_package_grants_RequestedScopeType_RequestedScopeId",
                table: "export_package_grants");

            migrationBuilder.DropIndex(
                name: "IX_export_package_grants_RevokedAt",
                table: "export_package_grants");

            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS file_objects
                DROP COLUMN IF EXISTS "Classification";
                """);

            migrationBuilder.DropColumn(
                name: "BuildAuthorizationState",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "DownloadAuthorizationState",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "ExportType",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "IncludedClassifications",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "ReasonRequired",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "RequestedScopeId",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "RequestedScopeType",
                table: "export_package_grants");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "export_package_grants");
        }
    }
}
