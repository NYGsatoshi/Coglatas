using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackCompatiblePublishedSurveyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompatibilityChangeReason",
                table: "target_question_set_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompatibilityChangeReason",
                table: "form_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompatibilityRootFieldId",
                table: "form_field_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompatiblePreviousFieldId",
                table: "form_field_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_form_field_definitions_Id_TenantId_FieldKey",
                table: "form_field_definitions",
                columns: new[] { "Id", "TenantId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_CompatibilityRootFieldId_TenantId_Fi~",
                table: "form_field_definitions",
                columns: new[] { "CompatibilityRootFieldId", "TenantId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_CompatiblePreviousFieldId_TenantId_F~",
                table: "form_field_definitions",
                columns: new[] { "CompatiblePreviousFieldId", "TenantId", "FieldKey" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_form_field_definitions_compatibility",
                table: "form_field_definitions",
                sql: "(\"CompatiblePreviousFieldId\" IS NULL AND \"CompatibilityRootFieldId\" IS NULL) OR\n(\"CompatiblePreviousFieldId\" IS NOT NULL AND \"CompatibilityRootFieldId\" IS NOT NULL AND\n    \"CompatiblePreviousFieldId\" <> \"Id\" AND \"CompatibilityRootFieldId\" <> \"Id\")");

            migrationBuilder.AddForeignKey(
                name: "FK_form_field_definitions_form_field_definitions_Compatibility~",
                table: "form_field_definitions",
                columns: new[] { "CompatibilityRootFieldId", "TenantId", "FieldKey" },
                principalTable: "form_field_definitions",
                principalColumns: new[] { "Id", "TenantId", "FieldKey" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_field_definitions_form_field_definitions_CompatiblePre~",
                table: "form_field_definitions",
                columns: new[] { "CompatiblePreviousFieldId", "TenantId", "FieldKey" },
                principalTable: "form_field_definitions",
                principalColumns: new[] { "Id", "TenantId", "FieldKey" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_form_field_definitions_form_field_definitions_Compatibility~",
                table: "form_field_definitions");

            migrationBuilder.DropForeignKey(
                name: "FK_form_field_definitions_form_field_definitions_CompatiblePre~",
                table: "form_field_definitions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_form_field_definitions_Id_TenantId_FieldKey",
                table: "form_field_definitions");

            migrationBuilder.DropIndex(
                name: "IX_form_field_definitions_CompatibilityRootFieldId_TenantId_Fi~",
                table: "form_field_definitions");

            migrationBuilder.DropIndex(
                name: "IX_form_field_definitions_CompatiblePreviousFieldId_TenantId_F~",
                table: "form_field_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_form_field_definitions_compatibility",
                table: "form_field_definitions");

            migrationBuilder.DropColumn(
                name: "CompatibilityChangeReason",
                table: "target_question_set_versions");

            migrationBuilder.DropColumn(
                name: "CompatibilityChangeReason",
                table: "form_versions");

            migrationBuilder.DropColumn(
                name: "CompatibilityRootFieldId",
                table: "form_field_definitions");

            migrationBuilder.DropColumn(
                name: "CompatiblePreviousFieldId",
                table: "form_field_definitions");
        }
    }
}
