using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>Establishes durable automatic Watch state for existing Tasks and database concurrency defaults.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260725050000_TaskV1WatchAndLabelConcurrency")]
public sealed class TaskV1WatchAndLabelConcurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE work_item_watch_states SET "VersionNo" = 1 WHERE "VersionNo" < 1;
UPDATE project_task_labels SET "VersionNo" = 1 WHERE "VersionNo" < 1;
ALTER TABLE work_item_watch_states ALTER COLUMN "VersionNo" SET DEFAULT 1;
ALTER TABLE project_task_labels ALTER COLUMN "VersionNo" SET DEFAULT 1;
""");
        migrationBuilder.Sql(TaskV1WatchBackfillScript.Sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE work_item_watch_states ALTER COLUMN "VersionNo" DROP DEFAULT;
ALTER TABLE project_task_labels ALTER COLUMN "VersionNo" DROP DEFAULT;
""");
    }
}
