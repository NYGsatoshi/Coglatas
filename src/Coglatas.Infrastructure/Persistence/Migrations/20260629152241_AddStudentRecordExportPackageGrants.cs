using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentRecordExportPackageGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_package_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedFields = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AuthorizedFields = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PolicyStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReauthorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BuiltAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_package_grants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_CreatedAt",
                table: "export_package_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_ExpiresAt",
                table: "export_package_grants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_RequestedByUserId",
                table: "export_package_grants",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_StudentRecordId",
                table: "export_package_grants",
                column: "StudentRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_TenantId",
                table: "export_package_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_export_package_grants_WorkspaceId",
                table: "export_package_grants",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_package_grants");
        }
    }
}
