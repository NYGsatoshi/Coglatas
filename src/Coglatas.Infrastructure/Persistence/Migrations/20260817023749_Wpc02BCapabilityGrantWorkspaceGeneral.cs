using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wpc02BCapabilityGrantWorkspaceGeneral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultKind",
                table: "conversations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "conversations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "capability_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capability_grants", x => x.Id);
                    table.CheckConstraint("CK_capability_grants_scope_shape", "(\"ScopeType\" = 'Tenant' AND \"ScopeId\" = \"TenantId\") OR (\"ScopeType\" = 'Workspace' AND \"ScopeId\" IS NOT NULL)");
                    table.CheckConstraint("CK_capability_grants_version_positive", "\"VersionNo\" > 0");
                    table.ForeignKey(
                        name: "FK_capability_grants_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capability_grants_users_SubjectUserId",
                        column: x => x.SubjectUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_ProjectId_DefaultKind",
                table: "conversations",
                columns: new[] { "TenantId", "ProjectId", "DefaultKind" },
                unique: true,
                filter: "\"DefaultKind\" = 'ProjectGeneral' AND \"ProjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId_WorkspaceId_DefaultKind",
                table: "conversations",
                columns: new[] { "TenantId", "WorkspaceId", "DefaultKind" },
                unique: true,
                filter: "\"DefaultKind\" = 'WorkspaceGeneral'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_conversations_project_general_shape",
                table: "conversations",
                sql: "\"DefaultKind\" <> 'ProjectGeneral' OR (\"Type\" = 'ProjectChannel' AND \"ProjectId\" IS NOT NULL AND \"Visibility\" = 'PublicWithinScope')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_conversations_workspace_general_shape",
                table: "conversations",
                sql: "\"DefaultKind\" <> 'WorkspaceGeneral' OR (\"Type\" = 'WorkspaceChannel' AND \"ProjectId\" IS NULL AND \"Visibility\" = 'PublicWithinScope')");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_CreatedAt",
                table: "capability_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_ExpiresAt",
                table: "capability_grants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_GrantedByUserId",
                table: "capability_grants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_RevokedAt",
                table: "capability_grants",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_SubjectUserId",
                table: "capability_grants",
                column: "SubjectUserId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_TenantId",
                table: "capability_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_grants_TenantId_SubjectUserId_CapabilityKey_Scop~",
                table: "capability_grants",
                columns: new[] { "TenantId", "SubjectUserId", "CapabilityKey", "ScopeType", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capability_grants");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_ProjectId_DefaultKind",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_TenantId_WorkspaceId_DefaultKind",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_conversations_project_general_shape",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_conversations_workspace_general_shape",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "DefaultKind",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "conversations");
        }
    }
}
