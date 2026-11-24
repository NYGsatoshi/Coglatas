using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyPresentationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_workspaces_Id_TenantId",
                table: "workspaces",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_tenant_users_TenantId_UserId",
                table: "tenant_users",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_groups_Id_TenantId",
                table: "groups",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_activity_events_Id_TenantId",
                table: "activity_events",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.CreateTable(
                name: "event_feedback_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceptionStopped = table.Column<bool>(type: "boolean", nullable: false),
                    ResultsStopped = table.Column<bool>(type: "boolean", nullable: false),
                    IsProgramPublished = table.Column<bool>(type: "boolean", nullable: false),
                    NameMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AccountBinding = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProgramFields = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_feedback_settings", x => x.Id);
                    table.CheckConstraint("CK_event_feedback_settings_window", "\"StartsAtUtc\" < \"EndsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_event_feedback_settings_activity_events_EventId_TenantId",
                        columns: x => new { x.EventId, x.TenantId },
                        principalTable: "activity_events",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleteReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_definitions", x => x.Id);
                    table.UniqueConstraint("AK_form_definitions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.CheckConstraint("CK_form_definitions_scope", "num_nonnulls(\"WorkspaceId\", \"GroupId\", \"ProjectId\") = 1");
                    table.ForeignKey(
                        name: "FK_form_definitions_groups_GroupId_TenantId",
                        columns: x => new { x.GroupId, x.TenantId },
                        principalTable: "groups",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_definitions_projects_ProjectId_TenantId",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_definitions_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_definitions_workspaces_WorkspaceId_TenantId",
                        columns: x => new { x.WorkspaceId, x.TenantId },
                        principalTable: "workspaces",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presentations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Venue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presentations", x => x.Id);
                    table.UniqueConstraint("AK_presentations_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.UniqueConstraint("AK_presentations_Id_TenantId_EventId", x => new { x.Id, x.TenantId, x.EventId });
                    table.CheckConstraint("CK_presentations_time", "\"StartsAtUtc\" < \"EndsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_presentations_activity_events_EventId_TenantId",
                        columns: x => new { x.EventId, x.TenantId },
                        principalTable: "activity_events",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_presentations_projects_ProjectId_TenantId",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_versions", x => x.Id);
                    table.UniqueConstraint("AK_form_versions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.UniqueConstraint("AK_form_versions_Id_TenantId_FormDefinitionId", x => new { x.Id, x.TenantId, x.FormDefinitionId });
                    table.CheckConstraint("CK_form_versions_number", "\"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_form_versions_form_definitions_FormDefinitionId_TenantId",
                        columns: x => new { x.FormDefinitionId, x.TenantId },
                        principalTable: "form_definitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_campaigns", x => x.Id);
                    table.UniqueConstraint("AK_survey_campaigns_Id_TenantId_EventId_FormDefinitionId", x => new { x.Id, x.TenantId, x.EventId, x.FormDefinitionId });
                    table.ForeignKey(
                        name: "FK_survey_campaigns_activity_events_EventId_TenantId",
                        columns: x => new { x.EventId, x.TenantId },
                        principalTable: "activity_events",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_campaigns_form_definitions_FormDefinitionId_TenantId",
                        columns: x => new { x.FormDefinitionId, x.TenantId },
                        principalTable: "form_definitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presentation_feedback_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultsStopped = table.Column<bool>(type: "boolean", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presentation_feedback_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_presentation_feedback_settings_presentations_PresentationId~",
                        columns: x => new { x.PresentationId, x.TenantId },
                        principalTable: "presentations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presentation_participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingRosterRef = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presentation_participants", x => x.Id);
                    table.CheckConstraint("CK_presentation_participants_subject", "num_nonnulls(\"UserId\", \"PendingRosterRef\") = 1");
                    table.ForeignKey(
                        name: "FK_presentation_participants_presentations_PresentationId_Tena~",
                        columns: x => new { x.PresentationId, x.TenantId },
                        principalTable: "presentations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_presentation_participants_tenant_users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "tenant_users",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_field_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetQuestionSetVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ConstraintsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_field_definitions", x => x.Id);
                    table.UniqueConstraint("AK_form_field_definitions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.CheckConstraint("CK_form_field_definitions_order", "\"SortOrder\" >= 0");
                    table.CheckConstraint("CK_form_field_definitions_version", "num_nonnulls(\"FormVersionId\", \"TargetQuestionSetVersionId\") = 1");
                    table.ForeignKey(
                        name: "FK_form_field_definitions_form_versions_FormVersionId_TenantId",
                        columns: x => new { x.FormVersionId, x.TenantId },
                        principalTable: "form_versions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_submission_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormFieldDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedSurveySchemaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submission_values", x => x.Id);
                    table.UniqueConstraint("AK_form_submission_values_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_form_submission_values_form_field_definitions_FormFieldDefi~",
                        columns: x => new { x.FormFieldDefinitionId, x.TenantId },
                        principalTable: "form_field_definitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedSurveySchemaId = table.Column<Guid>(type: "uuid", nullable: true),
                    RespondentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    FieldLockSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submissions", x => x.Id);
                    table.UniqueConstraint("AK_form_submissions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.CheckConstraint("CK_form_submissions_lock", "jsonb_typeof(\"FieldLockSnapshotJson\") = 'object'");
                    table.CheckConstraint("CK_form_submissions_shape", "(\"SurveyTargetId\" IS NULL AND \"PublishedSurveySchemaId\" IS NULL) OR\n(\"SurveyTargetId\" IS NOT NULL AND \"PublishedSurveySchemaId\" IS NOT NULL AND \"RespondentUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_form_submissions_form_versions_FormVersionId_TenantId_FormD~",
                        columns: x => new { x.FormVersionId, x.TenantId, x.FormDefinitionId },
                        principalTable: "form_versions",
                        principalColumns: new[] { "Id", "TenantId", "FormDefinitionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_submissions_users_RespondentUserId",
                        column: x => x.RespondentUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "survey_respondent_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnteredName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AccountUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountConsentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_respondent_identities", x => x.Id);
                    table.CheckConstraint("CK_survey_respondent_identities_consent", "num_nonnulls(\"AccountUserId\", \"AccountConsentAtUtc\") <> 1");
                    table.CheckConstraint("CK_survey_respondent_identities_present", "num_nonnulls(\"EnteredName\", \"AccountUserId\") > 0");
                    table.ForeignKey(
                        name: "FK_survey_respondent_identities_form_submissions_FormSubmissio~",
                        columns: x => new { x.FormSubmissionId, x.TenantId },
                        principalTable: "form_submissions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_respondent_identities_users_AccountUserId",
                        column: x => x.AccountUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_survey_schema_fields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedSurveySchemaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormFieldDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_survey_schema_fields", x => x.Id);
                    table.UniqueConstraint("AK_published_survey_schema_field_identity", x => new { x.PublishedSurveySchemaId, x.FormFieldDefinitionId, x.TenantId });
                    table.CheckConstraint("CK_published_survey_schema_fields_order", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_published_survey_schema_fields_form_field_definitions_FormF~",
                        columns: x => new { x.FormFieldDefinitionId, x.TenantId },
                        principalTable: "form_field_definitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_survey_schemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetQuestionSetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SchemaDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_survey_schemas", x => x.Id);
                    table.UniqueConstraint("AK_published_survey_schemas_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.UniqueConstraint("AK_published_survey_schemas_Id_TenantId_SurveyTargetId", x => new { x.Id, x.TenantId, x.SurveyTargetId });
                    table.UniqueConstraint("AK_published_survey_schemas_Id_TenantId_SurveyTargetId_FormDef~", x => new { x.Id, x.TenantId, x.SurveyTargetId, x.FormDefinitionId, x.FormVersionId });
                    table.CheckConstraint("CK_published_survey_schemas_digest", "\"SchemaDigest\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_published_survey_schemas_form_versions_FormVersionId_Tenant~",
                        columns: x => new { x.FormVersionId, x.TenantId, x.FormDefinitionId },
                        principalTable: "form_versions",
                        principalColumns: new[] { "Id", "TenantId", "FormDefinitionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedSchemaId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ResultsStopped = table.Column<bool>(type: "boolean", nullable: false),
                    LocalMarkerKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_targets", x => x.Id);
                    table.UniqueConstraint("AK_survey_targets_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.UniqueConstraint("AK_survey_targets_Id_TenantId_EventId_PresentationId", x => new { x.Id, x.TenantId, x.EventId, x.PresentationId });
                    table.UniqueConstraint("AK_survey_targets_Id_TenantId_FormDefinitionId", x => new { x.Id, x.TenantId, x.FormDefinitionId });
                    table.CheckConstraint("CK_survey_targets_window", "\"StartsAtUtc\" IS NULL OR \"EndsAtUtc\" IS NULL OR \"StartsAtUtc\" < \"EndsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_survey_targets_presentations_PresentationId_TenantId_EventId",
                        columns: x => new { x.PresentationId, x.TenantId, x.EventId },
                        principalTable: "presentations",
                        principalColumns: new[] { "Id", "TenantId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_targets_published_survey_schemas_PublishedSchemaId_T~",
                        columns: x => new { x.PublishedSchemaId, x.TenantId, x.Id },
                        principalTable: "published_survey_schemas",
                        principalColumns: new[] { "Id", "TenantId", "SurveyTargetId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_targets_survey_campaigns_CampaignId_TenantId_EventId~",
                        columns: x => new { x.CampaignId, x.TenantId, x.EventId, x.FormDefinitionId },
                        principalTable: "survey_campaigns",
                        principalColumns: new[] { "Id", "TenantId", "EventId", "FormDefinitionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_idempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntentDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceiptJson = table.Column<string>(type: "text", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_idempotency", x => x.Id);
                    table.CheckConstraint("CK_submission_idempotency_digests", " \"KeyDigest\" ~ '^[0-9a-f]{64}$' AND \"IntentDigest\" ~ '^[0-9a-f]{64}$' ");
                    table.CheckConstraint("CK_submission_idempotency_status", " \"StatusCode\" = 201 ");
                    table.ForeignKey(
                        name: "FK_submission_idempotency_form_submissions_FormSubmissionId_Te~",
                        columns: x => new { x.FormSubmissionId, x.TenantId },
                        principalTable: "form_submissions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_idempotency_survey_targets_SurveyTargetId_Tenant~",
                        columns: x => new { x.SurveyTargetId, x.TenantId },
                        principalTable: "survey_targets",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_program_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_program_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_survey_program_entries_survey_targets_SurveyTargetId_Tenant~",
                        columns: x => new { x.SurveyTargetId, x.TenantId, x.EventId, x.PresentationId },
                        principalTable: "survey_targets",
                        principalColumns: new[] { "Id", "TenantId", "EventId", "PresentationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_public_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedReprintValue = table.Column<string>(type: "text", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_public_codes", x => x.Id);
                    table.CheckConstraint("CK_survey_public_codes_digest", " \"CodeDigest\" ~ '^[0-9a-f]{64}$' ");
                    table.CheckConstraint("CK_survey_public_codes_subject", "num_nonnulls(\"EventId\", \"SurveyTargetId\") = 1");
                    table.ForeignKey(
                        name: "FK_survey_public_codes_activity_events_EventId_TenantId",
                        columns: x => new { x.EventId, x.TenantId },
                        principalTable: "activity_events",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_public_codes_survey_targets_SurveyTargetId_TenantId",
                        columns: x => new { x.SurveyTargetId, x.TenantId },
                        principalTable: "survey_targets",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "target_question_set_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_target_question_set_versions", x => x.Id);
                    table.UniqueConstraint("AK_target_question_set_versions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.UniqueConstraint("AK_target_question_set_versions_Id_TenantId_SurveyTargetId", x => new { x.Id, x.TenantId, x.SurveyTargetId });
                    table.CheckConstraint("CK_target_question_set_versions_number", "\"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_target_question_set_versions_survey_targets_SurveyTargetId_~",
                        columns: x => new { x.SurveyTargetId, x.TenantId },
                        principalTable: "survey_targets",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_feedback_settings_EventId_TenantId",
                table: "event_feedback_settings",
                columns: new[] { "EventId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_event_feedback_settings_TenantId",
                table: "event_feedback_settings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_event_feedback_settings_TenantId_EventId",
                table: "event_feedback_settings",
                columns: new[] { "TenantId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_CreatedAt",
                table: "form_definitions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_CreatedByUserId",
                table: "form_definitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_DeletedAt",
                table: "form_definitions",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_DeletedByUserId",
                table: "form_definitions",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_GroupId_TenantId",
                table: "form_definitions",
                columns: new[] { "GroupId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_ProjectId_TenantId",
                table: "form_definitions",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_TenantId",
                table: "form_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_definitions_WorkspaceId_TenantId",
                table: "form_definitions",
                columns: new[] { "WorkspaceId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_FormVersionId_TenantId",
                table: "form_field_definitions",
                columns: new[] { "FormVersionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TargetQuestionSetVersionId_TenantId",
                table: "form_field_definitions",
                columns: new[] { "TargetQuestionSetVersionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TenantId",
                table: "form_field_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TenantId_FormVersionId_FieldKey",
                table: "form_field_definitions",
                columns: new[] { "TenantId", "FormVersionId", "FieldKey" },
                unique: true,
                filter: "\"FormVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TenantId_FormVersionId_SortOrder",
                table: "form_field_definitions",
                columns: new[] { "TenantId", "FormVersionId", "SortOrder" },
                unique: true,
                filter: "\"FormVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TenantId_TargetQuestionSetVersionId_~",
                table: "form_field_definitions",
                columns: new[] { "TenantId", "TargetQuestionSetVersionId", "FieldKey" },
                unique: true,
                filter: "\"TargetQuestionSetVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_form_field_definitions_TenantId_TargetQuestionSetVersionId~1",
                table: "form_field_definitions",
                columns: new[] { "TenantId", "TargetQuestionSetVersionId", "SortOrder" },
                unique: true,
                filter: "\"TargetQuestionSetVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_form_submission_values_FormFieldDefinitionId_TenantId",
                table: "form_submission_values",
                columns: new[] { "FormFieldDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submission_values_FormSubmissionId_TenantId_FormFieldD~",
                table: "form_submission_values",
                columns: new[] { "FormSubmissionId", "TenantId", "FormFieldDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_submission_values_PublishedSurveySchemaId_FormFieldDef~",
                table: "form_submission_values",
                columns: new[] { "PublishedSurveySchemaId", "FormFieldDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submission_values_TenantId",
                table: "form_submission_values",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_FormVersionId_TenantId_FormDefinitionId",
                table: "form_submissions",
                columns: new[] { "FormVersionId", "TenantId", "FormDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_PublishedSurveySchemaId_TenantId_SurveyTar~",
                table: "form_submissions",
                columns: new[] { "PublishedSurveySchemaId", "TenantId", "SurveyTargetId", "FormDefinitionId", "FormVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_RespondentUserId",
                table: "form_submissions",
                column: "RespondentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_schema_identity",
                table: "form_submissions",
                columns: new[] { "Id", "TenantId", "PublishedSurveySchemaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_target_identity",
                table: "form_submissions",
                columns: new[] { "Id", "TenantId", "SurveyTargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId",
                table: "form_submissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId_FormDefinitionId_RespondentUserId",
                table: "form_submissions",
                columns: new[] { "TenantId", "FormDefinitionId", "RespondentUserId" },
                unique: true,
                filter: " \"SurveyTargetId\" IS NULL AND \"RespondentUserId\" IS NOT NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_TenantId_SurveyTargetId_SubmittedAtUtc_Id",
                table: "form_submissions",
                columns: new[] { "TenantId", "SurveyTargetId", "SubmittedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_FormDefinitionId_TenantId",
                table: "form_versions",
                columns: new[] { "FormDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_TenantId",
                table: "form_versions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_form_versions_TenantId_FormDefinitionId_Number",
                table: "form_versions",
                columns: new[] { "TenantId", "FormDefinitionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_presentation_feedback_settings_PresentationId_TenantId",
                table: "presentation_feedback_settings",
                columns: new[] { "PresentationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_presentation_feedback_settings_TenantId",
                table: "presentation_feedback_settings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_presentation_feedback_settings_TenantId_PresentationId",
                table: "presentation_feedback_settings",
                columns: new[] { "TenantId", "PresentationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_presentation_participants_PresentationId_TenantId",
                table: "presentation_participants",
                columns: new[] { "PresentationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_presentation_participants_TenantId",
                table: "presentation_participants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_presentation_participants_TenantId_PresentationId_PendingRo~",
                table: "presentation_participants",
                columns: new[] { "TenantId", "PresentationId", "PendingRosterRef", "Role" },
                unique: true,
                filter: " \"PendingRosterRef\" IS NOT NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_presentation_participants_TenantId_PresentationId_UserId_Ro~",
                table: "presentation_participants",
                columns: new[] { "TenantId", "PresentationId", "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_presentation_participants_TenantId_UserId_Role",
                table: "presentation_participants",
                columns: new[] { "TenantId", "UserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_presentations_EventId_TenantId",
                table: "presentations",
                columns: new[] { "EventId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_presentations_ProjectId_TenantId",
                table: "presentations",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_presentations_TenantId",
                table: "presentations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schema_fields_FormFieldDefinitionId_Tenant~",
                table: "published_survey_schema_fields",
                columns: new[] { "FormFieldDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schema_fields_PublishedSurveySchemaId_Tena~",
                table: "published_survey_schema_fields",
                columns: new[] { "PublishedSurveySchemaId", "TenantId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schema_fields_TenantId",
                table: "published_survey_schema_fields",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schema_version_pair",
                table: "published_survey_schemas",
                columns: new[] { "SurveyTargetId", "TenantId", "FormVersionId", "TargetQuestionSetVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schemas_FormVersionId_TenantId_FormDefinit~",
                table: "published_survey_schemas",
                columns: new[] { "FormVersionId", "TenantId", "FormDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schemas_SurveyTargetId_TenantId_FormDefini~",
                table: "published_survey_schemas",
                columns: new[] { "SurveyTargetId", "TenantId", "FormDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schemas_SurveyTargetId_TenantId_VersionRef",
                table: "published_survey_schemas",
                columns: new[] { "SurveyTargetId", "TenantId", "VersionRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schemas_TargetQuestionSetVersionId_TenantI~",
                table: "published_survey_schemas",
                columns: new[] { "TargetQuestionSetVersionId", "TenantId", "SurveyTargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_published_survey_schemas_TenantId",
                table: "published_survey_schemas",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_idempotency_ExpiresAtUtc",
                table: "submission_idempotency",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_submission_idempotency_FormSubmissionId_TenantId",
                table: "submission_idempotency",
                columns: new[] { "FormSubmissionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_idempotency_SurveyTargetId_TenantId",
                table: "submission_idempotency",
                columns: new[] { "SurveyTargetId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_idempotency_TenantId",
                table: "submission_idempotency",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_idempotency_TenantId_SurveyTargetId_KeyDigest",
                table: "submission_idempotency",
                columns: new[] { "TenantId", "SurveyTargetId", "KeyDigest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_campaigns_EventId_TenantId",
                table: "survey_campaigns",
                columns: new[] { "EventId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_campaigns_FormDefinitionId_TenantId",
                table: "survey_campaigns",
                columns: new[] { "FormDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_campaigns_TenantId",
                table: "survey_campaigns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_program_entries_SurveyTargetId_TenantId_EventId_Pres~",
                table: "survey_program_entries",
                columns: new[] { "SurveyTargetId", "TenantId", "EventId", "PresentationId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_program_entries_TenantId",
                table: "survey_program_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_program_entries_TenantId_EventId_PresentationId",
                table: "survey_program_entries",
                columns: new[] { "TenantId", "EventId", "PresentationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_CodeDigest",
                table: "survey_public_codes",
                column: "CodeDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_EventId_TenantId",
                table: "survey_public_codes",
                columns: new[] { "EventId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_SurveyTargetId_TenantId",
                table: "survey_public_codes",
                columns: new[] { "SurveyTargetId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_TenantId",
                table: "survey_public_codes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_TenantId_EventId",
                table: "survey_public_codes",
                columns: new[] { "TenantId", "EventId" },
                unique: true,
                filter: " \"EventId\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_survey_public_codes_TenantId_SurveyTargetId",
                table: "survey_public_codes",
                columns: new[] { "TenantId", "SurveyTargetId" },
                unique: true,
                filter: " \"SurveyTargetId\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_survey_respondent_identities_AccountUserId",
                table: "survey_respondent_identities",
                column: "AccountUserId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_respondent_identities_FormSubmissionId_TenantId",
                table: "survey_respondent_identities",
                columns: new[] { "FormSubmissionId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_respondent_identities_TenantId",
                table: "survey_respondent_identities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_CampaignId_TenantId_EventId_FormDefinitionId",
                table: "survey_targets",
                columns: new[] { "CampaignId", "TenantId", "EventId", "FormDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_LocalMarkerKey",
                table: "survey_targets",
                column: "LocalMarkerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_PresentationId_TenantId_EventId",
                table: "survey_targets",
                columns: new[] { "PresentationId", "TenantId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_PublishedSchemaId_TenantId_Id",
                table: "survey_targets",
                columns: new[] { "PublishedSchemaId", "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_TenantId",
                table: "survey_targets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_survey_targets_TenantId_CampaignId_PresentationId",
                table: "survey_targets",
                columns: new[] { "TenantId", "CampaignId", "PresentationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_target_question_set_versions_SurveyTargetId_TenantId",
                table: "target_question_set_versions",
                columns: new[] { "SurveyTargetId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_target_question_set_versions_TenantId",
                table: "target_question_set_versions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_target_question_set_versions_TenantId_SurveyTargetId_Number",
                table: "target_question_set_versions",
                columns: new[] { "TenantId", "SurveyTargetId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_form_field_definitions_target_question_set_versions_TargetQ~",
                table: "form_field_definitions",
                columns: new[] { "TargetQuestionSetVersionId", "TenantId" },
                principalTable: "target_question_set_versions",
                principalColumns: new[] { "Id", "TenantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_submission_values_form_submissions_FormSubmissionId_Te~",
                table: "form_submission_values",
                columns: new[] { "FormSubmissionId", "TenantId" },
                principalTable: "form_submissions",
                principalColumns: new[] { "Id", "TenantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_submission_values_published_survey_schema_fields_Publi~",
                table: "form_submission_values",
                columns: new[] { "PublishedSurveySchemaId", "FormFieldDefinitionId", "TenantId" },
                principalTable: "published_survey_schema_fields",
                principalColumns: new[] { "PublishedSurveySchemaId", "FormFieldDefinitionId", "TenantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_form_submissions_published_survey_schemas_PublishedSurveySc~",
                table: "form_submissions",
                columns: new[] { "PublishedSurveySchemaId", "TenantId", "SurveyTargetId", "FormDefinitionId", "FormVersionId" },
                principalTable: "published_survey_schemas",
                principalColumns: new[] { "Id", "TenantId", "SurveyTargetId", "FormDefinitionId", "FormVersionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_survey_schema_fields_published_survey_schemas_Pub~",
                table: "published_survey_schema_fields",
                columns: new[] { "PublishedSurveySchemaId", "TenantId" },
                principalTable: "published_survey_schemas",
                principalColumns: new[] { "Id", "TenantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_survey_schemas_survey_targets_SurveyTargetId_Tena~",
                table: "published_survey_schemas",
                columns: new[] { "SurveyTargetId", "TenantId", "FormDefinitionId" },
                principalTable: "survey_targets",
                principalColumns: new[] { "Id", "TenantId", "FormDefinitionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_survey_schemas_target_question_set_versions_Targe~",
                table: "published_survey_schemas",
                columns: new[] { "TargetQuestionSetVersionId", "TenantId", "SurveyTargetId" },
                principalTable: "target_question_set_versions",
                principalColumns: new[] { "Id", "TenantId", "SurveyTargetId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "AK_activity_events_presentation_project"
                    ON activity_events ("Id", "TenantId", "ProjectId");

                ALTER TABLE presentations ADD CONSTRAINT "FK_presentations_event_project"
                    FOREIGN KEY ("EventId", "TenantId", "ProjectId")
                    REFERENCES activity_events ("Id", "TenantId", "ProjectId") ON DELETE RESTRICT;

                ALTER TABLE submission_idempotency ADD CONSTRAINT "FK_submission_idempotency_submission_target"
                    FOREIGN KEY ("FormSubmissionId", "TenantId", "SurveyTargetId")
                    REFERENCES form_submissions ("Id", "TenantId", "SurveyTargetId") ON DELETE RESTRICT;

                CREATE FUNCTION guard_survey_publication_immutability() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Published schema records are immutable' USING ERRCODE = '23514';
                END;
                $$;

                CREATE TRIGGER form_versions_immutable BEFORE UPDATE OR DELETE ON form_versions
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_immutability();
                CREATE TRIGGER target_question_set_versions_immutable BEFORE UPDATE OR DELETE ON target_question_set_versions
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_immutability();
                CREATE TRIGGER form_field_definitions_immutable BEFORE UPDATE OR DELETE ON form_field_definitions
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_immutability();
                CREATE TRIGGER published_survey_schemas_immutable BEFORE UPDATE OR DELETE ON published_survey_schemas
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_immutability();
                CREATE TRIGGER published_survey_schema_fields_immutable BEFORE UPDATE OR DELETE ON published_survey_schema_fields
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_immutability();

                CREATE FUNCTION guard_survey_publication_field() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM published_survey_schemas AS schema_record
                        JOIN form_field_definitions AS field
                          ON field."Id" = NEW."FormFieldDefinitionId" AND field."TenantId" = NEW."TenantId"
                        WHERE schema_record."Id" = NEW."PublishedSurveySchemaId"
                          AND schema_record."TenantId" = NEW."TenantId"
                          AND (field."FormVersionId" = schema_record."FormVersionId"
                            OR field."TargetQuestionSetVersionId" = schema_record."TargetQuestionSetVersionId"))
                    THEN
                        RAISE EXCEPTION 'Published field belongs to a different version' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER published_survey_schema_fields_membership BEFORE INSERT ON published_survey_schema_fields
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_publication_field();

                CREATE FUNCTION guard_form_submission_value() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM form_submissions AS submission
                        JOIN form_field_definitions AS field
                          ON field."Id" = NEW."FormFieldDefinitionId" AND field."TenantId" = NEW."TenantId"
                        WHERE submission."Id" = NEW."FormSubmissionId"
                          AND submission."TenantId" = NEW."TenantId"
                          AND submission."PublishedSurveySchemaId" IS NOT DISTINCT FROM NEW."PublishedSurveySchemaId"
                          AND (NEW."PublishedSurveySchemaId" IS NOT NULL OR field."FormVersionId" = submission."FormVersionId"))
                    THEN
                        RAISE EXCEPTION 'Submission value belongs to a different version' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER form_submission_values_membership BEFORE INSERT ON form_submission_values
                    FOR EACH ROW EXECUTE FUNCTION guard_form_submission_value();

                CREATE FUNCTION guard_survey_identity_subject() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM form_submissions AS submission
                        WHERE submission."Id" = NEW."FormSubmissionId"
                          AND submission."TenantId" = NEW."TenantId" AND submission."SurveyTargetId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Survey identity requires a survey submission' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER survey_respondent_identities_subject BEFORE INSERT OR UPDATE ON survey_respondent_identities
                    FOR EACH ROW EXECUTE FUNCTION guard_survey_identity_subject();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM form_submissions) THEN
                        RAISE EXCEPTION 'Reversing the foundation would discard accepted submissions' USING ERRCODE = '23514';
                    END IF;
                END;
                $$;

                DROP TRIGGER survey_respondent_identities_subject ON survey_respondent_identities;
                DROP FUNCTION guard_survey_identity_subject();
                DROP TRIGGER form_submission_values_membership ON form_submission_values;
                DROP FUNCTION guard_form_submission_value();
                DROP TRIGGER published_survey_schema_fields_membership ON published_survey_schema_fields;
                DROP FUNCTION guard_survey_publication_field();
                DROP TRIGGER published_survey_schema_fields_immutable ON published_survey_schema_fields;
                DROP TRIGGER published_survey_schemas_immutable ON published_survey_schemas;
                DROP TRIGGER form_field_definitions_immutable ON form_field_definitions;
                DROP TRIGGER target_question_set_versions_immutable ON target_question_set_versions;
                DROP TRIGGER form_versions_immutable ON form_versions;
                DROP FUNCTION guard_survey_publication_immutability();
                ALTER TABLE submission_idempotency DROP CONSTRAINT "FK_submission_idempotency_submission_target";
                ALTER TABLE presentations DROP CONSTRAINT "FK_presentations_event_project";
                DROP INDEX "AK_activity_events_presentation_project";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_published_survey_schemas_form_versions_FormVersionId_Tenant~",
                table: "published_survey_schemas");

            migrationBuilder.DropForeignKey(
                name: "FK_published_survey_schemas_target_question_set_versions_Targe~",
                table: "published_survey_schemas");

            migrationBuilder.DropForeignKey(
                name: "FK_survey_targets_published_survey_schemas_PublishedSchemaId_T~",
                table: "survey_targets");

            migrationBuilder.DropTable(
                name: "event_feedback_settings");

            migrationBuilder.DropTable(
                name: "form_submission_values");

            migrationBuilder.DropTable(
                name: "presentation_feedback_settings");

            migrationBuilder.DropTable(
                name: "presentation_participants");

            migrationBuilder.DropTable(
                name: "submission_idempotency");

            migrationBuilder.DropTable(
                name: "survey_program_entries");

            migrationBuilder.DropTable(
                name: "survey_public_codes");

            migrationBuilder.DropTable(
                name: "survey_respondent_identities");

            migrationBuilder.DropTable(
                name: "published_survey_schema_fields");

            migrationBuilder.DropTable(
                name: "form_submissions");

            migrationBuilder.DropTable(
                name: "form_field_definitions");

            migrationBuilder.DropTable(
                name: "form_versions");

            migrationBuilder.DropTable(
                name: "target_question_set_versions");

            migrationBuilder.DropTable(
                name: "published_survey_schemas");

            migrationBuilder.DropTable(
                name: "survey_targets");

            migrationBuilder.DropTable(
                name: "presentations");

            migrationBuilder.DropTable(
                name: "survey_campaigns");

            migrationBuilder.DropTable(
                name: "form_definitions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workspaces_Id_TenantId",
                table: "workspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tenant_users_TenantId_UserId",
                table: "tenant_users");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_groups_Id_TenantId",
                table: "groups");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_activity_events_Id_TenantId",
                table: "activity_events");
        }
    }
}
