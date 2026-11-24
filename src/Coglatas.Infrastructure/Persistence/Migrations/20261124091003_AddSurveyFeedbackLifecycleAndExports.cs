using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyFeedbackLifecycleAndExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_presentations_TenantId_Id",
                table: "presentations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_form_submissions_TenantId_Id",
                table: "form_submissions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_form_submission_values_TenantId_Id",
                table: "form_submission_values",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_activity_events_TenantId_Id",
                table: "activity_events",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "answer_visibility_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer_visibility_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_answer_visibility_decisions_form_submission_values_TenantId~",
                        columns: x => new { x.TenantId, x.FormSubmissionValueId },
                        principalTable: "form_submission_values",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_invitation_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedForAutomaticApproval = table.Column<bool>(type: "boolean", nullable: false),
                    DecisionWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaximumActiveSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_invitation_policies", x => x.Id);
                    table.UniqueConstraint("AK_feedback_invitation_policies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("ck_feedback_invitation_policy_windows", "\"DecisionWindowSeconds\" > 0 AND \"MaximumActiveSeconds\" > 0");
                    table.ForeignKey(
                        name: "FK_feedback_invitation_policies_activity_events_TenantId_Event~",
                        columns: x => new { x.TenantId, x.EventId },
                        principalTable: "activity_events",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_read_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedRevision = table.Column<long>(type: "bigint", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_read_states", x => x.Id);
                    table.ForeignKey(
                        name: "FK_feedback_read_states_form_submissions_TenantId_FormSubmissi~",
                        columns: x => new { x.TenantId, x.FormSubmissionId },
                        principalTable: "form_submissions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_report_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    DisposedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisposedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispositionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_report_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_feedback_report_cases_form_submission_values_TenantId_FormS~",
                        columns: x => new { x.TenantId, x.FormSubmissionValueId },
                        principalTable: "form_submission_values",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_feedback_report_cases_presentations_TenantId_PresentationId",
                        columns: x => new { x.TenantId, x.PresentationId },
                        principalTable: "presentations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shared_teacher_feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_teacher_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shared_teacher_feedback_presentations_TenantId_Presentation~",
                        columns: x => new { x.TenantId, x.PresentationId },
                        principalTable: "presentations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_feedback_export_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ColumnsJson = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    SourceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceiptRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_feedback_export_requests", x => x.Id);
                    table.UniqueConstraint("AK_survey_feedback_export_requests_Id_TenantId_RequesterId", x => new { x.Id, x.TenantId, x.RequesterId });
                    table.CheckConstraint("ck_survey_feedback_export_requests_mode", "\"Mode\" IN ('aggregate', 'detail')");
                    table.ForeignKey(
                        name: "FK_survey_feedback_export_requests_presentations_PresentationI~",
                        columns: x => new { x.PresentationId, x.TenantId },
                        principalTable: "presentations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_feedback_export_requests_users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_private_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_private_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teacher_private_notes_presentations_TenantId_PresentationId",
                        columns: x => new { x.TenantId, x.PresentationId },
                        principalTable: "presentations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_viewer_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationPolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    ActiveDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    DecisionDeadlineUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecipientAcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_viewer_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_feedback_viewer_grants_feedback_invitation_policies_TenantI~",
                        columns: x => new { x.TenantId, x.InvitationPolicyId },
                        principalTable: "feedback_invitation_policies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_feedback_viewer_grants_presentations_TenantId_PresentationId",
                        columns: x => new { x.TenantId, x.PresentationId },
                        principalTable: "presentations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_feedback_viewer_grants_users_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_feedback_viewer_grants_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_feedback_export_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExportRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_feedback_export_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_survey_feedback_export_grants_survey_feedback_export_reques~",
                        columns: x => new { x.ExportRequestId, x.TenantId, x.RecipientUserId },
                        principalTable: "survey_feedback_export_requests",
                        principalColumns: new[] { "Id", "TenantId", "RequesterId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answer_visibility_decisions_CreatedAt",
                table: "answer_visibility_decisions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_answer_visibility_decisions_TenantId",
                table: "answer_visibility_decisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_answer_visibility_decisions_TenantId_FormSubmissionValueId",
                table: "answer_visibility_decisions",
                columns: new[] { "TenantId", "FormSubmissionValueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_invitation_policies_CreatedAt",
                table: "feedback_invitation_policies",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_invitation_policies_TenantId",
                table: "feedback_invitation_policies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_invitation_policies_TenantId_EventId",
                table: "feedback_invitation_policies",
                columns: new[] { "TenantId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_read_states_CreatedAt",
                table: "feedback_read_states",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_read_states_TenantId",
                table: "feedback_read_states",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_read_states_TenantId_FormSubmissionId",
                table: "feedback_read_states",
                columns: new[] { "TenantId", "FormSubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_read_states_TenantId_UserId_FormSubmissionId",
                table: "feedback_read_states",
                columns: new[] { "TenantId", "UserId", "FormSubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_report_cases_CreatedAt",
                table: "feedback_report_cases",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_report_cases_TenantId",
                table: "feedback_report_cases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_report_cases_TenantId_FormSubmissionValueId",
                table: "feedback_report_cases",
                columns: new[] { "TenantId", "FormSubmissionValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_report_cases_TenantId_PresentationId_Status",
                table: "feedback_report_cases",
                columns: new[] { "TenantId", "PresentationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_CreatedAt",
                table: "feedback_viewer_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_InviterUserId",
                table: "feedback_viewer_grants",
                column: "InviterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_RecipientUserId",
                table: "feedback_viewer_grants",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_TenantId",
                table: "feedback_viewer_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_TenantId_InvitationPolicyId",
                table: "feedback_viewer_grants",
                columns: new[] { "TenantId", "InvitationPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_TenantId_PresentationId_RecipientUse~",
                table: "feedback_viewer_grants",
                columns: new[] { "TenantId", "PresentationId", "RecipientUserId" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viewer_grants_TenantId_RecipientUserId_Presentatio~",
                table: "feedback_viewer_grants",
                columns: new[] { "TenantId", "RecipientUserId", "PresentationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_shared_teacher_feedback_CreatedAt",
                table: "shared_teacher_feedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_shared_teacher_feedback_TenantId",
                table: "shared_teacher_feedback",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_teacher_feedback_TenantId_PresentationId",
                table: "shared_teacher_feedback",
                columns: new[] { "TenantId", "PresentationId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_grants_CreatedAt",
                table: "survey_feedback_export_grants",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_grants_ExportRequestId_TenantId_Reci~",
                table: "survey_feedback_export_grants",
                columns: new[] { "ExportRequestId", "TenantId", "RecipientUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_grants_TenantId",
                table: "survey_feedback_export_grants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_grants_TenantId_RecipientUserId_Expi~",
                table: "survey_feedback_export_grants",
                columns: new[] { "TenantId", "RecipientUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_requests_CreatedAt",
                table: "survey_feedback_export_requests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_requests_PresentationId_TenantId",
                table: "survey_feedback_export_requests",
                columns: new[] { "PresentationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_requests_RequesterId",
                table: "survey_feedback_export_requests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_requests_TenantId",
                table: "survey_feedback_export_requests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_feedback_export_requests_TenantId_RequesterId_Expire~",
                table: "survey_feedback_export_requests",
                columns: new[] { "TenantId", "RequesterId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_private_notes_CreatedAt",
                table: "teacher_private_notes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_private_notes_TenantId",
                table: "teacher_private_notes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_private_notes_TenantId_PresentationId_AuthorUserId",
                table: "teacher_private_notes",
                columns: new[] { "TenantId", "PresentationId", "AuthorUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_visibility_decisions");

            migrationBuilder.DropTable(
                name: "feedback_read_states");

            migrationBuilder.DropTable(
                name: "feedback_report_cases");

            migrationBuilder.DropTable(
                name: "feedback_viewer_grants");

            migrationBuilder.DropTable(
                name: "shared_teacher_feedback");

            migrationBuilder.DropTable(
                name: "survey_feedback_export_grants");

            migrationBuilder.DropTable(
                name: "teacher_private_notes");

            migrationBuilder.DropTable(
                name: "feedback_invitation_policies");

            migrationBuilder.DropTable(
                name: "survey_feedback_export_requests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_presentations_TenantId_Id",
                table: "presentations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_form_submissions_TenantId_Id",
                table: "form_submissions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_form_submission_values_TenantId_Id",
                table: "form_submission_values");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_activity_events_TenantId_Id",
                table: "activity_events");
        }
    }
}
