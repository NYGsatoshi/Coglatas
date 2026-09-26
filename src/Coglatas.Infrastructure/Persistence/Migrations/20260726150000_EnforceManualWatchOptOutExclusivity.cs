using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes explicit manual watch intent and explicit opt-out mutually exclusive.
/// The repair is deliberately append-only so upgraded databases are brought to
/// the same canonical state as clean databases before the constraint is added.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260726150000_EnforceManualWatchOptOutExclusivity")]
public sealed class EnforceManualWatchOptOutExclusivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE work_item_watch_states AS state
SET "IsManualWatch" = false,
    "IsWatching" = CASE
        WHEN state."IsExplicitOptOut" THEN false
        WHEN state."IsManualWatch" AND NOT state."IsExplicitOptOut" THEN true
        WHEN state."AutomaticSources" <> 0 THEN true
        ELSE false
    END,
    "UpdatedAt" = CURRENT_TIMESTAMP,
    "VersionNo" = state."VersionNo" + 1
WHERE state."IsManualWatch" AND state."IsExplicitOptOut";

UPDATE work_item_watch_states AS state
SET "IsWatching" = CASE
        WHEN state."IsManualWatch" THEN true
        WHEN state."IsExplicitOptOut" THEN false
        WHEN state."AutomaticSources" <> 0 THEN true
        ELSE false
    END,
    "UpdatedAt" = CURRENT_TIMESTAMP,
    "VersionNo" = state."VersionNo" + 1
WHERE EXISTS (
        SELECT 1
        FROM task_items AS task
        WHERE task."Id" = state."TaskItemId"
          AND task."TenantId" = state."TenantId"
          AND task."DeletedAt" IS NULL)
  AND state."IsWatching" IS DISTINCT FROM CASE
        WHEN state."IsManualWatch" THEN true
        WHEN state."IsExplicitOptOut" THEN false
        WHEN state."AutomaticSources" <> 0 THEN true
        ELSE false
    END;
""");

        migrationBuilder.AddCheckConstraint(
            name: "CK_work_item_watch_states_manual_opt_out_exclusive",
            table: "work_item_watch_states",
            sql: "NOT (\"IsManualWatch\" AND \"IsExplicitOptOut\")");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_work_item_watch_states_manual_opt_out_exclusive",
            table: "work_item_watch_states");
    }
}
