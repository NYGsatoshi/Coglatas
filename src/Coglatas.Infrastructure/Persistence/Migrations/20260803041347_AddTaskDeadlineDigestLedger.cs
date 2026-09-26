using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDeadlineDigestLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_deadline_digest_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AutomaticAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptSequence = table.Column<int>(type: "integer", nullable: false),
                    ScheduledForUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_deadline_digest_jobs", x => x.Id);
                    table.CheckConstraint("CK_task_deadline_digest_jobs_attempt_counts", "\"AttemptCount\" >= 0 AND \"AutomaticAttemptCount\" >= 0 AND \"AutomaticAttemptCount\" <= 3 AND \"AutomaticAttemptCount\" <= \"AttemptCount\" AND \"AttemptSequence\" >= 0");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_claim_expiry", "\"ClaimExpiresAt\" IS NULL OR \"ClaimExpiresAt\" > \"ClaimedAt\"");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_claim_fields", "(\"Status\" = 'Claimed' AND \"ClaimOwner\" IS NOT NULL AND \"ClaimToken\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"ClaimExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Claimed' AND \"ClaimOwner\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimedAt\" IS NULL AND \"ClaimExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_completion", "((\"Status\" IN ('Succeeded', 'Failed')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_failed_after_three", "\"Status\" <> 'Failed' OR \"AutomaticAttemptCount\" = 3");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_next_attempt", "\"Status\" <> 'Pending' OR \"NextAttemptAt\" IS NOT NULL");
                    table.CheckConstraint("CK_task_deadline_digest_jobs_policy_version", "\"PolicyVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_jobs_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_jobs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_jobs_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_deadline_digest_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RestartedFromAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_deadline_digest_attempts", x => x.Id);
                    table.CheckConstraint("CK_task_deadline_digest_attempts_claim_expiry", "\"ClaimExpiresAt\" IS NULL OR \"ClaimExpiresAt\" > \"ClaimedAt\"");
                    table.CheckConstraint("CK_task_deadline_digest_attempts_claim_fields", "(\"Status\" = 'Claimed' AND \"ClaimOwner\" IS NOT NULL AND \"ClaimToken\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"ClaimExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Claimed' AND \"ClaimOwner\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimedAt\" IS NULL AND \"ClaimExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_task_deadline_digest_attempts_completion", "((\"Status\" IN ('Succeeded', 'Failed', 'Expired', 'Deferred')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");
                    table.CheckConstraint("CK_task_deadline_digest_attempts_number", "\"AttemptNumber\" > 0");
                    table.CheckConstraint("CK_task_deadline_digest_attempts_restart", "(\"Trigger\" = 'Automatic' AND \"RestartedFromAttemptId\" IS NULL AND \"RequestedByUserId\" IS NULL) OR (\"Trigger\" = 'OperatorRestart' AND \"RestartedFromAttemptId\" IS NOT NULL AND \"RequestedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_attempts_task_deadline_digest_attempts~",
                        column: x => x.RestartedFromAttemptId,
                        principalTable: "task_deadline_digest_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_attempts_task_deadline_digest_jobs_Job~",
                        column: x => x.JobId,
                        principalTable: "task_deadline_digest_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_deadline_digest_attempts_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_CreatedAt",
                table: "task_deadline_digest_attempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_job_number",
                table: "task_deadline_digest_attempts",
                columns: new[] { "JobId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_one_active",
                table: "task_deadline_digest_attempts",
                column: "JobId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_RequestedByUserId",
                table: "task_deadline_digest_attempts",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_RestartedFromAttemptId",
                table: "task_deadline_digest_attempts",
                column: "RestartedFromAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_TenantId",
                table: "task_deadline_digest_attempts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_claim_expiry",
                table: "task_deadline_digest_jobs",
                columns: new[] { "TenantId", "ClaimExpiresAt", "Id" },
                filter: "\"Status\" = 'Claimed'");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_CreatedAt",
                table: "task_deadline_digest_jobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_due",
                table: "task_deadline_digest_jobs",
                columns: new[] { "TenantId", "NextAttemptAt", "CreatedAt", "Id" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_identity",
                table: "task_deadline_digest_jobs",
                columns: new[] { "TenantId", "WorkspaceId", "UserId", "LocalDate", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_NotificationId",
                table: "task_deadline_digest_jobs",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_TenantId",
                table: "task_deadline_digest_jobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_UserId",
                table: "task_deadline_digest_jobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_jobs_WorkspaceId",
                table: "task_deadline_digest_jobs",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_deadline_digest_attempts");

            migrationBuilder.DropTable(
                name: "task_deadline_digest_jobs");
        }
    }
}
