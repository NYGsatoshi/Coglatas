using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Versioned physical relation membership and the separately authorized fixture scope.</summary>
internal static class SecurityArchitectureRlsRelationCatalogue
{
    public const string HistoricalVersion = "pre-aq-v1";
    public const string HistoricalMigration = "20261101062657_StabilizeLogicalRecoveryConstraintExpressions";
    private const string FoundationVersion = "aq-foundation-v2";
    private const string FeedbackVersion = "aq-feedback-v3";
    private const string FoundationMigration = "20261124084252_AddSurveyPresentationFoundation";
    private const string CutoverMigration = "20261124085803_CutOverOrdinaryFormsToCanonicalSubmissions";
    private const string PrivacyMigration = "20261124091200_ProtectOrdinaryRespondentPrivacy";
    private const string FeedbackMigration = "20261124091003_AddSurveyFeedbackLifecycleAndExports";
    private const string FieldCompatibilityMigration = "20261124091631_TrackCompatiblePublishedSurveyFields";
    internal static object HistoricalReceiptIdentity { get; } = new
    {
        version = HistoricalVersion, latestMigration = HistoricalMigration
    };

    internal sealed record Relation(string Name, string Kind, string? TenantColumnType);
    internal sealed record Snapshot(string Version, string LatestMigration, IReadOnlyList<Relation> Relations);
    internal sealed record Archive(string LegacyName, string ArchiveName, string TriggerName, string[] ViewSources);

    private static readonly string[] HistoricalUuidTables = """
        activity_events
        activity_logs
        announcement_drafts
        announcement_engagement_events
        announcement_reads
        announcements
        api_tokens
        artifact_claims
        artifact_evidence
        artifact_findings
        artifact_report_citations
        artifact_report_documents
        artifact_report_sections
        artifact_versions
        artifacts
        attachments
        audit_finding_decisions
        audit_finding_history
        audit_finding_workflow_history
        audit_logs
        capability_grants
        channel_members
        channels
        comments
        conversation_members
        conversations
        event_attendances
        export_jobs
        export_package_grants
        feedback
        file_access_grants
        file_download_grants
        file_folder_placements
        file_folder_root_states
        file_folders
        file_objects
        file_scan_results
        file_selection_snapshots
        file_versions
        form_answers
        form_questions
        form_responses
        group_members
        groups
        idempotency_records
        integration_accounts
        internal_forms
        invites
        message_attachments
        message_follow_ups
        messages
        milestones
        notification_user_states
        notifications
        outbox_events
        post_threads
        posts
        project_execution_scopes
        project_members
        project_task_labels
        projects
        radial_menu_items
        radial_menu_profiles
        read_states
        research_plan_revisions
        research_plan_steps
        research_plans
        security_evaluation_rule_results
        security_evaluation_runs
        security_events
        student_records
        subscriptions
        task_assignments
        task_checklist_items
        task_comments
        task_deadline_digest_attempts
        task_deadline_digest_jobs
        task_dependencies
        task_execution_materialized_sources
        task_execution_result_sources
        task_execution_results
        task_execution_runs
        task_execution_scope_overrides
        task_execution_source_policy_documents
        task_item_collaborators
        task_items
        task_migration_inventory
        task_workflow_definitions
        task_workflow_stages
        task_workflow_template_stages
        task_workflow_templates
        tenant_settings
        tenant_task_workflow_defaults
        tenant_users
        usage_records
        user_layouts
        webhook_endpoints
        work_item_labels
        work_item_watch_states
        workspace_members
        workspace_task_workflow_defaults
        workspaces
        """.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private const string TextTenantTable = "coglatas_ui_canonical_revision_heads";
    private static readonly string[] ParentDerivedTables =
        ["coglatas_ui_canonical_change_journal", "file_selection_snapshot_items"];
    private static readonly string[] GlobalSharedTables = ["command_definitions", "feature_modules", "panel_definitions", "plans"];
    private static readonly string[] InternalTables = ["__EFMigrationsHistory", "system_settings"];
    private static readonly string[] IdentityRootTables = ["sessions", "tenants", "users"];

