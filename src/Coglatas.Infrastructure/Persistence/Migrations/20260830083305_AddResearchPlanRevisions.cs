using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchPlanRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_plans", x => x.Id);
                    table.CheckConstraint("CK_research_plans_version_positive", "\"VersionNo\" > 0");
                    table.ForeignKey(
                        name: "FK_research_plans_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plans_task_items_TaskItemId_ProjectId",
                        columns: x => new { x.TaskItemId, x.ProjectId },
                        principalTable: "task_items",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_plan_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNo = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_plan_revisions", x => x.Id);
                    table.UniqueConstraint("AK_research_plan_revisions_Id_ResearchPlanId", x => new { x.Id, x.ResearchPlanId });
                    table.CheckConstraint("CK_research_plan_revisions_revision_positive", "\"RevisionNo\" > 0");
                    table.ForeignKey(
                        name: "FK_research_plan_revisions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_revisions_research_plans_ResearchPlanId",
                        column: x => x.ResearchPlanId,
                        principalTable: "research_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_revisions_task_items_TaskItemId_ProjectId",
                        columns: x => new { x.TaskItemId, x.ProjectId },
                        principalTable: "task_items",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_revisions_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_plan_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchPlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Objective = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ScopeSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "Planned")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_plan_steps", x => x.Id);
                    table.CheckConstraint("CK_research_plan_steps_sort_order_positive", "\"SortOrder\" > 0");
                    table.ForeignKey(
                        name: "FK_research_plan_steps_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_steps_research_plan_revisions_ResearchPlanRev~",
                        columns: x => new { x.ResearchPlanRevisionId, x.ResearchPlanId },
                        principalTable: "research_plan_revisions",
                        principalColumns: new[] { "Id", "ResearchPlanId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_steps_research_plans_ResearchPlanId",
                        column: x => x.ResearchPlanId,
                        principalTable: "research_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plan_steps_task_items_TaskItemId_ProjectId",
                        columns: x => new { x.TaskItemId, x.ProjectId },
                        principalTable: "task_items",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_CreatedByUserId",
                table: "research_plan_revisions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_ProjectId",
                table: "research_plan_revisions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_ResearchPlanId_RevisionNo",
                table: "research_plan_revisions",
                columns: new[] { "ResearchPlanId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_TaskItemId_ProjectId",
                table: "research_plan_revisions",
                columns: new[] { "TaskItemId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_TenantId",
                table: "research_plan_revisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_revisions_TenantId_WorkspaceId_ProjectId_Task~",
                table: "research_plan_revisions",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "RevisionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_ProjectId",
                table: "research_plan_steps",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_ResearchPlanId",
                table: "research_plan_steps",
                column: "ResearchPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_ResearchPlanRevisionId_ResearchPlanId",
                table: "research_plan_steps",
                columns: new[] { "ResearchPlanRevisionId", "ResearchPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_ResearchPlanRevisionId_SortOrder",
                table: "research_plan_steps",
                columns: new[] { "ResearchPlanRevisionId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_TaskItemId_ProjectId",
                table: "research_plan_steps",
                columns: new[] { "TaskItemId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_TenantId",
                table: "research_plan_steps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plan_steps_TenantId_WorkspaceId_ProjectId_TaskItem~",
                table: "research_plan_steps",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "ResearchPlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_CurrentRevisionId",
                table: "research_plans",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_ProjectId",
                table: "research_plans",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_TaskItemId",
                table: "research_plans",
                column: "TaskItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_TaskItemId_ProjectId",
                table: "research_plans",
                columns: new[] { "TaskItemId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_TenantId",
                table: "research_plans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_research_plans_TenantId_WorkspaceId_ProjectId_TaskItemId",
                table: "research_plans",
                columns: new[] { "TenantId", "WorkspaceId", "ProjectId", "TaskItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_plan_steps");

            migrationBuilder.DropTable(
                name: "research_plan_revisions");

            migrationBuilder.DropTable(
                name: "research_plans");
        }
    }
}
