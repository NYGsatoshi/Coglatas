using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wpc02AProjectVisibilityAndActivationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAtUtc",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
      name: "ActivationState",
      table: "projects",
      type: "character varying(40)",
      maxLength: 40,
      nullable: true);

  migrationBuilder.Sql(
      "UPDATE \"projects\" SET \"ActivationState\" = 'LegacyUnknown' WHERE \"ActivationState\" IS NULL;");

  migrationBuilder.AlterColumn<string>(
      name: "ActivationState",
      table: "projects",
      type: "character varying(40)",
      maxLength: 40,
      nullable: false,
      oldClrType: typeof(string),
      oldType: "character varying(40)",
      oldMaxLength: 40,
      oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActivationVersion",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedFromStatus",
                table: "projects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedFromStatus",
                table: "projects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "projects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_ActivationState",
                table: "projects",
                columns: new[] { "TenantId", "ActivationState" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_Visibility",
                table: "projects",
                columns: new[] { "TenantId", "Visibility" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects",
                sql: "(\"ActivationState\" = 'Activated' AND \"ActivatedAtUtc\" IS NOT NULL AND \"ActivationVersion\" IS NOT NULL AND \"ActivationVersion\" > 0) OR (\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated') AND \"ActivatedAtUtc\" IS NULL AND \"ActivationVersion\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects",
                sql: "\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated', 'Activated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects",
                sql: "\"Visibility\" IS NULL OR \"Visibility\" IN ('WorkspaceVisible', 'MembersOnly', 'Restricted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_projects_TenantId_ActivationState",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_projects_TenantId_Visibility",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "ActivationState",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "ActivationVersion",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "ArchivedFromStatus",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "SuspendedFromStatus",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "projects");
        }
    }
}
