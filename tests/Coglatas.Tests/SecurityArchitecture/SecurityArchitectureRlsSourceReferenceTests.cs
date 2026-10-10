using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Independent freshly migrated native source inputs; no operation receipt supplies expected identities.</summary>
public sealed class SecurityArchitectureRlsSourceReferenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    [PostgreSqlFact]
    public async Task FreshMigratedCatalogueIndependentlyBindsEveryNativeSourceAndUnavailableDirectOperation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            var names = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('r','p') ORDER BY c.relname COLLATE "C"
                """, reader => reader.GetString(0));
            Assert.Equal(114, names.Count);
            Assert.Equal(0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('r','p') AND (c.relrowsecurity OR c.relforcerowsecurity)
                """));
            var tables = new List<object>();
            var blockedDirectOperations = 0;
            var sourceGuardedProbeCount = 0;
            var sourceOperationDispositionCount = 0;
            foreach (var name in names)
            {
                var native = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, name);
                AssertNativeIdentity(native, name);
                var unavailable = await SecurityArchitectureRlsUnavailableOperations.BindAsync(name, native);
                foreach (var disposition in unavailable)
                {
                    Assert.Contains(disposition.Operation, new[] { "UPDATE", "DELETE" });
                    Assert.Equal(SecurityArchitectureRlsUnavailableOperations.Reason(name, disposition.Operation), disposition.ReasonCode);
                    Assert.Equal("SOURCE_BLOCKED_DIRECT_MUTATION", disposition.Classification);
                    Assert.NotEmpty(disposition.Sources);
                    foreach (var source in disposition.Sources)
                    {
                        Assert.Matches("^src/Coglatas.Infrastructure/Persistence/Migrations/[0-9]{14}_[A-Za-z0-9_]+\\.cs$", source.Path);
                        Assert.Matches("^[a-f0-9]{64}$", source.Digest);
                    }
                    Assert.True((disposition.Guard is null) != (disposition.Constraint is null));
                    if (disposition.Guard is not null) Assert.Contains(disposition.Guard, native.Guards);
                    if (disposition.Constraint is not null) Assert.Contains(disposition.Constraint, native.Constraints);
                    if (disposition.AlternateLifecycle is not null) Assert.Matches("^[A-Za-z][A-Za-z0-9]+$", disposition.AlternateLifecycle);
                }
                blockedDirectOperations += unavailable.Count;
                var guarded = await SecurityArchitectureRlsGuardedProbes.BindAsync(name, native);
                sourceGuardedProbeCount += guarded.Count;
                var operationDispositions = SecurityArchitectureRlsOperationDispositions.Bind(native, unavailable, guarded);
                sourceOperationDispositionCount += operationDispositions.Count;
                tables.Add(new { table = name, sourceSchemaIdentity = native, sourceUnavailableOperations = unavailable,
                    sourceGuardedProbes = guarded, sourceOperationDispositions = operationDispositions });
            }
            Assert.Equal(19, blockedDirectOperations);
            Assert.Equal(33, sourceGuardedProbeCount);
            Assert.Equal(136, sourceOperationDispositionCount);
            var environment = await EnvironmentAsync(database);
            await WritePrivateAsync("draft-rls-source-reference.json", new
            {
                schemaVersion = 1, candidateSha = Candidate(), testAssemblyDigest = await AssemblyDigestAsync(),
                approval = "DRAFT", ownerApproval = (string?)null, environment,
                environmentFingerprint = Digest(JsonSerializer.Serialize(environment)),
                executionScope = "ISOLATED_MIGRATED_SCHEMA_REFERENCE", productRlsAppliedCount = 0, tables
            });
        });
    }

    [PostgreSqlFact]
    public async Task ActualNativeGuardFunctionAndConstraintMutationsInvalidateRetainedSourceIdentity()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            const string journal = "coglatas_ui_canonical_change_journal";
            const string files = "file_objects";
            var journalBaseline = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, journal);
            var fileBaseline = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, files);
            AssertNativeIdentity(journalBaseline, journal);
            AssertNativeIdentity(fileBaseline, files);
            var journalGuard = Assert.Single(journalBaseline.Guards, guard => guard.FunctionName == "coglatas_ui_reject_journal_rewrite");
            var fileConstraint = Assert.Single(fileBaseline.Constraints, constraint => constraint.ConstraintName == "FK_file_versions_file_objects_FileObjectId");
            var originalFunction = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
                SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname='public' AND p.proname='coglatas_ui_reject_journal_rewrite'
                """);
            var originalConstraint = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
                SELECT pg_get_constraintdef(k.oid) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid
                WHERE c.relname='file_versions' AND k.conname='FK_file_versions_file_objects_FileObjectId'
                """);
            var controls = new List<object>();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE public." + Quote(journal) + " DISABLE TRIGGER " + Quote(journalGuard.TriggerName));
            try
            {
                var disabled = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, journal);
                Assert.NotEqual(journalBaseline.SchemaDigest, disabled.SchemaDigest);
                Assert.Equal("D", Assert.Single(disabled.Guards, guard => guard.TriggerName == journalGuard.TriggerName).Enabled);
                controls.Add(new { mechanism = "DISABLED_NATIVE_TRIGGER", baseline = journalBaseline, mutated = disabled });
            }
            finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE public." + Quote(journal) + " ENABLE TRIGGER " + Quote(journalGuard.TriggerName)); }
            Assert.Equal(journalBaseline.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, journal)).SchemaDigest);

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                CREATE OR REPLACE FUNCTION public.coglatas_ui_reject_journal_rewrite() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END; $$
                """);
            try
            {
                var weakened = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, journal);
                var weakenedGuard = Assert.Single(weakened.Guards, guard => guard.TriggerName == journalGuard.TriggerName);
                Assert.Equal(journalGuard.TriggerDefinitionDigest, weakenedGuard.TriggerDefinitionDigest);
                Assert.Equal(journalGuard.FunctionName, weakenedGuard.FunctionName);
                Assert.NotEqual(journalGuard.FunctionDefinitionDigest, weakenedGuard.FunctionDefinitionDigest);
                Assert.NotEqual(journalBaseline.SchemaDigest, weakened.SchemaDigest);
                controls.Add(new { mechanism = "SAME_NAME_WEAKENED_NATIVE_FUNCTION", baseline = journalBaseline, mutated = weakened });
            }
            finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, originalFunction); }
            Assert.Equal(journalBaseline.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, journal)).SchemaDigest);

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE public.file_versions DROP CONSTRAINT " + Quote(fileConstraint.ConstraintName));
            try
            {
                var missing = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, files);
                Assert.DoesNotContain(missing.Constraints, constraint => constraint.ConstraintName == fileConstraint.ConstraintName);
                Assert.NotEqual(fileBaseline.SchemaDigest, missing.SchemaDigest);
                controls.Add(new { mechanism = "MISSING_NATIVE_REFERENCING_CONSTRAINT", baseline = fileBaseline, mutated = missing });
            }
            finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE public.file_versions ADD CONSTRAINT " + Quote(fileConstraint.ConstraintName) + " " + originalConstraint); }
            Assert.Equal(fileBaseline.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, files)).SchemaDigest);
            Assert.Equal(3, controls.Count);
            await WritePrivateAsync("draft-rls-native-source-mutations.json", new
            {
                schemaVersion = 1, candidateSha = Candidate(), testAssemblyDigest = await AssemblyDigestAsync(), approval = "DRAFT",
                ownerApproval = (string?)null, executionScope = "ISOLATED_NATIVE_SOURCE_MUTATIONS", controls,
                productRlsAppliedCount = 0, preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
            });
        });
    }

    private static void AssertNativeIdentity(SecurityArchitectureRlsSchemaIdentity.Snapshot native, string table)
    {
        Assert.Equal(table, native.Table);
        Assert.Matches("^[a-f0-9]{64}$", native.SchemaDigest);
        Assert.Equal(native.Guards.Count, native.Guards.Select(guard => guard.TriggerName).Distinct().Count());
        foreach (var guard in native.Guards)
        {
            Assert.All(new[] { guard.TriggerName, guard.FunctionSchema, guard.FunctionName }, name => Assert.Matches("^[A-Za-z_][A-Za-z0-9_]{0,62}$", name));
            Assert.Contains(guard.Enabled, new[] { "O", "D", "R", "A" });
            Assert.InRange(guard.CommandMask, 1, 127);
            Assert.Equal(string.Empty, guard.IdentityArguments);
            Assert.All(new[] { guard.TriggerDefinitionDigest, guard.FunctionDefinitionDigest }, digest => Assert.Matches("^[a-f0-9]{64}$", digest));
        }
        foreach (var constraint in native.Constraints)
        {
            Assert.Matches("^[A-Za-z_][A-Za-z0-9_~]{0,62}$", constraint.ConstraintName);
            Assert.Matches("^[A-Za-z_][A-Za-z0-9_]{0,62}$", constraint.ConstraintTable);
            Assert.Contains(constraint.Kind, new[] { "c", "f", "p", "u", "x", "t", "n" });
            if (!constraint.Validated) Assert.Contains(constraint.Kind, new[] { "c", "f", "n" });
            Assert.True(constraint.Deferrable || !constraint.Deferred);
            if (constraint.Relationship == "OWNED") Assert.Equal(table, constraint.ConstraintTable);
            else
            {
                Assert.Equal("REFERENCING", constraint.Relationship);
                Assert.Equal("f", constraint.Kind);
                Assert.Equal(table, constraint.ReferencedTable);
            }
            Assert.Matches("^[a-f0-9]{64}$", constraint.DefinitionDigest);
        }
    }

    private static async Task<object> EnvironmentAsync(string database) => new
    {
        dotnetVersion = Environment.Version.ToString(), npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version!.ToString(),
        postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SHOW server_version"), fixture = "isolated-migrated-postgresql"
    };

    private static string? Candidate()
    {
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        return candidate is not null && Regex.IsMatch(candidate, "^[a-f0-9]{40}$") ? candidate : null;
    }

    private static async Task<string> AssemblyDigestAsync()
    {
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsSourceReferenceTests).Assembly.Location);
        return Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Quote(string identifier)
    {
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]{0,62}$", identifier);
        return "\"" + identifier + "\"";
    }

    private static async Task WritePrivateAsync(string name, object receipt)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, receipt, JsonOptions);
    }
}
