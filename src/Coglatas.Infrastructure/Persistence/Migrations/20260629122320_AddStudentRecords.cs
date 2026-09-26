using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicDisplayName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    HomeroomLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    HealthNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    GuardianContact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Grades = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttendanceStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    InternalSensitiveNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_student_records_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_student_records_CreatedAt",
                table: "student_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_student_records_TenantId",
                table: "student_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_student_records_WorkspaceId",
                table: "student_records",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_records");
        }
    }
}
