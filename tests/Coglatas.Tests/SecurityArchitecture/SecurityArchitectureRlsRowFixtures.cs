using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>
/// Source-model backed synthetic rows. These are PostgreSQL policy probes, not
/// approved resource, field, capability or worker authorization contracts.
/// </summary>
internal static class SecurityArchitectureRlsRowFixtures
{
    internal sealed record RowSql(string Sql, IReadOnlyList<(string Name, object Value, string Type)> Parameters);
    internal sealed record Seed(Guid Tenant, Dictionary<string, object> Entities, RowSql? Insert = null);

    public static async Task<Seed> SeedAsync(string database, Guid tenant, string? insertTarget = null)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var types = db.Model.GetEntityTypes().Where(type => type.GetTableName() is not null).ToArray();
        var rows = types.ToDictionary(type => type, Create);
        foreach (var (type, row) in rows)
        {
            foreach (var property in type.GetProperties())
            {
                if (property.PropertyInfo?.SetMethod is null || property.GetComputedColumnSql() is not null) continue;
                if (property.Name == "TenantId") Set(property, row, tenant);
                else if (property.IsNullable) Set(property, row, null);
                else if (property.ClrType == typeof(string))
                {
                    var value = property.GetColumnType() == "jsonb" ? "{}" : "synthetic-" + tenant.ToString("N");
                    if (property.GetMaxLength() is { } max) value = value[..Math.Min(max, value.Length)];
                    Set(property, row, value);
                }
                else if (property.ClrType == typeof(Guid) && (Guid)property.PropertyInfo.GetValue(row)! == Guid.Empty)
                    Set(property, row, Guid.NewGuid());
                else if (property.ClrType == typeof(DateTimeOffset)) Set(property, row, DateTimeOffset.UtcNow);
                else if (property.ClrType == typeof(DateOnly)) Set(property, row, new DateOnly(2026, 10, 10));
            }
        }
        ((Tenant)rows[types.Single(type => type.ClrType == typeof(Tenant))]).Id = tenant;
        var assigned = new HashSet<IEntityType>();
        foreach (var type in types) AssignForeignKeys(type, rows, assigned);
        var byTable = rows.ToDictionary(pair => pair.Key.GetTableName()!, pair => pair.Value);
        ((Tenant)byTable["tenants"]).Id = tenant;
        // TenantId is also part of composite foreign keys. Resolve it only after the root identity is fixed.
        foreach (var (type, row) in rows)
            if (type.FindProperty("TenantId") is { } property) Set(property, row, tenant);
        ConfigureSourceInvariants(byTable, tenant);
        var runType = types.Single(type => type.ClrType == typeof(SecurityEvaluationRun));
        var ruleType = types.Single(type => type.ClrType == typeof(SecurityEvaluationRuleRecord));
        var context = SourceRevisionContext.Committed(new(new(new(tenant), new(((Project)byTable["projects"]).Id), BranchId.New()), RevisionId.New()));
        var document = SourceDocument.Create(DocumentId.New(), "project", SourceJson.Parse("""
            {"entityId":"00000000-0000-4000-8000-000000000006","kind":"coglatas.project","schemaVersion":1,"payload":{}}
            """));
        var source = ProjectSource.Create(context, [document]);
        var binding = SecurityBinding.Create(new(Guid.NewGuid(), new(new(tenant), ((User)byTable["users"]).Id),
            new("projectide.analyze"), new(context, source.Digest), SecurityEnforcementMode.Shadow, source, null, null), new(source, null, null));
        var run = SecurityEvaluationRun.CreatePending(binding, DateTimeOffset.UtcNow);
        var rule = SecurityEvaluationRuleRecord.Create(run, 0,
            new("synthetic.rule", SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.RuleNotExecuted));
        rows[runType] = byTable["security_evaluation_runs"] = run;
        rows[ruleType] = byTable["security_evaluation_rule_results"] = rule;

