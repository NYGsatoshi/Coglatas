using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101062657_StabilizeLogicalRecoveryConstraintExpressions")]
    public sealed class StabilizeLogicalRecoveryConstraintExpressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReplaceMigrationOnlyChecks(migrationBuilder, stable: true);
            migrationBuilder.DropCheckConstraint(
                name: "CK_task_deadline_digest_jobs_completion",
                table: "task_deadline_digest_jobs");

            migrationBuilder.DropIndex(
                name: "IX_task_deadline_digest_attempts_one_active",
                table: "task_deadline_digest_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_deadline_digest_attempts_completion",
                table: "task_deadline_digest_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_context",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_lifecycle",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_mode",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_reason",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_rule_results_execution",
                table: "security_evaluation_rule_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_rule_results_reason",
                table: "security_evaluation_rule_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_deadline_digest_jobs_completion",
                table: "task_deadline_digest_jobs",
                sql: "((\"Status\"::text IN ('Succeeded', 'Failed')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\"::text IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_one_active",
                table: "task_deadline_digest_attempts",
                column: "JobId",
                unique: true,
                filter: "\"Status\"::text IN ('Pending', 'Claimed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_deadline_digest_attempts_completion",
                table: "task_deadline_digest_attempts",
                sql: "((\"Status\"::text IN ('Succeeded', 'Failed', 'Expired', 'Deferred')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\"::text IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_context",
                table: "security_evaluation_runs",
                sql: "\"ContextKind\"::text IN ('coglatas.committed', 'coglatas.candidate', 'coglatas.scenario')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_lifecycle",
                table: "security_evaluation_runs",
                sql: "(\"Status\" = 'Pending' AND \"Outcome\" IS NULL AND \"TerminalAtUtc\" IS NULL AND \"ReasonCode\" = 'NotExecuted') OR\r\n(\"Status\"::text IN ('Completed', 'Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalAtUtc\" >= \"CreatedAtUtc\"\r\n AND ((\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\"::text IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\r\n      (\"Status\" <> 'Completed' AND \"Outcome\" IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_mode",
                table: "security_evaluation_runs",
                sql: "\"EnforcementMode\"::text IN ('Disabled', 'Shadow')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_reason",
                table: "security_evaluation_runs",
                sql: "(\"Status\"::text IN ('Pending', 'NotExecuted') AND \"ReasonCode\"::text IN ('NotExecuted', 'RuleNotExecuted')) OR\r\n(\"Status\" = 'Failed' AND \"ReasonCode\"::text IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\r\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\r\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\r\n(\"Status\" = 'Completed' AND (\r\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\"::text IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\r\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\r\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\"::text IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\r\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\"::text IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_rule_results_execution",
                table: "security_evaluation_rule_results",
                sql: "(\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\"::text IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\r\n(\"Status\"::text IN ('Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"Outcome\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_rule_results_reason",
                table: "security_evaluation_rule_results",
                sql: "(\"Status\"::text IN ('Pending', 'NotExecuted') AND \"ReasonCode\"::text IN ('NotExecuted', 'RuleNotExecuted')) OR\r\n(\"Status\" = 'Failed' AND \"ReasonCode\"::text IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\r\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\r\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\r\n(\"Status\" = 'Completed' AND (\r\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\"::text IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\r\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\r\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\"::text IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\r\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\"::text IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects",
                sql: "(\"ActivationState\" = 'Activated' AND \"ActivatedAtUtc\" IS NOT NULL AND \"ActivationVersion\" IS NOT NULL AND \"ActivationVersion\" > 0) OR (\"ActivationState\"::text IN ('LegacyUnknown', 'NeverActivated') AND \"ActivatedAtUtc\" IS NULL AND \"ActivationVersion\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects",
                sql: "\"ActivationState\"::text IN ('LegacyUnknown', 'NeverActivated', 'Activated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects",
                sql: "\"Visibility\" IS NULL OR \"Visibility\"::text IN ('WorkspaceVisible', 'MembersOnly', 'Restricted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReplaceMigrationOnlyChecks(migrationBuilder, stable: false);
            migrationBuilder.DropCheckConstraint(
                name: "CK_task_deadline_digest_jobs_completion",
                table: "task_deadline_digest_jobs");

            migrationBuilder.DropIndex(
                name: "IX_task_deadline_digest_attempts_one_active",
                table: "task_deadline_digest_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_deadline_digest_attempts_completion",
                table: "task_deadline_digest_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_context",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_lifecycle",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_mode",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_runs_reason",
                table: "security_evaluation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_rule_results_execution",
                table: "security_evaluation_rule_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_security_evaluation_rule_results_reason",
                table: "security_evaluation_rule_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_deadline_digest_jobs_completion",
                table: "task_deadline_digest_jobs",
                sql: "((\"Status\" IN ('Succeeded', 'Failed')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_task_deadline_digest_attempts_one_active",
                table: "task_deadline_digest_attempts",
                column: "JobId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Claimed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_deadline_digest_attempts_completion",
                table: "task_deadline_digest_attempts",
                sql: "((\"Status\" IN ('Succeeded', 'Failed', 'Expired', 'Deferred')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_context",
                table: "security_evaluation_runs",
                sql: "\"ContextKind\" IN ('coglatas.committed', 'coglatas.candidate', 'coglatas.scenario')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_lifecycle",
                table: "security_evaluation_runs",
                sql: "(\"Status\" = 'Pending' AND \"Outcome\" IS NULL AND \"TerminalAtUtc\" IS NULL AND \"ReasonCode\" = 'NotExecuted') OR\n(\"Status\" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalAtUtc\" >= \"CreatedAtUtc\"\n AND ((\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n      (\"Status\" <> 'Completed' AND \"Outcome\" IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_mode",
                table: "security_evaluation_runs",
                sql: "\"EnforcementMode\" IN ('Disabled', 'Shadow')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_runs_reason",
                table: "security_evaluation_runs",
                sql: "(\"Status\" IN ('Pending', 'NotExecuted') AND \"ReasonCode\" IN ('NotExecuted', 'RuleNotExecuted')) OR\n(\"Status\" = 'Failed' AND \"ReasonCode\" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\n(\"Status\" = 'Completed' AND (\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_rule_results_execution",
                table: "security_evaluation_rule_results",
                sql: "(\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n(\"Status\" IN ('Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"Outcome\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_security_evaluation_rule_results_reason",
                table: "security_evaluation_rule_results",
                sql: "(\"Status\" IN ('Pending', 'NotExecuted') AND \"ReasonCode\" IN ('NotExecuted', 'RuleNotExecuted')) OR\n(\"Status\" = 'Failed' AND \"ReasonCode\" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\n(\"Status\" = 'Completed' AND (\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_provenance",
                table: "projects",
                sql: "(\"ActivationState\" = 'Activated' AND \"ActivatedAtUtc\" IS NOT NULL AND \"ActivationVersion\" IS NOT NULL AND \"ActivationVersion\" > 0) OR (\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated') AND \"ActivatedAtUtc\" IS NULL AND \"ActivationVersion\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_activation_state",
                table: "projects",
                sql: "\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated', 'Activated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_visibility",
                table: "projects",
                sql: "\"Visibility\" IS NULL OR \"Visibility\" IN ('WorkspaceVisible', 'MembersOnly', 'Restricted')");
        }

        // These three checks are owned by earlier SQL migrations, not the EF model.
        // Keep their values and null semantics while selecting text before IN typing.
        private static void ReplaceMigrationOnlyChecks(MigrationBuilder migrationBuilder, bool stable)
        {
            ReplaceCheck(
                migrationBuilder,
                "task_execution_materialized_sources",
                "CK_task_execution_materialized_sources_media_type",
                stable
                    ? "\"MediaType\"::text IN ('text/plain', 'text/markdown')"
                    : "\"MediaType\" IN ('text/plain', 'text/markdown')");
            ReplaceCheck(
                migrationBuilder,
                "task_execution_runs",
                "CK_task_execution_runs_status",
                stable
                    ? "\"Status\"::text IN ('Accepted', 'Queued', 'Running', 'Succeeded', 'Failed')"
                    : "\"Status\" IN ('Accepted', 'Queued', 'Running', 'Succeeded', 'Failed')");
            ReplaceCheck(
                migrationBuilder,
                "task_workflow_template_stages",
                "CK_task_workflow_template_stages_category",
                stable
                    ? "\"InternalCategory\"::text IN ('Backlog', 'Todo', 'InProgress', 'Review', 'Done', 'Cancelled')"
                    : "\"InternalCategory\" IN ('Backlog', 'Todo', 'InProgress', 'Review', 'Done', 'Cancelled')");
        }

        private static void ReplaceCheck(MigrationBuilder migrationBuilder, string table, string name, string sql)
        {
            migrationBuilder.DropCheckConstraint(name, table);
            migrationBuilder.AddCheckConstraint(name, table, sql);
        }
    }
}
