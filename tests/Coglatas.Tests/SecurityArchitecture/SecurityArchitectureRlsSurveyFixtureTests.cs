using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureRlsSurveyFixtureTests
{
    [PostgreSqlFact]
    public Task SeedBuildsSeparateValidSurveyAndOrdinaryFormGraphs() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                foreach (var fixture in new[] { alpha, beta })
                {
                    var surveyValue = (FormSubmissionValue)fixture.Entities["form_submission_values"];
                    var surveyCount = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                        SELECT count(*)
                        FROM form_submission_values AS answer
                        JOIN form_submissions AS submission
                          ON submission."Id" = answer."FormSubmissionId"
                          AND submission."TenantId" = answer."TenantId"
                        JOIN survey_targets AS target
                          ON target."Id" = submission."SurveyTargetId"
                          AND target."TenantId" = submission."TenantId"
                          AND target."PublishedSchemaId" = submission."PublishedSurveySchemaId"
                        JOIN published_survey_schema_fields AS field
                          ON field."PublishedSurveySchemaId" = target."PublishedSchemaId"
                          AND field."FormFieldDefinitionId" = answer."FormFieldDefinitionId"
                          AND field."TenantId" = answer."TenantId"
                        JOIN survey_respondent_identities AS identity
                          ON identity."FormSubmissionId" = submission."Id"
                          AND identity."TenantId" = submission."TenantId"
                        JOIN submission_idempotency AS receipt
                          ON receipt."FormSubmissionId" = submission."Id"
                          AND receipt."TenantId" = submission."TenantId"
                          AND receipt."SurveyTargetId" = target."Id"
                        WHERE answer."Id" = @answer AND answer."TenantId" = @tenant
                          AND identity."EnteredName" IS NOT NULL AND identity."AccountUserId" IS NULL
                          AND submission."RespondentUserId" IS NULL AND receipt."StatusCode" = 201
                        """, ("answer", surveyValue.Id), ("tenant", fixture.Tenant));
                    Assert.Equal(1, surveyCount);

                    var ordinary = (FormAnswer)fixture.Entities["form_answers"];
                    var ordinaryCount = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                        SELECT count(*)
                        FROM form_answers AS answer
                        JOIN form_submission_values AS canonical
                          ON canonical."Id" = answer."Id" AND canonical."TenantId" = answer."TenantId"
                        JOIN form_submissions AS submission
                          ON submission."Id" = canonical."FormSubmissionId"
                          AND submission."TenantId" = canonical."TenantId"
                        WHERE answer."Id" = @answer AND answer."TenantId" = @tenant
                          AND canonical."LegacyQuestionId" = answer."FormQuestionId"
                          AND canonical."HasUninterpretedLegacyValue" = false
                          AND submission."SurveyTargetId" IS NULL
                        """, ("answer", ordinary.Id), ("tenant", fixture.Tenant));
                    Assert.Equal(1, ordinaryCount);

                    var feedbackCount = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                        SELECT count(*)
                        FROM feedback_viewer_grants AS invitation
                        JOIN feedback_invitation_policies AS policy
                          ON policy."Id" = invitation."InvitationPolicyId"
                          AND policy."TenantId" = invitation."TenantId"
                        JOIN presentations AS presentation
                          ON presentation."Id" = invitation."PresentationId"
                          AND presentation."TenantId" = invitation."TenantId"
                          AND presentation."EventId" = policy."EventId"
                        JOIN feedback_report_cases AS report
                          ON report."PresentationId" = presentation."Id"
                          AND report."TenantId" = presentation."TenantId"
                        JOIN answer_visibility_decisions AS visibility
                          ON visibility."FormSubmissionValueId" = report."FormSubmissionValueId"
                          AND visibility."TenantId" = report."TenantId"
                        JOIN survey_feedback_export_requests AS request
                          ON request."PresentationId" = presentation."Id"
                          AND request."TenantId" = presentation."TenantId"
                        JOIN survey_feedback_export_grants AS grant_row
                          ON grant_row."ExportRequestId" = request."Id"
                          AND grant_row."TenantId" = request."TenantId"
                          AND grant_row."RecipientUserId" = request."RequesterId"
                        WHERE invitation."TenantId" = @tenant AND report."FormSubmissionValueId" = @answer
                          AND invitation."InviterUserId" <> invitation."RecipientUserId"
                          AND policy."Enabled" AND visibility."IsHidden"
                        """, ("tenant", fixture.Tenant), ("answer", surveyValue.Id));
                    Assert.Equal(1, feedbackCount);
                }
            });
}
