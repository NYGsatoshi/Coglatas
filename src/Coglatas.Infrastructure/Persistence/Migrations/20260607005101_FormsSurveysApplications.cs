using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormsSurveysApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "internal_forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FormType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OpensAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_forms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_internal_forms_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_internal_forms_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_internal_forms_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_internal_forms_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "form_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    QuestionType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_questions_internal_forms_FormId",
                        column: x => x.FormId,
                        principalTable: "internal_forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_responses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespondentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_responses_internal_forms_FormId",
                        column: x => x.FormId,
                        principalTable: "internal_forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_responses_users_RespondentUserId",
                        column: x => x.RespondentUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "form_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormResponseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AnswerJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_answers_form_questions_FormQuestionId",
                        column: x => x.FormQuestionId,
                        principalTable: "form_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_form_answers_form_responses_FormResponseId",
                        column: x => x.FormResponseId,
                        principalTable: "form_responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_CreatedAt",
                table: "form_answers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_FormQuestionId",
                table: "form_answers",
                column: "FormQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_FormResponseId",
                table: "form_answers",
                column: "FormResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_form_answers_FormResponseId_FormQuestionId",
                table: "form_answers",
                columns: new[] { "FormResponseId", "FormQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_questions_CreatedAt",
                table: "form_questions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_questions_FormId",
                table: "form_questions",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_form_questions_FormId_SortOrder",
                table: "form_questions",
                columns: new[] { "FormId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_CreatedAt",
                table: "form_responses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_FormId",
                table: "form_responses",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_FormId_RespondentUserId",
                table: "form_responses",
                columns: new[] { "FormId", "RespondentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_RespondentUserId",
                table: "form_responses",
                column: "RespondentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_form_responses_SubmittedAt",
                table: "form_responses",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_ClosesAt",
                table: "internal_forms",
                column: "ClosesAt");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_CreatedAt",
                table: "internal_forms",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_CreatedByUserId",
                table: "internal_forms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_DeletedAt",
                table: "internal_forms",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_FormType",
                table: "internal_forms",
                column: "FormType");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_GroupId",
                table: "internal_forms",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_OpensAt",
                table: "internal_forms",
                column: "OpensAt");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_ProjectId",
                table: "internal_forms",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_Status",
                table: "internal_forms",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_internal_forms_WorkspaceId",
                table: "internal_forms",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_answers");

            migrationBuilder.DropTable(
                name: "form_questions");

            migrationBuilder.DropTable(
                name: "form_responses");

            migrationBuilder.DropTable(
                name: "internal_forms");
        }
    }
}
