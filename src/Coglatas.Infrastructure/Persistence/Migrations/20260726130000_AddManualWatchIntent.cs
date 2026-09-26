using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists explicit watch intent independently of relationship-derived sources.
/// Legacy rows with automatic sources are ambiguous, so they conservatively retain
/// automatic watching while defaulting manual intent to false.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260726130000_AddManualWatchIntent")]
public sealed class AddManualWatchIntent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsManualWatch",
            table: "work_item_watch_states",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
UPDATE work_item_watch_states
SET "IsManualWatch" = CASE
        WHEN "IsExplicitOptOut" THEN false
        WHEN "AutomaticSources" = 0 AND "IsWatching" THEN true
        ELSE false
    END,
    "IsWatching" = CASE
        WHEN "IsExplicitOptOut" THEN false
        WHEN "AutomaticSources" <> 0 THEN true
        ELSE "IsWatching"
    END;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsManualWatch", table: "work_item_watch_states");
    }
}
