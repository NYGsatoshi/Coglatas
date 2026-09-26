using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Applies the current watch formula after the manual-intent column exists.
/// This is intentionally append-only: the earlier source backfill also runs on
/// databases where that column did not yet exist.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260726140000_NormalizeWatchStateAfterManualIntent")]
public sealed class NormalizeWatchStateAfterManualIntent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE work_item_watch_states AS state
SET "IsWatching" = CASE
        WHEN state."IsExplicitOptOut" THEN false
        WHEN state."IsManualWatch" THEN true
        WHEN state."AutomaticSources" <> 0 THEN true
        ELSE false
    END,
    "UpdatedAt" = CURRENT_TIMESTAMP,
    "VersionNo" = state."VersionNo" + 1
WHERE EXISTS (
        SELECT 1 FROM task_items AS task
        WHERE task."Id" = state."TaskItemId"
          AND task."TenantId" = state."TenantId"
          AND task."DeletedAt" IS NULL)
  AND state."IsWatching" IS DISTINCT FROM CASE
        WHEN state."IsExplicitOptOut" THEN false
        WHEN state."IsManualWatch" THEN true
        WHEN state."AutomaticSources" <> 0 THEN true
        ELSE false
    END;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
