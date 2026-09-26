using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Narrow schema sidecar for #388. These columns intentionally are not mapped
/// into AppDbContext, so the existing content aggregate and model snapshot stay
/// stable while distribution metadata has an independent durable lifecycle.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901103000_AddAnnouncementDistributionTargets")]
public sealed class AddAnnouncementDistributionTargets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DistributionTargetsJson",
            table: "announcement_drafts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DistributionTargetsJson",
            table: "announcements",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DistributionTargetsJson",
            table: "announcement_drafts");

        migrationBuilder.DropColumn(
            name: "DistributionTargetsJson",
            table: "announcements");
    }
}