    internal static IReadOnlyList<Archive> FormsArchives { get; } =
    [
        new("form_answers", "pre_aq_form_answers", "pre_aq_answers_fenced", ["form_submission_values"]),
        new("form_responses", "pre_aq_form_responses", "pre_aq_responses_fenced", ["form_definitions", "form_submissions"]),
        new("internal_forms", "pre_aq_internal_forms", "pre_aq_forms_fenced", ["form_definitions"])
    ];

    internal sealed record AqTable(string Name, string SourceMigration, string[] TenantParents, string[] SourceBlockedOperations);

    private static readonly AqTable[] FoundationTables =
    [
        new("event_feedback_settings", FoundationMigration, ["activity_events"], []),
        new("form_definitions", FoundationMigration, ["groups", "projects", "workspaces"], []),
        new("form_field_definitions", FoundationMigration, ["form_versions", "target_question_set_versions"], ["UPDATE", "DELETE"]),
        new("form_submission_values", FoundationMigration, ["form_field_definitions", "form_submissions", "published_survey_schema_fields"], []),
        new("form_submissions", FoundationMigration, ["form_versions", "published_survey_schemas"], []),
        new("form_versions", FoundationMigration, ["form_definitions"], ["UPDATE", "DELETE"]),
        new("presentation_feedback_settings", FoundationMigration, ["presentations"], []),
        new("presentation_participants", FoundationMigration, ["presentations", "tenant_users"], []),
        new("presentations", FoundationMigration, ["activity_events", "projects"], []),
        new("published_survey_schema_fields", FoundationMigration, ["form_field_definitions", "published_survey_schemas"], ["UPDATE", "DELETE"]),
        new("published_survey_schemas", FoundationMigration, ["form_versions", "survey_targets", "target_question_set_versions"], ["UPDATE", "DELETE"]),
        new("submission_idempotency", FoundationMigration, ["form_submissions", "survey_targets"], []),
        new("survey_campaigns", FoundationMigration, ["activity_events", "form_definitions"], []),
        new("survey_program_entries", FoundationMigration, ["survey_targets"], []),
        new("survey_public_codes", FoundationMigration, ["activity_events", "survey_targets"], []),
        new("survey_respondent_identities", FoundationMigration, ["form_submissions"], []),
        new("survey_targets", FoundationMigration, ["presentations", "published_survey_schemas", "survey_campaigns"], []),
        new("target_question_set_versions", FoundationMigration, ["survey_targets"], ["UPDATE", "DELETE"])
    ];

    private static readonly AqTable[] FeedbackTables =
    [
        new("answer_visibility_decisions", FeedbackMigration, ["form_submission_values"], []),
        new("feedback_invitation_policies", FeedbackMigration, ["activity_events"], []),
        new("feedback_read_states", FeedbackMigration, ["form_submissions"], []),
        new("feedback_report_cases", FeedbackMigration, ["form_submission_values", "presentations"], []),
        new("feedback_viewer_grants", FeedbackMigration, ["feedback_invitation_policies", "presentations"], []),
        new("shared_teacher_feedback", FeedbackMigration, ["presentations"], []),
        new("survey_feedback_export_grants", FeedbackMigration, ["survey_feedback_export_requests"], []),
        new("survey_feedback_export_requests", FeedbackMigration, ["presentations"], []),
        new("teacher_private_notes", FeedbackMigration, ["presentations"], [])
    ];

    private static IEnumerable<Relation> HistoricalRelations =>
        PhysicalTables(HistoricalUuidTables, "uuid")
            .Append(new Relation(TextTenantTable, "r", "text"))
            .Concat(PhysicalTables(ParentDerivedTables, null))
            .Concat(PhysicalTables(GlobalSharedTables, null))
            .Concat(PhysicalTables(InternalTables, null))
            .Concat(PhysicalTables(IdentityRootTables, null));

    private static IEnumerable<Relation> PhysicalTables(IEnumerable<string> names, string? tenantType) =>
        names.Select(name => new Relation(name, "r", tenantType));

