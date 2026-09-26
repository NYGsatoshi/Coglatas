using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes task/file PUT idempotent under concurrent requests.  Historical duplicate
/// links are equivalent references to the same immutable file, so retain the first
/// active link and tombstone only later duplicate links before creating the index.
/// No FileObject or file bytes are deleted by this migration.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260725070000_EnforceUniqueActiveTaskFileAssociations")]
public sealed class EnforceUniqueActiveTaskFileAssociations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
WITH ranked AS (
    SELECT "Id",
           row_number() OVER (
               PARTITION BY "TenantId", "OwnerType", "OwnerId", "FileObjectId"
               ORDER BY "CreatedAt", "Id") AS row_number
    FROM attachments
    WHERE "OwnerType" = 'TaskItem'
      AND "OwnerId" IS NOT NULL
      AND "DeletedAt" IS NULL
)
UPDATE attachments AS duplicate
SET "DeletedAt" = CURRENT_TIMESTAMP,
    "DeletedByUserId" = duplicate."OwnerUserId",
    "DeleteReason" = 'Duplicate task file association consolidated by migration'
FROM ranked
WHERE duplicate."Id" = ranked."Id"
  AND ranked.row_number > 1;
""");

        migrationBuilder.CreateIndex(
            name: "IX_attachments_OwnerType_OwnerId_FileObjectId_active_task",
            table: "attachments",
            columns: new[] { "OwnerType", "OwnerId", "FileObjectId" },
            unique: true,
            filter: "\"OwnerType\" = 'TaskItem' AND \"DeletedAt\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_attachments_OwnerType_OwnerId_FileObjectId_active_task",
            table: "attachments");

        // Tombstoned duplicate links stay tombstoned: restoring them would recreate
        // ambiguous associations and cannot be safely inferred during rollback.
    }
}
