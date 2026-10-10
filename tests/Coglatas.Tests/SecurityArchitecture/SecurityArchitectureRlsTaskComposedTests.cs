using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsComposedHostTests
{
    [Fact]
    public void ActualTaskContextPrototypeRequiresExplicitTestEnvironmentAndIsAbsentWhenDisabled()
    {
        foreach (var environment in new[] { "Production", "Development" })
        {
            var builder = new HostBuilder().UseEnvironment(environment)
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    { ["COGLATAS_SEC_ARCH_RLS_TASK_COMPOSED_PROBE"] = "true" }))
                .ConfigureWebHost(web =>
                {
                    web.UseEnvironment(environment).UseKestrel().Configure(_ => { });
                    new SecurityArchitectureRlsTaskComposedStartup().Configure(web);
                });
            Assert.Throws<InvalidOperationException>(builder.Build);
        }
        using var disabled = new HostBuilder().UseEnvironment("Test").ConfigureWebHost(web =>
        {
            web.UseEnvironment("Test").UseKestrel().Configure(_ => { });
            new SecurityArchitectureRlsTaskComposedStartup().Configure(web);
        }).Build();
        Assert.Null(disabled.Services.GetService<ComposedTaskRlsProbe>());
    }

    [PostgreSqlFact]
    public Task ActualWebCurrentV3TaskRequestInvokesRuntimeWithOwnedTransactionsAndCurrentProjectAuthority() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, betaWorkspace) =>
        {
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAOwnerEmail, password);
            await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                GRANT SELECT ON projects,project_members,groups,task_items,task_item_collaborators,task_workflow_stages,project_execution_scopes,
                    task_execution_scope_overrides,task_execution_runs,task_execution_source_policy_documents,
                    research_plans,research_plan_revisions,integration_accounts,attachments,file_objects,
                    idempotency_records,outbox_events,audit_logs,task_execution_materialized_sources,
                    task_execution_results,task_execution_result_sources TO "{role}";
                GRANT INSERT ON idempotency_records,task_execution_runs,task_execution_source_policy_documents,outbox_events,
                    task_execution_materialized_sources,task_execution_results,task_execution_result_sources TO "{role}";
                GRANT UPDATE ON task_execution_runs TO "{role}";
                GRANT UPDATE ("VersionNo","UpdatedAt") ON task_items TO "{role}";
                """);
            var alphaTask = await TaskIdAsync(alphaTenant);
            var betaTask = await TaskIdAsync(betaTenant);
            await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                foreach (var (tenantId, workspaceId, taskId, actor) in new[]
                         { (alphaTenant, alphaWorkspace, alphaTask, SecurityCiFixtureSeed.TenantAOwnerUserId),
                           (betaTenant, betaWorkspace, betaTask, SecurityCiFixtureSeed.TenantBOwnerUserId) })
                {
                    var project = await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database,
                        "SELECT \"ProjectId\" FROM task_items WHERE \"Id\"=@task", ("task", taskId));
                    var current = await seed.ProjectExecutionScopes.SingleAsync(scope => scope.ProjectId == project);
                    Assert.Equal(tenantId, current.TenantId);
                    Assert.Equal(workspaceId, current.WorkspaceId);
                    current.ProjectFilesEnabled = true;
                    current.WebEnabled = false;
                    current.UpdatedByUserId = actor;
                    current.VersionNo++;
                }
                await seed.SaveChangesAsync();
            }
            foreach (var table in new[] { "task_execution_materialized_sources", "task_execution_results", "task_execution_result_sources", "audit_logs" })
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY; ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY sec_arch_draft_composed_task ON public."{table}" TO "{role}"
                        USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                        WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                    """);
            var observations = new List<JsonElement>();
            var foreignPositive = await IssueAsync(beta, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId, betaTask, HttpStatusCode.Created);
            var foreignState = await TaskStateAsync(betaTask);
            var foreignStateDigest = await TaskStateDigestAsync(betaTask);
            Assert.Equal((1L, 1L, 1L, 1L, 4L, 1L, 1L), foreignState);
            Assert.Equal("Succeeded", await LatestStatusAsync(betaTask));
            observations.Add(foreignPositive);
            observations.Add(await IssueAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, alphaTask, HttpStatusCode.Created));
            Assert.Equal((1L, 1L, 1L, 1L, 4L, 1L, 1L), await TaskStateAsync(alphaTask));
            Assert.Equal("Succeeded", await LatestStatusAsync(alphaTask));
            observations.Add(await IssueAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, betaTask, HttpStatusCode.NotFound));
            Assert.Equal(foreignState, await TaskStateAsync(betaTask));
            Assert.Equal(foreignStateDigest, await TaskStateDigestAsync(betaTask));

            await SetProjectRoleAsync("Viewer");
            observations.Add(await IssueAsync(beta, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId, betaTask, HttpStatusCode.NotFound));
            Assert.Equal(foreignState, await TaskStateAsync(betaTask));
            Assert.Equal(foreignStateDigest, await TaskStateDigestAsync(betaTask));
            await SetProjectRoleAsync("Owner");
            observations.Add(await IssueAsync(beta, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId, betaTask, HttpStatusCode.Created));
            Assert.Equal((2L, 2L, 2L, 2L, 8L, 2L, 2L), await TaskStateAsync(betaTask));
            Assert.Equal("Succeeded", await LatestStatusAsync(betaTask));
            await WritePrivateAsync("draft-rls-composed-web-task-v3-runtime.json", database, role, new
            {
                observations, webAssemblyDigest = host.WebAssemblyDigest,
                observedCurrentSnapshotSchemaVersion = 3, observedRuntimeContractVersion = 1,
                selectedDraftPolicyCount = 4, actualRuntime = "DurableTaskExecutionResultRuntime",
                actualRequestAcceptance = "TaskExecutionScopeService", actualIdempotency = "EfCreateIdempotencyCoordinator",
                actualSourcePolicyDocumentFlush = "TaskExecutionScopeRepository", actualStorage = "LocalFileStorageService", actualAudit = "DbAuditLogger",
                sourcePolicyDocumentsCreated = 3, persistedResultCount = 3, persistedProvenanceCount = 3, persistedReferenceCount = 3,
                persistedRequestAndRuntimeAuditCount = 12, persistedTaskChangedOutboxCount = 3,
                foreignPositiveStateDigest = foreignStateDigest, crossTenantAndCurrentRoleDenialStateUnchanged = true,
                restoredStateDigest = await TaskStateDigestAsync(betaTask),
                actualAuthenticatedControllerInvocation = "OBSERVED", ambientTransactionApplied = false,
                crossTenantDeniedStatus = 404, currentProjectRoleDenialStatus = 404,
                fullTableContextCompatibility = "UNVERIFIED", identityPolicyAuthority = "UNVERIFIED",
                sourcePolicyAndIdempotencyRlsQualification = "UNVERIFIED", eventDelivery = "UNVERIFIED"
            });

            Task<Guid> TaskIdAsync(Guid tenantId) => PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database,
                "SELECT \"Id\" FROM task_items WHERE \"TenantId\"=@tenant ORDER BY \"Id\" LIMIT 1", ("tenant", tenantId));
            Task SetProjectRoleAsync(string projectRole) => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                UPDATE project_members SET "Role"=@role WHERE "TenantId"=@tenant AND "UserId"=@subject
                """, ("role", projectRole), ("tenant", betaTenant), ("subject", SecurityCiFixtureSeed.TenantBOwnerUserId));
            Task<string> LatestStatusAsync(Guid task) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                "SELECT \"Status\" FROM task_execution_runs WHERE \"TaskItemId\"=@task ORDER BY \"RequestedAtUtc\" DESC,\"Id\" DESC LIMIT 1", ("task", task));
            async Task<(long Runs, long Sources, long Results, long References, long Audits, long Documents, long Outbox)> TaskStateAsync(Guid task)
            {
                var states = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                    SELECT (SELECT count(*) FROM task_execution_runs WHERE "TaskItemId"=@task),
                        (SELECT count(*) FROM task_execution_materialized_sources WHERE "TaskItemId"=@task),
                        (SELECT count(*) FROM task_execution_results WHERE "TaskItemId"=@task),
                        (SELECT count(*) FROM task_execution_result_sources s JOIN task_execution_results r ON r."Id"=s."TaskExecutionResultId" WHERE r."TaskItemId"=@task),
                        (SELECT count(*) FROM audit_logs a JOIN task_execution_runs r ON r."Id"=a."EntityId" WHERE r."TaskItemId"=@task AND a."EntityType"='TaskExecutionRun'),
                        (SELECT count(*) FROM task_execution_source_policy_documents WHERE "OwnerType"='Run' AND "TaskItemId"=@task),
                        (SELECT count(*) FROM outbox_events WHERE "AggregateId"=@task AND "EventType"='Projects.TaskChanged.v1')
                    """, reader => (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                        reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6)), ("task", task));
                return Assert.Single(states);
            }
            async Task<string> TaskStateDigestAsync(Guid task)
            {
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var (table, condition) in new[]
                         {
                             ("task_execution_runs", "\"TaskItemId\"=@task"),
                             ("task_execution_materialized_sources", "\"TaskItemId\"=@task"),
                             ("task_execution_results", "\"TaskItemId\"=@task"),
                             ("task_execution_result_sources", "\"TaskExecutionResultId\" IN (SELECT \"Id\" FROM task_execution_results WHERE \"TaskItemId\"=@task)"),
                             ("audit_logs", "\"EntityId\" IN (SELECT \"Id\" FROM task_execution_runs WHERE \"TaskItemId\"=@task) AND \"EntityType\"='TaskExecutionRun'"),
                             ("task_execution_source_policy_documents", "\"OwnerType\"='Run' AND \"TaskItemId\"=@task"),
                             ("outbox_events", "\"AggregateId\"=@task AND \"EventType\"='Projects.TaskChanged.v1'")
                         })
                {
                    var rows = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                        $"SELECT COALESCE(string_agg(row_to_json(observed)::text,E'\\n' ORDER BY row_to_json(observed)::text COLLATE \"C\"),'') FROM (SELECT * FROM public.\"{table}\" WHERE {condition}) observed", ("task", task));
                    digest.AppendData(Encoding.UTF8.GetBytes(table + "\n" + rows + "\n"));
                }
                return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            }
            async Task<JsonElement> IssueAsync(HttpClient client, Guid tenantId, Guid subject, Guid task, HttpStatusCode expected)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Post, "/api/tasks/" + task + "/execution-runs", capture);
                request.Headers.Add("Idempotency-Key", "sec-arch-task-" + Guid.NewGuid().ToString("N"));
                request.Content = JsonContent.Create(new { });
                using var response = await client.SendAsync(request);
                Assert.True(host.HasCapture(capture), "Actual selected Task request did not retain a post-auth capture; HTTP=" + (int)response.StatusCode);
                var receipt = await host.ReceiptAsync(capture);
                var privateDirectory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(privateDirectory))
                {
                    Directory.CreateDirectory(privateDirectory);
                    await using var archived = new FileStream(Path.Combine(privateDirectory, "task-request-" + capture.ToString("N") + ".json"),
                        FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                    await JsonSerializer.SerializeAsync(archived, receipt);
                }
                Assert.True(expected == response.StatusCode, "Actual Task HTTP mismatch; expected=" + (int)expected + "; actual=" + (int)response.StatusCode +
                    "; exceptionType=" + receipt.GetProperty("exceptionType").GetString() +
                    "; nativeSqlState=" + receipt.GetProperty("nativeSqlState").GetString() +
                    "; permissionRejectedTable=" + receipt.GetProperty("permissionRejectedTable").GetString());
                if (expected == HttpStatusCode.Created)
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.Equal("Succeeded", body.RootElement.GetProperty("status").GetString());
                }
                Assert.Equal(tenantId.ToString(), receipt.GetProperty("tenantId").GetString());
                Assert.Equal(subject.ToString(), receipt.GetProperty("subjectId").GetString());
                Assert.Equal(role, receipt.GetProperty("databaseRole").GetString());
                Assert.True(receipt.GetProperty("backendPid").GetInt32() > 0);
                Assert.True(receipt.GetProperty("outsideTransactionContextEmpty").GetBoolean());
                Assert.False(receipt.GetProperty("ambientTransactionApplied").GetBoolean());
                Assert.Equal("DRAFT", receipt.GetProperty("approval").GetString());
                var transactions = receipt.GetProperty("boundTransactionCount").GetInt32();
                Assert.Equal(expected == HttpStatusCode.Created ? 4 : 0, transactions);
                await AssertSessionIdentityAsync(database, receipt, subject);
                return receipt;
            }
        }, taskRuntimeProbe: true);
}