    internal static async Task WithHistoricalDatabaseAsync(string root, Func<string, Task> scenario)
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(root, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, HistoricalMigration);
            await scenario(database);
        });
    }

    internal static async Task<Snapshot> AssertMigratedAsync(string database)
    {
        var migrations = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" COLLATE "C"
            """, reader => reader.GetString(0));
        Assert.Contains(HistoricalMigration, migrations);
        var version = HistoricalVersion;
        var expected = HistoricalRelations;
        if (migrations.Contains(FoundationMigration))
        {
            Assert.Contains(CutoverMigration, migrations);
            Assert.Contains(PrivacyMigration, migrations);
            version = FoundationVersion;
            expected = expected.Where(relation => !FormsArchives.Any(archive => archive.LegacyName == relation.Name))
                .Concat(PhysicalTables(FoundationTables.Select(table => table.Name), "uuid"))
                .Concat(PhysicalTables(FormsArchives.Select(archive => archive.ArchiveName), "uuid"))
                .Concat(FormsArchives.Select(archive => new Relation(archive.LegacyName, "v", "uuid")));
        }
        else
        {
            Assert.DoesNotContain(CutoverMigration, migrations);
            Assert.DoesNotContain(PrivacyMigration, migrations);
        }
        if (migrations.Contains(FeedbackMigration))
        {
            Assert.Equal(FoundationVersion, version);
            Assert.Contains(FieldCompatibilityMigration, migrations);
            version = FeedbackVersion;
            expected = expected.Concat(PhysicalTables(FeedbackTables.Select(table => table.Name), "uuid"));
        }
        else Assert.DoesNotContain(FieldCompatibilityMigration, migrations);

        var actual = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT c.relname, c.relkind::text, format_type(a.atttypid, a.atttypmod)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
            ORDER BY c.relname COLLATE "C"
            """, reader => new Relation(reader.GetString(0), reader.GetString(1), NullableString(reader, 2)));
        Assert.Equal(expected.OrderBy(relation => relation.Name, StringComparer.Ordinal), actual);
        return new Snapshot(version, migrations[^1], actual);
    }

    internal static IEnumerable<Relation> TenantColumnTables(Snapshot catalogue) =>
        catalogue.Relations.Where(relation => relation.Kind == "r" && relation.TenantColumnType is not null);

    // Keep draft fixture policy mechanics on historical relations that remain physical and active.
    internal static IReadOnlyList<string> ExistingActiveDraftPolicyTables(Snapshot catalogue) =>
        TenantColumnTables(catalogue)
            .Where(relation => HistoricalUuidTables.Contains(relation.Name) || relation.Name == TextTenantTable)
            .Select(relation => relation.Name)
            .Concat(ParentDerivedTables)
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal sealed record AqSource(AqTable Declaration, SecurityArchitectureRlsSchemaIdentity.Snapshot NativeIdentity,
        string Classification, string PolicyOperationQualification);

    internal static async Task<IReadOnlyList<AqSource>> AssertCurrentAqSourcesAsync(string database, Snapshot catalogue)
    {
        var hasFeedback = catalogue.Version == FeedbackVersion;
        var tables = hasFeedback ? FoundationTables.Concat(FeedbackTables) : FoundationTables;
        var sources = new List<AqSource>();
        foreach (var table in tables)
        {
            var tenantParents = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                SELECT DISTINCT parent.relname COLLATE "C" FROM pg_constraint k
                JOIN pg_class child ON child.oid = k.conrelid
                JOIN pg_namespace child_schema ON child_schema.oid = child.relnamespace
                JOIN pg_class parent ON parent.oid = k.confrelid
                JOIN pg_namespace parent_schema ON parent_schema.oid = parent.relnamespace
                WHERE k.contype = 'f' AND k.convalidated AND child_schema.nspname = 'public'
                  AND parent_schema.nspname = 'public' AND child.relname = @table
                  AND EXISTS (
                    SELECT 1 FROM unnest(k.conkey, k.confkey) AS keys(child_column, parent_column)
                    JOIN pg_attribute ca ON ca.attrelid = child.oid AND ca.attnum = keys.child_column
                    JOIN pg_attribute pa ON pa.attrelid = parent.oid AND pa.attnum = keys.parent_column
                    WHERE ca.attname = 'TenantId' AND pa.attname = 'TenantId')
                ORDER BY 1
                """, reader => reader.GetString(0), ("table", table.Name));
            var expected = hasFeedback && table.Name == "form_field_definitions"
                ? table with { TenantParents = [table.Name, .. table.TenantParents] }
                : table;
            Assert.Equal(expected.TenantParents, tenantParents);
            var native = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table.Name);
            if (table.SourceBlockedOperations.Length != 0)
            {
                var guard = Assert.Single(native.Guards, item => item.FunctionName == "guard_survey_publication_immutability");
                Assert.Equal("O", guard.Enabled);
                Assert.Equal(24, guard.CommandMask & 24); // UPDATE and DELETE.
            }
            sources.Add(new AqSource(expected, native, "TENANT_COLUMN_WITH_PARENT_BINDING", "UNVERIFIED"));
        }

        var names = tables.Select(table => table.Name).Concat(FormsArchives.SelectMany(archive =>
            new[] { archive.ArchiveName, archive.LegacyName })).ToArray();
        var nonOwnerGrants = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN LATERAL aclexplode(c.relacl) acl
            WHERE n.nspname = 'public' AND c.relname = ANY(@tables) AND acl.grantee <> c.relowner
            """, ("tables", names));
        Assert.Equal(0L, nonOwnerGrants);
        var nullableTenantColumns = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = ANY(@tables)
                AND a.attname = 'TenantId' AND NOT a.attisdropped AND NOT a.attnotnull
            """, ("tables", tables.Select(table => table.Name).ToArray()));
        Assert.Equal(0L, nullableTenantColumns);
        return sources;
    }

    internal static async Task<IReadOnlyDictionary<string, List<Guid>>> CapturePreCutoverFormIdsAsync(string database, Guid tenant)
    {
        var identities = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var archive in FormsArchives)
        {
            var ids = await TenantRowIdsAsync(database, archive.LegacyName, tenant);
            Assert.NotEmpty(ids);
            identities.Add(archive.LegacyName, ids);
        }
        return identities;
    }

    internal static async Task AssertFormsCutoverAsync(string database, Guid sourceTenant,
        IReadOnlyDictionary<string, List<Guid>> priorIdentities)
    {
        var question = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, "form_questions");
        Assert.Contains(question.Constraints, constraint =>
            constraint.ConstraintName == "FK_form_questions_form_definitions_FormId" &&
            constraint.ReferencedTable == "form_definitions" && constraint.Validated);
        Assert.Contains(question.Guards, guard =>
            guard.FunctionName == "synchronize_ordinary_question_authoring" && guard.Enabled == "O");
        foreach (var archive in FormsArchives)
        {
            var native = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, archive.ArchiveName);
            var fence = Assert.Single(native.Guards, guard => guard.TriggerName == archive.TriggerName);
            Assert.Equal("reject_fenced_legacy_form_write", fence.FunctionName);
            Assert.Equal("O", fence.Enabled);
            Assert.Equal(62, fence.CommandMask); // BEFORE statement, INSERT/UPDATE/DELETE/TRUNCATE.
            var securityInvoker = await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
                SELECT 'security_invoker=true' = ANY(c.reloptions)
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = @view AND c.relkind = 'v'
                """, ("view", archive.LegacyName));
            Assert.True(securityInvoker);
            var dependencies = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                SELECT DISTINCT used.table_name COLLATE "C" FROM information_schema.view_table_usage used
                WHERE used.view_schema = 'public' AND used.view_name = @view AND used.table_schema = 'public'
                ORDER BY 1
                """, reader => reader.GetString(0), ("view", archive.LegacyName));
            Assert.Equal(archive.ViewSources, dependencies);
            var archived = await TenantRowIdsAsync(database, archive.ArchiveName, sourceTenant);
            Assert.Equal(priorIdentities[archive.LegacyName], archived);
            Assert.Equal(archived, await TenantRowIdsAsync(database, archive.LegacyName, sourceTenant));
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    DELETE FROM public."{archive.ArchiveName}" WHERE "TenantId" = @tenant
                    """, ("tenant", sourceTenant)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
            Assert.Contains("reject_fenced_legacy_form_write()", rejected.Where, StringComparison.Ordinal);
            Assert.Equal(archived, await TenantRowIdsAsync(database, archive.ArchiveName, sourceTenant));
        }
    }

    private static Task<List<Guid>> TenantRowIdsAsync(string database, string relation, Guid tenant) =>
        PostgreSqlMigrationTestDatabase.QueryAsync(database, $"""
            SELECT "Id" FROM public."{relation}" WHERE "TenantId" = @tenant ORDER BY "Id"
            """, reader => reader.GetGuid(0), ("tenant", tenant));

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
