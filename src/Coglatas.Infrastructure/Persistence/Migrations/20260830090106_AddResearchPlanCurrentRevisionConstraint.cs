using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchPlanCurrentRevisionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_research_plans_CurrentRevisionId_Id",
                table: "research_plans",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_research_plans_research_plan_revisions_CurrentRevisionId_Id",
                table: "research_plans",
                columns: new[] { "CurrentRevisionId", "Id" },
                principalTable: "research_plan_revisions",
                principalColumns: new[] { "Id", "ResearchPlanId" });

            // A first revision legitimately references the newly-created
            // plan while the plan points back to that revision. Deferring
            // this optional NO ACTION FK preserves that single transaction
            // without relaxing the same-plan ownership boundary at commit.
            migrationBuilder.Sql("""
ALTER TABLE research_plans
    ALTER CONSTRAINT "FK_research_plans_research_plan_revisions_CurrentRevisionId_Id"
    DEFERRABLE INITIALLY DEFERRED;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_research_plans_research_plan_revisions_CurrentRevisionId_Id",
                table: "research_plans");

            migrationBuilder.DropIndex(
                name: "IX_research_plans_CurrentRevisionId_Id",
                table: "research_plans");
        }
    }
}
