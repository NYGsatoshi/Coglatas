using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260824220000_AddStructuredTaskBrief")]
public sealed class AddStructuredTaskBrief : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BriefConstraints",
            table: "task_items",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BriefDeliverable",
            table: "task_items",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BriefGoal",
            table: "task_items",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BriefConstraints", table: "task_items");
        migrationBuilder.DropColumn(name: "BriefDeliverable", table: "task_items");
        migrationBuilder.DropColumn(name: "BriefGoal", table: "task_items");
    }
}