        // Save parents before children so database trigger dependencies observe actual persisted rows.
        var target = insertTarget is null ? null : types.SingleOrDefault(type => type.GetTableName() == insertTarget);
        var pending = target is null ? types.ToList() : Ancestors(target).Where(type => type != target).ToList();
        var saved = new HashSet<IEntityType>();
        var errors = new List<string>();
        while (pending.Count > 0)
        {
            var ready = pending.Where(type => type.GetForeignKeys().Where(key => key.IsRequired)
                .All(key => key.PrincipalEntityType == type || saved.Contains(key.PrincipalEntityType))).ToArray();
            if (ready.Length == 0) throw new InvalidOperationException("Synthetic required-parent cycle requires an explicit fixture.");
            foreach (var type in ready)
            {
                var row = rows[type];
                try { await InsertAsync(database, type, row); }
                catch (PostgresException error)
                {
                    errors.Add(type.GetTableName() + ": " + error.SqlState + " / " + error.ConstraintName);
                }
                saved.Add(type);
                pending.Remove(type);
            }
        }
        if (errors.Count > 0) throw new InvalidOperationException("Synthetic source fixture failures: " + string.Join("; ", errors));
        var insert = target is null ? await SeedSqlAdaptersAsync(database, byTable, tenant, insertTarget) : InsertCommand(target, rows[target]);
        return new Seed(tenant, byTable, insert);
    }

    private static object Create(IEntityType type) => type.ClrType == typeof(OutboxEvent)
        ? new OutboxEvent(Guid.NewGuid())
        : Activator.CreateInstance(type.ClrType, nonPublic: true)
            ?? throw new InvalidOperationException("A source fixture constructor is required for " + type.Name);

    private static void Set(IProperty property, object row, object? value) => property.PropertyInfo!.SetValue(row, value);

    private static void AssignForeignKeys(IEntityType type, Dictionary<IEntityType, object> rows, HashSet<IEntityType> assigned)
    {
        if (!assigned.Add(type)) return;
        foreach (var foreignKey in type.GetForeignKeys().Where(key => key.IsRequired))
        {
            AssignForeignKeys(foreignKey.PrincipalEntityType, rows, assigned);
            var principal = rows[foreignKey.PrincipalEntityType];
            for (var i = 0; i < foreignKey.Properties.Count; i++)
                Set(foreignKey.Properties[i], rows[type], foreignKey.PrincipalKey.Properties[i].PropertyInfo!.GetValue(principal));
        }
    }

    private static IReadOnlySet<IEntityType> Ancestors(IEntityType target)
    {
        var result = new HashSet<IEntityType>();
        void Add(IEntityType type)
        {
            if (!result.Add(type)) return;
            foreach (var key in type.GetForeignKeys().Where(key => key.IsRequired)) Add(key.PrincipalEntityType);
        }
        Add(target);
        return result;
    }

    private static async Task InsertAsync(string database, IEntityType type, object row)
    {
        var insert = InsertCommand(type, row);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(insert.Sql, connection);
        foreach (var (name, value, storeType) in insert.Parameters)
        {
            var parameter = new NpgsqlParameter(name, value);
            if (storeType == "jsonb") parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync();
    }

    private static RowSql InsertCommand(IEntityType type, object row)
    {
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        var properties = type.GetProperties().Where(property => property.GetComputedColumnSql() is null).ToArray();
        var sql = "INSERT INTO public.\"" + table.Name + "\" (" +
            string.Join(",", properties.Select(property => "\"" + property.GetColumnName(table) + "\"")) + ") VALUES (" +
            string.Join(",", properties.Select((_, i) => "@p" + i)) + ")";
        var parameters = new List<(string Name, object Value, string Type)>();
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var value = property.PropertyInfo?.GetValue(row) ?? property.FieldInfo?.GetValue(row);
            var converter = property.GetTypeMapping().Converter;
            if (converter is not null) value = converter.ConvertToProvider(value);
            parameters.Add(("p" + i, value ?? DBNull.Value, property.GetColumnType() ?? ""));
        }
        return new RowSql(sql, parameters);
    }

    private static void ConfigureSourceInvariants(Dictionary<string, object> rows, Guid tenant)
    {
        // Keep entity constructor defaults and set only invariants established by the current source.
        ((CapabilityGrant)rows["capability_grants"]).ScopeId = tenant;
        ((CapabilityGrant)rows["capability_grants"]).ScopeType = CapabilityScopeType.Tenant;
        ((OutboxEvent)rows["outbox_events"]).PayloadSchemaVersion = 1;
        ((TaskExecutionRun)rows["task_execution_runs"]).SnapshotProjectFilesEnabled = true;
        ((ArtifactClaim)rows["artifact_claims"]).Ordinal = 1;
        ((ArtifactEvidence)rows["artifact_evidence"]).Ordinal = 1;
        ((ArtifactReportSection)rows["artifact_report_sections"]).Ordinal = 1;
        ((ArtifactReportCitation)rows["artifact_report_citations"]).Ordinal = 1;
        ((ArtifactReportCitation)rows["artifact_report_citations"]).AnchorLengthUtf16 = 1;
        ((ResearchPlanStep)rows["research_plan_steps"]).SortOrder = 1;
        ((TaskDeadlineDigestJob)rows["task_deadline_digest_jobs"]).NextAttemptAt = DateTimeOffset.UtcNow;
        ((TaskDeadlineDigestJob)rows["task_deadline_digest_jobs"]).PolicyVersion = 1;
        ((TaskDeadlineDigestAttempt)rows["task_deadline_digest_attempts"]).AttemptNumber = 1;
        var workspace = ((Workspace)rows["workspaces"]).Id;
        var project = ((Project)rows["projects"]).Id;
        var task = ((TaskItem)rows["task_items"]).Id;
        foreach (var row in rows.Values)
        {
            // Several persisted duplicated scope fields deliberately have no standalone FK.
            foreach (var (name, value) in new[] { ("WorkspaceId", workspace), ("ProjectId", project), ("TaskItemId", task) })
            {
                var property = row.GetType().GetProperty(name);
                if (property?.PropertyType == typeof(Guid) && property.SetMethod is not null) property.SetValue(row, value);
            }
        }
    }

    private static async Task<RowSql?> SeedSqlAdaptersAsync(string database, Dictionary<string, object> rows, Guid tenant, string? insertTarget)
    {
        var workspace = ((Workspace)rows["workspaces"]).Id;
        var project = ((Project)rows["projects"]).Id;
        var task = ((TaskItem)rows["task_items"]).Id;
        var run = ((TaskExecutionRun)rows["task_execution_runs"]).Id;
        var file = ((FileObject)rows["file_objects"]).Id;
        var attachment = ((Attachment)rows["attachments"]).Id;
        var announcement = ((Announcement)rows["announcements"]).Id;
        // SQL-only adapters use their current migration lifecycle, scope guards and append-only rules.
        var statements = """
            INSERT INTO task_workflow_templates ("Id","TenantId","Name") VALUES (@template,@tenant,'Synthetic');
            INSERT INTO announcement_engagement_events ("TenantId","AnnouncementId","RecipientToken","Action")
                VALUES (@tenant,@announcement,repeat('a',64),'read');
            INSERT INTO task_workflow_template_stages ("Id","TenantId","TemplateId","Name","InternalCategory","SortKey")
                VALUES (@stage,@tenant,@template,'Synthetic','Todo',1);
            INSERT INTO workspace_task_workflow_defaults ("TenantId","WorkspaceId","TemplateId") VALUES (@tenant,@workspace,@template);
            INSERT INTO tenant_task_workflow_defaults ("TenantId","TemplateId") VALUES (@tenant,@template);
            INSERT INTO task_execution_source_policy_documents ("OwnerType","OwnerId","TenantId","WorkspaceId","ProjectId",
                "PolicySchemaVersion","ProjectScopeVersion","PolicyJson") VALUES ('Project',@project,@tenant,@workspace,@project,2,1,'{}');
            UPDATE task_execution_runs SET "Status"='Queued',"QueuedAtUtc"=now() WHERE "Id"=@run;
            UPDATE task_execution_runs SET "Status"='Running',"StartedAtUtc"=now() WHERE "Id"=@run;
            UPDATE file_objects SET "WorkspaceId"=@workspace,"ProjectId"=@project WHERE "Id"=@file;
            UPDATE attachments SET "WorkspaceId"=@workspace,"FileObjectId"=@file,"OwnerType"='TaskItem',"OwnerId"=@task,"ScanStatus"='Clean'
                WHERE "Id"=@attachment;
            INSERT INTO task_execution_materialized_sources ("Id","TenantId","WorkspaceId","ProjectId","TaskItemId","TaskExecutionRunId",
                "FileObjectId","AttachmentId","ContentSha256","MediaType","MaterializedByteCount","MaterializedAtUtc")
                VALUES (@source,@tenant,@workspace,@project,@task,@run,@file,@attachment,repeat('a',64),'text/plain',0,now());
            INSERT INTO task_execution_results ("Id","TenantId","WorkspaceId","ProjectId","TaskItemId","TaskExecutionRunId",
                "Status","Title","BodyMarkdown","ContentSha256","CompletedAtUtc","CreatedAtUtc")
                VALUES (@result,@tenant,@workspace,@project,@task,@run,'Succeeded','Synthetic','Synthetic',repeat('a',64),now(),now());
            INSERT INTO task_execution_result_sources ("Id","TenantId","TaskExecutionResultId","MaterializedSourceId","Ordinal")
                VALUES (@reference,@tenant,@result,@source,1);
            INSERT INTO coglatas_ui_canonical_revision_heads ("ScopeKey","TenantId","WorkspaceId","Revision")
                VALUES (@scope,@textTenant,'synthetic',0);
            UPDATE coglatas_ui_canonical_revision_heads SET "Revision"=1 WHERE "ScopeKey"=@scope;
            INSERT INTO coglatas_ui_canonical_change_journal ("ScopeKey","Revision","ChangedDomains") VALUES (@scope,1,1);
            """;
        var parameters = new (string Name, object? Value)[] { ("tenant", tenant), ("workspace", workspace), ("project", project), ("task", task), ("run", run),
            ("file", file), ("attachment", attachment), ("announcement", announcement), ("template", Guid.NewGuid()), ("stage", Guid.NewGuid()),
            ("source", Guid.NewGuid()), ("result", Guid.NewGuid()), ("reference", Guid.NewGuid()),
            ("scope", tenant.ToString("N")), ("textTenant", tenant.ToString()) };
        foreach (var statement in statements.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (insertTarget is not null && statement.StartsWith("INSERT INTO " + insertTarget + " ", StringComparison.Ordinal))
                return new(statement, parameters.Select(parameter => (parameter.Name, parameter.Value ?? DBNull.Value, "")).ToArray());
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, statement, parameters);
        }
        if (insertTarget is not null) throw new InvalidOperationException("An explicit SQL source adapter fixture is required for " + insertTarget);
        return null;
    }
}
