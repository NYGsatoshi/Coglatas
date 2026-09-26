using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

/// <summary>Copies only legacy generic TaskItem comments.  The anti-join makes the data step safe if an operator retries it.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260722230000_MigrateLegacyTaskComments")]
public sealed class MigrateLegacyTaskComments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
INSERT INTO task_comments ("Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "AuthorUserId", "BodyPlainText", "IsImportant", "VersionNo", "CreatedAt", "UpdatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason")
SELECT c."Id", c."TenantId", t."WorkspaceId", t."ProjectId", t."Id", c."AuthorUserId", c."Body", false, 1, c."CreatedAt", c."UpdatedAt", c."DeletedAt", c."DeletedByUserId", c."DeleteReason"
FROM comments c
INNER JOIN task_items t ON t."Id" = c."TargetId" AND t."TenantId" = c."TenantId"
WHERE c."TargetType" = 'TaskItem'
  AND NOT EXISTS (SELECT 1 FROM task_comments tc WHERE tc."Id" = c."Id");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // IDs are shared with the legacy table; deleting copied rows would make a downgrade destructive.
    }
}
