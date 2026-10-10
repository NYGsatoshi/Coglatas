using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsComposedHostTests
{
    [PostgreSqlFact]
    public Task ActualWebEfWorkspaceMutationAndStagedAuditAreAtomicAcrossRlsDenialAndCurrentMembershipChange() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, betaWorkspace) =>
        {
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            await InstallWorkspacePolicyAsync(database, role);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY; ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_draft_composed_workspace_audit ON audit_logs TO "{role}"
                    USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                    WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                """);
            var alphaBefore = await DescriptionAsync(alphaWorkspace);
            var betaAuditBefore = await AuditCountAsync(betaWorkspace, SecurityCiFixtureSeed.TenantBOwnerUserId);
            var alphaAuditBefore = await AuditCountAsync(alphaWorkspace, SecurityCiFixtureSeed.TenantAMemberUserId);
            var committed = "SEC-ARCH isolated committed metadata " + Guid.NewGuid().ToString("N");
            var attempted = "SEC-ARCH isolated rolled-back metadata " + Guid.NewGuid().ToString("N");
            var restored = "SEC-ARCH isolated restored metadata " + Guid.NewGuid().ToString("N");
            var observations = new List<JsonElement>
            {
                await UpdateAsync(beta, betaWorkspace, betaTenant, committed, HttpStatusCode.OK, efSaveCount: 1)
            };
            Assert.Equal(committed, await DescriptionAsync(betaWorkspace));
            Assert.Equal(betaAuditBefore + 1, await AuditCountAsync(betaWorkspace, SecurityCiFixtureSeed.TenantBOwnerUserId));
            observations.Add(await UpdateAsync(beta, betaWorkspace, betaTenant, attempted, HttpStatusCode.InternalServerError,
                efSaveCount: 1, injectException: true));
            await AssertBetaPreservedAsync(committed, betaAuditBefore + 1);

            // This is a real RLS denial after a successful save with the same INSERT grant.
            // The source's existing audit staging semantics are not promoted to a mandatory staging contract.
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                "SELECT has_table_privilege(@role,'audit_logs','INSERT')", ("role", role)));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER POLICY sec_arch_draft_composed_workspace_audit ON audit_logs WITH CHECK (false)");
            observations.Add(await UpdateAsync(beta, betaWorkspace, betaTenant, attempted, HttpStatusCode.InternalServerError,
                efSaveFailureCount: 1, nativeSqlState: "42501"));
            await AssertBetaPreservedAsync(committed, betaAuditBefore + 1);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                ALTER POLICY sec_arch_draft_composed_workspace_audit ON audit_logs
                    WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                """);
            observations.Add(await UpdateAsync(beta, betaWorkspace, betaTenant, restored, HttpStatusCode.OK, efSaveCount: 1));
            await AssertBetaPreservedAsync(restored, betaAuditBefore + 2);

            observations.Add(await UpdateAsync(alpha, alphaWorkspace, alphaTenant, attempted, HttpStatusCode.Forbidden));
            Assert.Equal(alphaBefore, await DescriptionAsync(alphaWorkspace));
            Assert.Equal(alphaAuditBefore, await AuditCountAsync(alphaWorkspace, SecurityCiFixtureSeed.TenantAMemberUserId));
            observations.Add(await UpdateAsync(alpha, betaWorkspace, alphaTenant, attempted, HttpStatusCode.NotFound));
            await AssertBetaPreservedAsync(restored, betaAuditBefore + 2);

            await SetBetaMembershipAsync(WorkspaceRole.Member);
            observations.Add(await UpdateAsync(beta, betaWorkspace, betaTenant, attempted, HttpStatusCode.Forbidden));
            await AssertBetaPreservedAsync(restored, betaAuditBefore + 2);
            await SetBetaMembershipAsync(WorkspaceRole.Owner);
            observations.Add(await UpdateAsync(beta, betaWorkspace, betaTenant, committed, HttpStatusCode.OK, efSaveCount: 1));
            await AssertBetaPreservedAsync(committed, betaAuditBefore + 3);
            Assert.Equal(alphaBefore, await DescriptionAsync(alphaWorkspace));
            Assert.Equal(alphaAuditBefore, await AuditCountAsync(alphaWorkspace, SecurityCiFixtureSeed.TenantAMemberUserId));
            await WritePrivateAsync("draft-rls-composed-web-ef-audit-mutation.json", database, role, new
            {
                observations, webAssemblyDigest = host.WebAssemblyDigest,
                actualEfAdapter = "EfUnitOfWork", actualAuditAdapter = "DbAuditLogger",
                completedEfSaveCount = 4, failedEfSaveCount = 1, persistedWorkspaceUpdatedAuditDelta = 3,
                injectedExceptionRolledBackMetadataAndStagedAudit = true,
                grantedAuditInsertRlsDenialRolledBackMetadataAndStagedAudit = true,
                sameTenantMemberDenialStatus = 403, crossTenantDenialStatus = 404,
                currentMembershipDowngradeDenialStatus = 403, restoredMembershipPositiveStatus = 200,
                workspaceUpdatedMandatoryStagingContract = "UNVERIFIED",
                databaseResourceOwnershipAuthority = "UNVERIFIED", fullTableStartupQualification = "UNVERIFIED"
            });

            Task<string> DescriptionAsync(Guid workspace) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                "SELECT COALESCE(\"Description\",'') FROM workspaces WHERE \"Id\"=@workspace", ("workspace", workspace));
            Task<long> AuditCountAsync(Guid workspace, Guid subject) => PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                SELECT count(*) FROM audit_logs WHERE "EntityType"='Workspace' AND "EntityId"=@workspace
                    AND "Action"='WorkspaceUpdated' AND "ActorUserId"=@subject
                """, ("workspace", workspace), ("subject", subject));
            async Task AssertBetaPreservedAsync(string description, long auditCount)
            {
                Assert.Equal(description, await DescriptionAsync(betaWorkspace));
                Assert.Equal(auditCount, await AuditCountAsync(betaWorkspace, SecurityCiFixtureSeed.TenantBOwnerUserId));
            }
            Task SetBetaMembershipAsync(WorkspaceRole membershipRole) => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                UPDATE workspace_members SET "Role"=@role WHERE "TenantId"=@tenant AND "WorkspaceId"=@workspace AND "UserId"=@subject
                """, ("role", (int)membershipRole), ("tenant", betaTenant), ("workspace", betaWorkspace), ("subject", SecurityCiFixtureSeed.TenantBOwnerUserId));
            async Task<JsonElement> UpdateAsync(HttpClient client, Guid workspace, Guid tenant, string description, HttpStatusCode status,
                int efSaveCount = 0, int efSaveFailureCount = 0, bool injectException = false, string? nativeSqlState = null)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Patch, "/api/workspaces/" + workspace, capture);
                request.Content = JsonContent.Create(new { description });
                if (injectException) request.Headers.Add("X-Sec-Arch-Composed-Exception", "true");
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                var observed = await host.ReceiptAsync(capture);
                var subject = tenant == alphaTenant ? SecurityCiFixtureSeed.TenantAMemberUserId : SecurityCiFixtureSeed.TenantBOwnerUserId;
                AssertReceipt(observed, role, tenant, subject, committed: status != HttpStatusCode.InternalServerError,
                    efSaveCount, efSaveFailureCount, nativeSqlState);
                await AssertSessionIdentityAsync(database, observed, subject);
                return observed;
            }
        }, workspaceMutation: true);
}
