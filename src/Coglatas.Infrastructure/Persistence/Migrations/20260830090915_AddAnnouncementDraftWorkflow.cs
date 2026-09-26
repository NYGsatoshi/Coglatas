using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementDraftWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcement_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Priority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresReadConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledForUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduleTimeZoneId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ScheduleLocalDateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ScheduleUtcOffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    PublishedAnnouncementId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublicationClaimOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PublicationClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextPublicationAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublicationAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastPublicationFailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_announcement_drafts_announcements_PublishedAnnouncementId",
                        column: x => x.PublishedAnnouncementId,
                        principalTable: "announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_announcement_drafts_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_announcement_drafts_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_announcement_drafts_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_announcement_drafts_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_AuthorUserId",
                table: "announcement_drafts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_ChannelId",
                table: "announcement_drafts",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_CreatedAt",
                table: "announcement_drafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_GroupId",
                table: "announcement_drafts",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_PublishedAnnouncementId",
                table: "announcement_drafts",
                column: "PublishedAnnouncementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_TenantId",
                table: "announcement_drafts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_TenantId_AuthorUserId_Status_UpdatedAt",
                table: "announcement_drafts",
                columns: new[] { "TenantId", "AuthorUserId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_TenantId_Status_ScheduledForUtc_NextPub~",
                table: "announcement_drafts",
                columns: new[] { "TenantId", "Status", "ScheduledForUtc", "NextPublicationAttemptAtUtc", "PublicationClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_drafts_WorkspaceId",
                table: "announcement_drafts",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_drafts");
        }
    }
}
