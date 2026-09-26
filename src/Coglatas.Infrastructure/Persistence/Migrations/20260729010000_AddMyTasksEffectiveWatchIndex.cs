using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Aligns the My Tasks Watching predicate with the canonical effective-watch
/// formula introduced after the original PR04 projection index migration.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260729010000_AddMyTasksEffectiveWatchIndex")]
public sealed class AddMyTasksEffectiveWatchIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX "IX_work_item_watch_states_effective_watch"
            ON work_item_watch_states ("TenantId", "UserId", "TaskItemId")
            WHERE "IsManualWatch"
               OR (NOT "IsExplicitOptOut" AND "AutomaticSources" <> 0);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_work_item_watch_states_effective_watch";
            """);
    }
}
