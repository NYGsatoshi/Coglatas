using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260903123000_AddAnnouncementEngagementSidecar")]
public sealed class AddAnnouncementEngagementSidecar : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "announcement_engagement_events",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                AnnouncementId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_announcement_engagement_events",
                    item => new { item.TenantId, item.AnnouncementId, item.RecipientToken, item.Action });
                table.ForeignKey(
                    name: "FK_announcement_engagement_events_announcements_AnnouncementId",
                    column: item => item.AnnouncementId,
                    principalTable: "announcements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_announcement_engagement_events_TenantId_AnnouncementId_Action",
            table: "announcement_engagement_events",
            columns: new[] { "TenantId", "AnnouncementId", "Action" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "announcement_engagement_events");
    }
}
