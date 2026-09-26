using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260831114644_AddFileSelectionSnapshots")]
public sealed class AddFileSelectionSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_selection_snapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                NormalizedQuery = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                FileKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FromDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                OnlyMyUploads = table.Column<bool>(type: "boolean", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ConsumptionVersion = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_selection_snapshots", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "file_selection_snapshot_items",
            columns: table => new
            {
                SelectionSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_selection_snapshot_items", x => new { x.SelectionSnapshotId, x.FileObjectId });
                table.ForeignKey(
                    name: "FK_file_selection_snapshot_items_file_selection_snapshots_Sele~",
                    column: x => x.SelectionSnapshotId,
                    principalTable: "file_selection_snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshot_items_FileObjectId",
            table: "file_selection_snapshot_items",
            column: "FileObjectId");
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_ActorUserId",
            table: "file_selection_snapshots",
            column: "ActorUserId");
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_CreatedAt",
            table: "file_selection_snapshots",
            column: "CreatedAt");
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_ExpiresAt",
            table: "file_selection_snapshots",
            column: "ExpiresAt");
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_TenantId",
            table: "file_selection_snapshots",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_TenantId_ActorUserId_ExpiresAt",
            table: "file_selection_snapshots",
            columns: new[] { "TenantId", "ActorUserId", "ExpiresAt" });
        migrationBuilder.CreateIndex(
            name: "IX_file_selection_snapshots_WorkspaceId",
            table: "file_selection_snapshots",
            column: "WorkspaceId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "file_selection_snapshot_items");
        migrationBuilder.DropTable(name: "file_selection_snapshots");
    }
}
