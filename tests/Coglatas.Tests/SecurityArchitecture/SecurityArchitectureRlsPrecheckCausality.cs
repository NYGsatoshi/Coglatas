using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsOperationTests
{
    private sealed record PrecheckStage(string Phase, string PolicyDigest, string SourceSchemaDigest, Observation Observation);
    private sealed record PrecheckControl(string Table, string TenantIdentityKind, string Operation, string Situation, string DatabaseRole,
        int SameScopePositiveAffectedRows, IReadOnlyList<string> ParentPolicyTables, string GuardFunction,
        string SourceSchemaDigest, string BaselinePolicyDigest, string RestoredPolicyDigest,
        string BeforeRowsDigest, string AfterRowsDigest, IReadOnlyList<PrecheckStage> Stages,
        string CausalOutcome, string RlsQualification);

    private static async Task<PrecheckControl> VerifyPrecheckAsync(string database, string application, string role,
        TenantTable table, IReadOnlyList<TenantTable> tables, SecurityArchitectureRlsGuardedProbes.Probe probe,
        SecurityArchitectureRlsSchemaIdentity.Snapshot schema, string? tenant, string operation, string sql,
        IReadOnlyList<(string Name, object Value, string Type)> parameters, int positive)
    {
        Assert.True(positive > 0, "The exact operation requires a prior legitimate same-scope positive.");
        Assert.Equal(operation, probe.Operation);
        var parents = probe.ParentTables.Where(parent => parent != table.Table).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var selected = parents.Append(table.Table).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => tables.Single(item => item.Table == name)).ToArray();
        var baselinePolicy = await PrecheckPolicyDigestAsync(database);
        var beforeRows = await PrecheckRowsDigestAsync(database, selected);
        var stages = new List<PrecheckStage>();
        string? restoredPolicy = null;
        string? afterRows = null;
        Exception? failure = null;
        try
        {
            foreach (var phase in new[] { "baseline", "childPermissive", "parentsPermissive", "bothPermissive", "restoredBaseline" })
            {
                foreach (var target in selected)
                {
                    var permissive = target.Table == table.Table
                        ? phase is "childPermissive" or "bothPermissive"
                        : phase is "parentsPermissive" or "bothPermissive";
                    await SetPrecheckPolicyAsync(database, target, permissive);
                }
                var policyDigest = await PrecheckPolicyDigestAsync(database);
                var native = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table.Table);
                Assert.Equal(schema.SchemaDigest, native.SchemaDigest);
                var observed = await ObserveAsync(application, tenant, operation, sql, parameters);
                var stage = new PrecheckStage(phase, policyDigest, native.SchemaDigest, observed);
                stages.Add(stage);
                Assert.Equal(role, stage.Observation.DatabaseRole);
                Assert.Equal(schema.SchemaDigest, stage.SourceSchemaDigest);
                if (phase is "baseline" or "restoredBaseline" || phase == "parentsPermissive" && parents.Length == 0)
                {
                    Assert.Equal(baselinePolicy, stage.PolicyDigest);
                }
                else
                {
                    Assert.NotEqual(baselinePolicy, stage.PolicyDigest);
                }
                var expected = operation == "INSERT" ? phase switch
                {
                    "parentsPermissive" => table.TenantIdentityKind == "PARENT" ? "ALLOWED" : "RLS_WITH_CHECK",
                    "bothPermissive" => "ALLOWED", _ => "TRIGGER_REJECTION"
                } : "TRIGGER_REJECTION";
                Assert.Equal(expected, stage.Observation.Mechanism);
                if (expected == "ALLOWED")
                {
                    Assert.True(stage.Observation.AffectedRows > 0);
                    Assert.Null(stage.Observation.SqlState);
                    Assert.Null(stage.Observation.SourceRejectionIdentity);
                }
                else
                {
                    Assert.Equal(0, stage.Observation.AffectedRows);
                    if (expected == "RLS_WITH_CHECK")
                    {
                        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, stage.Observation.SqlState);
                        Assert.Equal("ExecWithCheckOptions", stage.Observation.NativeRoutine);
                        Assert.Equal(table.Table, stage.Observation.NativeRelation);
                        Assert.Contains(stage.Observation.NativeRelationSource, new[] { "ErrorField", "BoundedPolicyDiagnostic" });
                        Assert.Null(stage.Observation.SourceRejectionIdentity);
                    }
                    else
                    {
                        Assert.Equal(probe.Guard.FunctionName, stage.Observation.SourceRejectionIdentity!.GuardFunctionName);
                    }
                }
                Assert.Equal(beforeRows, await PrecheckRowsDigestAsync(database, selected));
            }
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            var restorationErrors = new List<Exception>();
            foreach (var target in selected)
            {
                try
                {
                    await SetPrecheckPolicyAsync(database, target, permissive: false);
                }
                catch (Exception error)
                {
                    restorationErrors.Add(error);
                }
            }
            try
            {
                restoredPolicy = await PrecheckPolicyDigestAsync(database);
                afterRows = await PrecheckRowsDigestAsync(database, selected);
                Assert.Equal(baselinePolicy, restoredPolicy);
                Assert.Equal(beforeRows, afterRows);
                Assert.Equal(schema.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table.Table)).SchemaDigest);
            }
            catch (Exception error)
            {
                restorationErrors.Add(error);
            }
            await WritePrecheckHistoryAsync(table.Table, table.TenantIdentityKind, operation, probe.Situation, role, positive, parents,
                schema.SchemaDigest, baselinePolicy, restoredPolicy, beforeRows, afterRows, stages,
                failure?.GetType().Name, restorationErrors.Select(error => error.GetType().Name).ToArray());
            if (restorationErrors.Count > 0)
            {
                if (failure is not null)
                {
                    restorationErrors.Insert(0, failure);
                }
                throw new AggregateException("The disposable precheck policies or rows were not restored.", restorationErrors);
            }
        }
        var causalOutcome = operation == "INSERT"
            ? table.TenantIdentityKind == "PARENT" ? "PARENT_DERIVED_POLICY_DEPENDENCY_OBSERVED" : "PARENT_VISIBILITY_DEPENDENCY_OBSERVED"
            : "NATIVE_OWNERSHIP_INVARIANT_WITH_PERMISSIVE_POLICIES";
        var control = new PrecheckControl(table.Table, table.TenantIdentityKind, operation, probe.Situation, role, positive, parents,
            probe.Guard.FunctionName, schema.SchemaDigest, baselinePolicy, restoredPolicy!, beforeRows, afterRows!, stages,
            causalOutcome, "UNVERIFIED");
        Assert.Equal(table.Table, control.Table);
        Assert.Equal(table.TenantIdentityKind, control.TenantIdentityKind);
        Assert.Equal(probe.Operation, control.Operation);
        Assert.Equal(probe.Situation, control.Situation);
        Assert.Equal(role, control.DatabaseRole);
        Assert.Equal(positive, control.SameScopePositiveAffectedRows);
        Assert.Equal(parents, control.ParentPolicyTables);
        Assert.Equal(probe.Guard.FunctionName, control.GuardFunction);
        Assert.Equal(schema.SchemaDigest, control.SourceSchemaDigest);
        Assert.Equal(control.BaselinePolicyDigest, control.RestoredPolicyDigest);
        Assert.Equal(control.BeforeRowsDigest, control.AfterRowsDigest);
        Assert.Equal(new[] { "baseline", "childPermissive", "parentsPermissive", "bothPermissive", "restoredBaseline" }, control.Stages.Select(stage => stage.Phase));
        Assert.Equal(causalOutcome, control.CausalOutcome);
        Assert.Equal("UNVERIFIED", control.RlsQualification);
        return control;
    }

    private static Task SetPrecheckPolicyAsync(string database, TenantTable table, bool permissive)
    {
        var predicate = permissive ? "true" : table.Predicate;
        return PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER POLICY sec_arch_draft_rows ON public." +
            Quote(table.Table) + " USING (" + predicate + ") WITH CHECK (" + predicate + ")");
    }

    private static Task<string> PrecheckPolicyDigestAsync(string database) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
        SELECT encode(sha256(convert_to(string_agg(c.relname||':'||c.relrowsecurity||':'||c.relforcerowsecurity||':'||
            coalesce(c.relacl::text,'')||':'||to_jsonb(p)::text,'|' ORDER BY c.relname COLLATE "C",p.polname COLLATE "C"),'UTF8')),'hex')
        FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='public'
        """);

    private static async Task<string> PrecheckRowsDigestAsync(string database, IReadOnlyList<TenantTable> tables)
    {
        var identities = new List<string>();
        foreach (var table in tables)
        {
            var identity = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                "SELECT count(*)::text||':'||encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,'|' ORDER BY to_jsonb(r)::text COLLATE \"C\"),''),'UTF8')),'hex') FROM public." + Quote(table.Table) + " r");
            identities.Add(table.Table + ":" + identity);
        }
        return Digest(string.Join('\n', identities));
    }

    private static async Task WritePrecheckHistoryAsync(string table, string tenantIdentityKind, string operation, string situation, string role, int positive,
        IReadOnlyList<string> parents, string nativeDigest, string baselinePolicy, string? restoredPolicy,
        string beforeRows, string? afterRows, IReadOnlyList<PrecheckStage> stages, string? failureKind, IReadOnlyList<string> restorationErrors)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsOperationTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-precheck-" + table + "-" + operation.ToLowerInvariant() + "-" + situation + ".json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, candidateSha = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA"), testAssemblyDigest = assemblyDigest,
            table, tenantIdentityKind, operation, situation, databaseRole = role, sameScopePositiveAffectedRows = positive,
            parentPolicyTables = parents, nativeSourceDigest = nativeDigest, baselinePolicyDigest = baselinePolicy,
            restoredPolicyDigest = restoredPolicy, beforeRowsDigest = beforeRows, afterRowsDigest = afterRows,
            stages, failureKind, restorationErrors, baselineRlsQualification = "UNVERIFIED", approval = "DRAFT", ownerApproval = (string?)null,
            productRlsAppliedCount = 0, operationalRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
