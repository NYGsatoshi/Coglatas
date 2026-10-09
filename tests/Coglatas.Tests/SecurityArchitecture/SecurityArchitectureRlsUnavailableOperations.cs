using System.Security.Cryptography;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Current source dispositions, not approved operation applicability or RLS denial evidence.</summary>
internal static class SecurityArchitectureRlsUnavailableOperations
{
    internal sealed record SourceIdentity(string Path, string Digest);
    internal sealed record Disposition(string Operation, string ReasonCode, string Classification,
        string? AlternateLifecycle, IReadOnlyList<SourceIdentity> Sources,
        SecurityArchitectureRlsSchemaIdentity.Guard? Guard,
        SecurityArchitectureRlsSchemaIdentity.Constraint? Constraint);

    private sealed record Declaration(string Table, string[] Operations, string ReasonCode, string Migration,
        string? Function, string? Constraint = null, string? AlternateLifecycle = null);

    private static readonly Declaration[] Declarations =
    [
        new("audit_finding_decisions", ["UPDATE", "DELETE"], "AppendOnlyFindingDecision",
            "20260901093500_AddAuditFindingStructuredDecisions.cs", "reject_audit_finding_decision_mutation"),
        new("audit_finding_workflow_history", ["UPDATE", "DELETE"], "AppendOnlyFindingWorkflowHistory",
            "20260902074000_AddAuditFindingReviewWorkflow.cs", "reject_audit_finding_workflow_history_mutation"),
        new("coglatas_ui_canonical_change_journal", ["UPDATE", "DELETE"], "AppendOnlyCanonicalChangeJournal",
            "20260924133000_AddCoglatasUiCanonicalChangeJournal.cs", "coglatas_ui_reject_journal_rewrite"),
        new("file_versions", ["UPDATE", "DELETE"], "AppendOnlyFileVersionLedger",
            "20260901150000_AddFileVersionsActivityProjection.cs", "coglatas_file_versions_append_only_guard"),
        new("file_objects", ["DELETE"], "InitialImmutableVersionPreventsFileHardDelete",
            "20260901150000_AddFileVersionsActivityProjection.cs", null,
            "FK_file_versions_file_objects_FileObjectId", "PreserveLedgerAndUseReviewedFileLifecycle"),
        new("project_execution_scopes", ["DELETE"], "PersistentProjectExecutionDefault",
            "20260825081645_AddTaskExecutionScopeFoundation.cs", "project_execution_scope_delete_guard"),
        new("task_execution_runs", ["DELETE"], "PersistentTaskExecutionRun",
            "20260825081645_AddTaskExecutionScopeFoundation.cs", "task_execution_run_delete_guard"),
        new("security_evaluation_rule_results", ["UPDATE"], "ImmutableSecurityRuleResult",
            "20261009000906_AddSecurityEvaluationRecords.cs", "security_evaluation_rule_guard"),
        new("security_evaluation_rule_results", ["DELETE"], "SecurityRuleDirectDeleteRequiresAbsentParent",
            "20261009000906_AddSecurityEvaluationRecords.cs", "security_evaluation_rule_guard",
            AlternateLifecycle: "ParentDeletionCascadeOnlyRetentionAuthorityUnverified"),
        new("task_execution_materialized_sources", ["UPDATE", "DELETE"], "ImmutableMaterializedSourceProvenance",
            "20260830150000_AddTaskExecutionMaterializedSources.cs", "task_execution_materialized_source_guard"),
        new("task_execution_results", ["UPDATE", "DELETE"], "ImmutableTaskExecutionResult",
            "20260830220000_AddTaskExecutionResults.cs", "task_execution_result_guard"),
        new("task_execution_result_sources", ["UPDATE", "DELETE"], "ImmutableTaskExecutionResultSource",
            "20260830220000_AddTaskExecutionResults.cs", "task_execution_result_source_guard")
    ];

    public static string? Reason(string table, string operation) => Declarations
        .SingleOrDefault(item => item.Table == table && item.Operations.Contains(operation))?.ReasonCode;

    public static async Task<IReadOnlyList<Disposition>> BindAsync(string table,
        SecurityArchitectureRlsSchemaIdentity.Snapshot schema)
    {
        var result = new List<Disposition>();
        foreach (var declaration in Declarations.Where(item => item.Table == table))
        {
            var path = "src/Coglatas.Infrastructure/Persistence/Migrations/" + declaration.Migration;
            var bytes = await File.ReadAllBytesAsync(Path.Combine(FindRoot(), path));
            var source = new SourceIdentity(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var guard = declaration.Function is null ? null : Assert.Single(schema.Guards, item => item.FunctionName == declaration.Function);
            if (guard is not null) Assert.Equal("O", guard.Enabled);
            var constraint = declaration.Constraint is null ? null : Assert.Single(schema.Constraints, item => item.ConstraintName == declaration.Constraint);
            if (constraint is not null)
            {
                Assert.True(constraint.Validated);
                Assert.Equal("REFERENCING", constraint.Relationship);
                Assert.Equal("file_versions", constraint.ConstraintTable);
            }
            foreach (var operation in declaration.Operations)
            {
                if (guard is not null) Assert.NotEqual(0, guard.CommandMask & (operation == "UPDATE" ? 16 : 8));
                result.Add(new(operation, declaration.ReasonCode, "SOURCE_BLOCKED_DIRECT_MUTATION",
                    declaration.AlternateLifecycle, [source], guard, constraint));
            }
        }
        return result;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Source identities require the current repository checkout.");
    }
}
