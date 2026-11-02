using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101062657_StabilizeLogicalRecoveryConstraintExpressions")]
public sealed class StabilizeLogicalRecoveryConstraintExpressions : Migration
{
    private const string IndexTable = "task_deadline_digest_attempts";
    private const string IndexName = "IX_task_deadline_digest_attempts_one_active";
    private const string OriginalIndexPredicate = "\"Status\" IN ('Pending', 'Claimed')";
    private const string OriginalSecurityReason =
        "(\"Status\" IN ('Pending', 'NotExecuted') AND \"ReasonCode\" IN ('NotExecuted', 'RuleNotExecuted')) OR\n" +
        "(\"Status\" = 'Failed' AND \"ReasonCode\" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\n" +
        "(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\n" +
        "(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\n" +
        "(\"Status\" = 'Completed' AND (\n" +
        "    (\"Outcome\" = 'Allow' AND \"ReasonCode\" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\n" +
        "    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\n" +
        "    (\"Outcome\" = 'Unknown' AND \"ReasonCode\" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\n" +
        "    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))";

    // Frozen historical definitions: migration execution never consults the live EF model.
    // The first three tables' checks remain SQL-migration-owned, not model annotations.
    private static readonly ImmutableArray<CheckGroup> OriginalChecks =
    [
        new("task_execution_materialized_sources",
        [
            new("CK_task_execution_materialized_sources_media_type", "\"MediaType\" IN ('text/plain', 'text/markdown')")
        ]),
        new("task_execution_runs",
        [
            new("CK_task_execution_runs_status", "\"Status\" IN ('Accepted', 'Queued', 'Running', 'Succeeded', 'Failed')")
        ]),
        new("task_workflow_template_stages",
        [
            new("CK_task_workflow_template_stages_category", "\"InternalCategory\" IN ('Backlog', 'Todo', 'InProgress', 'Review', 'Done', 'Cancelled')")
        ]),
        new("task_deadline_digest_jobs",
        [
            new("CK_task_deadline_digest_jobs_completion", "((\"Status\" IN ('Succeeded', 'Failed')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)")
        ]),
        new(IndexTable,
        [
            new("CK_task_deadline_digest_attempts_completion", "((\"Status\" IN ('Succeeded', 'Failed', 'Expired', 'Deferred')) AND \"CompletedAt\" IS NOT NULL) OR ((\"Status\" IN ('Pending', 'Claimed')) AND \"CompletedAt\" IS NULL)")
        ]),
        new("security_evaluation_runs",
        [
            new("CK_security_evaluation_runs_context", "\"ContextKind\" IN ('coglatas.committed', 'coglatas.candidate', 'coglatas.scenario')"),
            new("CK_security_evaluation_runs_lifecycle",
                "(\"Status\" = 'Pending' AND \"Outcome\" IS NULL AND \"TerminalAtUtc\" IS NULL AND \"ReasonCode\" = 'NotExecuted') OR\n" +
                "(\"Status\" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalAtUtc\" >= \"CreatedAtUtc\"\n" +
                " AND ((\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n" +
                "      (\"Status\" <> 'Completed' AND \"Outcome\" IS NULL)))"),
            new("CK_security_evaluation_runs_mode", "\"EnforcementMode\" IN ('Disabled', 'Shadow')"),
            new("CK_security_evaluation_runs_reason", OriginalSecurityReason)
        ]),
        new("security_evaluation_rule_results",
        [
            new("CK_security_evaluation_rule_results_execution",
                "(\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n" +
                "(\"Status\" IN ('Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"Outcome\" IS NULL)"),
            new("CK_security_evaluation_rule_results_reason", OriginalSecurityReason)
        ]),
        new("projects",
        [
            new("CK_projects_activation_provenance", "(\"ActivationState\" = 'Activated' AND \"ActivatedAtUtc\" IS NOT NULL AND \"ActivationVersion\" IS NOT NULL AND \"ActivationVersion\" > 0) OR (\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated') AND \"ActivatedAtUtc\" IS NULL AND \"ActivationVersion\" IS NULL)"),
            new("CK_projects_activation_state", "\"ActivationState\" IN ('LegacyUnknown', 'NeverActivated', 'Activated')"),
            new("CK_projects_visibility", "\"Visibility\" IS NULL OR \"Visibility\" IN ('WorkspaceVisible', 'MembersOnly', 'Restricted')")
        ])
    ];

    private static readonly ImmutableArray<string> TextColumns =
        ["MediaType", "Status", "InternalCategory", "ContextKind", "Outcome", "EnforcementMode", "ReasonCode", "ActivationState", "Visibility"];

    protected override void Up(MigrationBuilder migrationBuilder) => ReplaceExpressions(migrationBuilder, stable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => ReplaceExpressions(migrationBuilder, stable: false);

    private static void ReplaceExpressions(MigrationBuilder migrationBuilder, bool stable)
    {
        foreach (var group in OriginalChecks)
        {
            foreach (var check in group.Checks)
            {
                migrationBuilder.DropCheckConstraint(check.Name, group.Table);
                migrationBuilder.AddCheckConstraint(check.Name, group.Table,
                    stable ? SelectTextBeforeIn(check.OriginalSql) : check.OriginalSql);
            }
        }

        migrationBuilder.DropIndex(IndexName, IndexTable);
        migrationBuilder.CreateIndex(IndexName, IndexTable, "JobId", unique: true,
            filter: stable ? SelectTextBeforeIn(OriginalIndexPredicate) : OriginalIndexPredicate);
    }

    private static string SelectTextBeforeIn(string sql)
    {
        foreach (var column in TextColumns)
            sql = sql.Replace($"\"{column}\" IN", $"\"{column}\"::text IN", StringComparison.Ordinal);
        return sql;
    }

    private sealed record CheckGroup(string Table, ImmutableArray<CheckDefinition> Checks);
    private sealed record CheckDefinition(string Name, string OriginalSql);
}
