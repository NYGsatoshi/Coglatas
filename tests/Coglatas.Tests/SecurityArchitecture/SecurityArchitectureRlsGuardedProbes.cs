using System.Security.Cryptography;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Explains native BEFORE-trigger observations without granting RLS-denial or authority credit.</summary>
internal static class SecurityArchitectureRlsGuardedProbes
{
    internal sealed record Probe(string Operation, string Situation, string SemanticReason,
        string Classification, string RlsQualification, string AuthorityDisposition,
        IReadOnlyList<string> ParentTables, SecurityArchitectureRlsUnavailableOperations.SourceIdentity Source,
        SecurityArchitectureRlsSchemaIdentity.Guard Guard);

    private sealed record Declaration(string Table, string Function, string Migration, string[] Parents,
        bool GuardsReassignment = false, bool ImmutableIdentity = false);

    private static readonly Declaration[] Declarations =
    [
        new("coglatas_ui_canonical_change_journal", "coglatas_ui_validate_journal_insert",
            "20260924133000_AddCoglatasUiCanonicalChangeJournal.cs", ["coglatas_ui_canonical_revision_heads"]),
        new("project_execution_scopes", "project_execution_scope_scope_guard",
            "20260825081645_AddTaskExecutionScopeFoundation.cs", ["projects"], true),
        new("security_evaluation_rule_results", "security_evaluation_rule_guard",
            "20261009000906_AddSecurityEvaluationRecords.cs", ["security_evaluation_runs"]),
        new("task_execution_materialized_sources", "task_execution_materialized_source_guard",
            "20260830150000_AddTaskExecutionMaterializedSources.cs", ["task_execution_runs", "attachments", "file_objects"]),
        new("task_execution_results", "task_execution_result_guard",
            "20260830220000_AddTaskExecutionResults.cs", ["task_execution_runs"]),
        new("task_execution_result_sources", "task_execution_result_source_guard",
            "20260830220000_AddTaskExecutionResults.cs", ["task_execution_results", "task_execution_materialized_sources"]),
        new("task_execution_runs", "task_execution_run_scope_and_snapshot_guard",
            "20260825081645_AddTaskExecutionScopeFoundation.cs", ["task_items"], true),
        new("task_execution_scope_overrides", "task_execution_scope_override_scope_guard",
            "20260825081645_AddTaskExecutionScopeFoundation.cs", ["task_items"], true),
        new("task_items", "task_items_v1_scope_guard",
            "20260719023631_TaskV1DomainPersistenceCompatibility.cs",
            ["projects", "groups", "tenant_users", "task_workflow_stages", "task_items"], true),
        new("coglatas_ui_canonical_revision_heads", "coglatas_ui_enforce_revision_head_step",
            "20260924133000_AddCoglatasUiCanonicalChangeJournal.cs", [], true, true),
        new("security_evaluation_runs", "security_evaluation_run_guard",
            "20261009000906_AddSecurityEvaluationRecords.cs", [], true, true)
    ];

    public static async Task<IReadOnlyList<Probe>> BindAsync(string table, SecurityArchitectureRlsSchemaIdentity.Snapshot native)
    {
        var probes = new List<Probe>();
        foreach (var declaration in Declarations.Where(item => item.Table == table))
        {
            var guard = Assert.Single(native.Guards, item => item.FunctionName == declaration.Function);
            Assert.Equal("O", guard.Enabled);
            var relative = "src/Coglatas.Infrastructure/Persistence/Migrations/" + declaration.Migration;
            var bytes = await File.ReadAllBytesAsync(Path.Combine(FindRoot(), relative));
            var source = new SecurityArchitectureRlsUnavailableOperations.SourceIdentity(relative,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            if (!declaration.ImmutableIdentity)
            {
                Assert.NotEqual(0, guard.CommandMask & 4);
                foreach (var situation in new[] { "crossTenant", "missingContext", "invalidContext" })
                    probes.Add(new("INSERT", situation, "ParentVisibilityOrCurrentStatePrecheck",
                        "SOURCE_PRECHECK_BEFORE_RLS_WITH_CHECK", "UNVERIFIED", "ConcreteParentLookupAuthorityRequiresOwnerReview",
                        declaration.Parents, source, guard));
            }
            if (declaration.GuardsReassignment)
            {
                Assert.NotEqual(0, guard.CommandMask & 16);
                probes.Add(new("UPDATE", "wrongOwnership", declaration.ImmutableIdentity ? "ImmutableBoundIdentity" : "ParentScopeMismatch",
                    "SOURCE_PRECHECK_BEFORE_RLS_WITH_CHECK", "UNVERIFIED", "ConcreteMutationAuthorityRequiresOwnerReview",
                    declaration.Parents, source, guard));
            }
        }
        Assert.All(probes, probe =>
        {
            Assert.Contains(probe.Operation, new[] { "INSERT", "UPDATE" });
            Assert.Contains(probe.Situation, new[] { "crossTenant", "missingContext", "invalidContext", "wrongOwnership" });
            Assert.Contains(probe.SemanticReason, new[] { "ParentVisibilityOrCurrentStatePrecheck", "ParentScopeMismatch", "ImmutableBoundIdentity" });
            Assert.Equal("SOURCE_PRECHECK_BEFORE_RLS_WITH_CHECK", probe.Classification);
            Assert.Equal("UNVERIFIED", probe.RlsQualification);
            Assert.Contains(probe.AuthorityDisposition, new[] { "ConcreteParentLookupAuthorityRequiresOwnerReview", "ConcreteMutationAuthorityRequiresOwnerReview" });
            Assert.All(probe.ParentTables, parent => Assert.Matches("^[a-z_][a-z0-9_]*$", parent));
            Assert.Matches("^[a-f0-9]{64}$", probe.Source.Digest);
            Assert.Contains(probe.Guard, native.Guards);
        });
        return probes;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Guard dispositions require the current repository checkout.");
    }
}
