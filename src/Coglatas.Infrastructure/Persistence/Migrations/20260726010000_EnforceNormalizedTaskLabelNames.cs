using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes a Task Label's durable identity trim- and case-insensitive. Existing
/// duplicate definitions are consolidated deterministically: an active label
/// wins over an archived label, then SortKey and Id decide. Associations are
/// first moved to the survivor (without creating duplicate associations), then
/// only redundant label definitions and association rows are removed.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260726010000_EnforceNormalizedTaskLabelNames")]
public sealed class EnforceNormalizedTaskLabelNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_project_task_labels_TenantId_ProjectId_Name",
            table: "project_task_labels");

        migrationBuilder.AddColumn<string>(
            name: "NormalizedName",
            table: "project_task_labels",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            computedColumnSql: "lower(btrim(\"Name\"))",
            stored: true);

        migrationBuilder.Sql("""
WITH ranked AS (
    SELECT "Id", "TenantId", "ProjectId", "NormalizedName",
           first_value("Id") OVER (
               PARTITION BY "TenantId", "ProjectId", "NormalizedName"
               ORDER BY CASE WHEN "IsArchived" THEN 1 ELSE 0 END, "SortKey", "Id") AS "SurvivorId",
           row_number() OVER (
               PARTITION BY "TenantId", "ProjectId", "NormalizedName"
               ORDER BY CASE WHEN "IsArchived" THEN 1 ELSE 0 END, "SortKey", "Id") AS row_number
    FROM project_task_labels
), duplicates AS (
    SELECT "Id" AS "LoserId", "SurvivorId" FROM ranked WHERE row_number > 1
)
INSERT INTO work_item_labels ("Id", "TenantId", "TaskItemId", "LabelId", "AddedAt", "AddedByUserId")
SELECT gen_random_uuid(), link."TenantId", link."TaskItemId", duplicates."SurvivorId", link."AddedAt", link."AddedByUserId"
FROM work_item_labels AS link
INNER JOIN duplicates ON duplicates."LoserId" = link."LabelId"
ON CONFLICT ("TenantId", "TaskItemId", "LabelId") DO NOTHING;

WITH ranked AS (
    SELECT "Id",
           first_value("Id") OVER (
               PARTITION BY "TenantId", "ProjectId", "NormalizedName"
               ORDER BY CASE WHEN "IsArchived" THEN 1 ELSE 0 END, "SortKey", "Id") AS "SurvivorId",
           row_number() OVER (
               PARTITION BY "TenantId", "ProjectId", "NormalizedName"
               ORDER BY CASE WHEN "IsArchived" THEN 1 ELSE 0 END, "SortKey", "Id") AS row_number
    FROM project_task_labels
), duplicates AS (
    SELECT "Id" AS "LoserId", "SurvivorId" FROM ranked WHERE row_number > 1
)
DELETE FROM work_item_labels AS link
USING duplicates
WHERE link."LabelId" = duplicates."LoserId";

WITH ranked AS (
    SELECT "Id",
           row_number() OVER (
               PARTITION BY "TenantId", "ProjectId", "NormalizedName"
               ORDER BY CASE WHEN "IsArchived" THEN 1 ELSE 0 END, "SortKey", "Id") AS row_number
    FROM project_task_labels
)
DELETE FROM project_task_labels AS label
USING ranked
WHERE label."Id" = ranked."Id" AND ranked.row_number > 1;
""");

        migrationBuilder.CreateIndex(
            name: "IX_project_task_labels_TenantId_ProjectId_NormalizedName",
            table: "project_task_labels",
            columns: new[] { "TenantId", "ProjectId", "NormalizedName" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_project_task_labels_TenantId_ProjectId_NormalizedName",
            table: "project_task_labels");

        migrationBuilder.DropColumn(
            name: "NormalizedName",
            table: "project_task_labels");

        migrationBuilder.CreateIndex(
            name: "IX_project_task_labels_TenantId_ProjectId_Name",
            table: "project_task_labels",
            columns: new[] { "TenantId", "ProjectId", "Name" },
            unique: true);
    }
}
