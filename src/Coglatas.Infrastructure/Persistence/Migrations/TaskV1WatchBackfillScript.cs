namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>Single source for the Watch backfill executed by the migration and its PostgreSQL acceptance tests.</summary>
public static class TaskV1WatchBackfillScript
{
    public const string Sql = """
WITH automatic_sources AS (
    SELECT t."TenantId", t."Id" AS "TaskItemId", t."CreatedByUserId" AS "UserId", 1 AS "Source"
    FROM task_items t WHERE t."DeletedAt" IS NULL
    UNION ALL
    SELECT t."TenantId", t."Id", t."PrimaryAssigneeUserId", 2
    FROM task_items t WHERE t."DeletedAt" IS NULL AND t."PrimaryAssigneeUserId" IS NOT NULL
    UNION ALL
    SELECT t."TenantId", t."Id", t."ReviewerUserId", 8
    FROM task_items t WHERE t."DeletedAt" IS NULL AND t."ReviewerUserId" IS NOT NULL
    UNION ALL
    SELECT c."TenantId", c."TaskItemId", c."UserId", 4
    FROM task_item_collaborators c
    INNER JOIN task_items t ON t."Id" = c."TaskItemId" AND t."TenantId" = c."TenantId"
    WHERE t."DeletedAt" IS NULL
), combined_sources AS (
    SELECT "TenantId", "TaskItemId", "UserId", bit_or("Source")::integer AS "AutomaticSources"
    FROM automatic_sources
    WHERE "UserId" IS NOT NULL
    GROUP BY "TenantId", "TaskItemId", "UserId"
)
INSERT INTO work_item_watch_states ("Id", "TenantId", "TaskItemId", "UserId", "AutomaticSources", "IsExplicitOptOut", "IsWatching", "UpdatedAt", "VersionNo")
SELECT gen_random_uuid(), "TenantId", "TaskItemId", "UserId", "AutomaticSources", false, true, CURRENT_TIMESTAMP, 1
FROM combined_sources
ON CONFLICT ("TenantId", "TaskItemId", "UserId") DO UPDATE
SET "AutomaticSources" = EXCLUDED."AutomaticSources",
    "IsWatching" = CASE
        WHEN work_item_watch_states."IsExplicitOptOut" THEN false
        ELSE true
    END,
    "UpdatedAt" = CASE
        WHEN work_item_watch_states."AutomaticSources" IS DISTINCT FROM EXCLUDED."AutomaticSources"
          OR work_item_watch_states."IsWatching" IS DISTINCT FROM CASE WHEN work_item_watch_states."IsExplicitOptOut" THEN false ELSE true END
        THEN CURRENT_TIMESTAMP
        ELSE work_item_watch_states."UpdatedAt"
    END,
    "VersionNo" = CASE
        WHEN work_item_watch_states."AutomaticSources" IS DISTINCT FROM EXCLUDED."AutomaticSources"
          OR work_item_watch_states."IsWatching" IS DISTINCT FROM CASE WHEN work_item_watch_states."IsExplicitOptOut" THEN false ELSE true END
        THEN work_item_watch_states."VersionNo" + 1
        ELSE work_item_watch_states."VersionNo"
    END
WHERE work_item_watch_states."AutomaticSources" IS DISTINCT FROM EXCLUDED."AutomaticSources"
   OR work_item_watch_states."IsWatching" IS DISTINCT FROM CASE WHEN work_item_watch_states."IsExplicitOptOut" THEN false ELSE true END;

WITH automatic_sources AS (
    SELECT t."TenantId", t."Id" AS "TaskItemId", t."CreatedByUserId" AS "UserId", 1 AS "Source"
    FROM task_items t WHERE t."DeletedAt" IS NULL
    UNION ALL
    SELECT t."TenantId", t."Id", t."PrimaryAssigneeUserId", 2
    FROM task_items t WHERE t."DeletedAt" IS NULL AND t."PrimaryAssigneeUserId" IS NOT NULL
    UNION ALL
    SELECT t."TenantId", t."Id", t."ReviewerUserId", 8
    FROM task_items t WHERE t."DeletedAt" IS NULL AND t."ReviewerUserId" IS NOT NULL
    UNION ALL
    SELECT c."TenantId", c."TaskItemId", c."UserId", 4
    FROM task_item_collaborators c
    INNER JOIN task_items t ON t."Id" = c."TaskItemId" AND t."TenantId" = c."TenantId"
    WHERE t."DeletedAt" IS NULL
), combined_sources AS (
    SELECT "TenantId", "TaskItemId", "UserId", bit_or("Source")::integer AS "AutomaticSources"
    FROM automatic_sources
    WHERE "UserId" IS NOT NULL
    GROUP BY "TenantId", "TaskItemId", "UserId"
)
UPDATE work_item_watch_states AS state
SET "AutomaticSources" = 0,
    "IsWatching" = CASE WHEN state."IsExplicitOptOut" THEN false ELSE state."IsWatching" END,
    "UpdatedAt" = CURRENT_TIMESTAMP,
    "VersionNo" = state."VersionNo" + 1
WHERE EXISTS (SELECT 1 FROM task_items AS task WHERE task."Id" = state."TaskItemId" AND task."TenantId" = state."TenantId" AND task."DeletedAt" IS NULL)
  AND NOT EXISTS (SELECT 1 FROM combined_sources AS source WHERE source."TenantId" = state."TenantId" AND source."TaskItemId" = state."TaskItemId" AND source."UserId" = state."UserId")
  AND (state."AutomaticSources" IS DISTINCT FROM 0
       OR (state."IsExplicitOptOut" AND state."IsWatching"));
""";
}
