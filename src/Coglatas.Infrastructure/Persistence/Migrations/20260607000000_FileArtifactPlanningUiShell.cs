using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileArtifactPlanningUiShell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactType",
                table: "artifacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "artifacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "artifact_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "feature_modules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "feature_modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultRoute",
                table: "feature_modules",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "feature_modules",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredRole",
                table: "feature_modules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "panel_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "panel_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPosition",
                table: "panel_definitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "Center");

            migrationBuilder.AddColumn<int>(
                name: "DefaultWidth",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.AddColumn<int>(
                name: "DefaultHeight",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 320);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosable",
                table: "panel_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDockable",
                table: "panel_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "panel_definitions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "panel_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "user_layouts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<Guid>(
                name: "ScopeId",
                table: "user_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "user_layouts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Global");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "command_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "command_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "command_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Navigate");

            migrationBuilder.AddColumn<string>(
                name: "ContextType",
                table: "command_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Global");

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "command_definitions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "command_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContextType",
                table: "radial_menu_profiles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Global");

            migrationBuilder.AddColumn<string>(
                name: "ProfileKey",
                table: "radial_menu_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE radial_menu_profiles SET \"ProfileKey\" = 'legacy-' || \"Id\"::text WHERE \"ProfileKey\" = '';");

            migrationBuilder.AddColumn<string>(
                name: "CommandKey",
                table: "radial_menu_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "radial_menu_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Center");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_Status",
                table: "artifacts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_versions_DeletedAt",
                table: "artifact_versions",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feature_modules_CreatedAt",
                table: "feature_modules",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_panel_definitions_CreatedAt",
                table: "panel_definitions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_panel_definitions_FeatureModuleId_SortOrder",
                table: "panel_definitions",
                columns: new[] { "FeatureModuleId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_user_layouts_UserId_ScopeType_ScopeId",
                table: "user_layouts",
                columns: new[] { "UserId", "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_command_definitions_CreatedAt",
                table: "command_definitions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_command_definitions_ContextType_SortOrder",
                table: "command_definitions",
                columns: new[] { "ContextType", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_radial_menu_profiles_ProfileKey",
                table: "radial_menu_profiles",
                column: "ProfileKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_radial_menu_profiles_ProfileKey", table: "radial_menu_profiles");
            migrationBuilder.DropIndex(name: "IX_command_definitions_ContextType_SortOrder", table: "command_definitions");
            migrationBuilder.DropIndex(name: "IX_command_definitions_CreatedAt", table: "command_definitions");
            migrationBuilder.DropIndex(name: "IX_user_layouts_UserId_ScopeType_ScopeId", table: "user_layouts");
            migrationBuilder.DropIndex(name: "IX_panel_definitions_FeatureModuleId_SortOrder", table: "panel_definitions");
            migrationBuilder.DropIndex(name: "IX_panel_definitions_CreatedAt", table: "panel_definitions");
            migrationBuilder.DropIndex(name: "IX_feature_modules_CreatedAt", table: "feature_modules");
            migrationBuilder.DropIndex(name: "IX_artifact_versions_DeletedAt", table: "artifact_versions");
            migrationBuilder.DropIndex(name: "IX_artifacts_Status", table: "artifacts");

            migrationBuilder.DropColumn(name: "Direction", table: "radial_menu_items");
            migrationBuilder.DropColumn(name: "CommandKey", table: "radial_menu_items");
            migrationBuilder.DropColumn(name: "ProfileKey", table: "radial_menu_profiles");
            migrationBuilder.DropColumn(name: "ContextType", table: "radial_menu_profiles");
            migrationBuilder.DropColumn(name: "SortOrder", table: "command_definitions");
            migrationBuilder.DropColumn(name: "RequiredPermission", table: "command_definitions");
            migrationBuilder.DropColumn(name: "ContextType", table: "command_definitions");
            migrationBuilder.DropColumn(name: "ActionType", table: "command_definitions");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "command_definitions");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "command_definitions");
            migrationBuilder.DropColumn(name: "ScopeType", table: "user_layouts");
            migrationBuilder.DropColumn(name: "ScopeId", table: "user_layouts");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "user_layouts");
            migrationBuilder.DropColumn(name: "SortOrder", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "RequiredPermission", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "IsDockable", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "IsClosable", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "DefaultHeight", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "DefaultWidth", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "DefaultPosition", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "panel_definitions");
            migrationBuilder.DropColumn(name: "RequiredRole", table: "feature_modules");
            migrationBuilder.DropColumn(name: "Icon", table: "feature_modules");
            migrationBuilder.DropColumn(name: "DefaultRoute", table: "feature_modules");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "feature_modules");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "feature_modules");
            migrationBuilder.DropColumn(name: "DeletedAt", table: "artifact_versions");
            migrationBuilder.DropColumn(name: "Status", table: "artifacts");
            migrationBuilder.DropColumn(name: "ArtifactType", table: "artifacts");
        }
    }
}
