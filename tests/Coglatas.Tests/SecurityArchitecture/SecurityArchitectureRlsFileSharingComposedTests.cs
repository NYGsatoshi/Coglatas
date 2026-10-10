using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsComposedHostTests
{
    [PostgreSqlFact]
    public Task ActualWebFileSharingRechecksCurrentManagementAndExpectedVersionWithoutEffects() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, betaWorkspace) =>
        {
            await GrantComposedFileOperationsAsync(database, role);
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAOwnerEmail, password);
            await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            var authority = await InstallComposedSharingPoliciesAsync(database, role);
            var observations = new List<object>();
            var file = await UploadAsync(alpha, alphaWorkspace, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId);
            var foreign = await UploadAsync(beta, betaWorkspace, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId);
            var versionDigest = await ComposedVersionDigestAsync(database, file);
            var sharing = await GetAsync(file);
            sharing = await MutateAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(true, sharing.SharingVersion), 0);
            Assert.Equal("Workspace", sharing.SharingPolicy);
            sharing = await MutateAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(false, sharing.SharingVersion), 0);
            Assert.Equal("Private", sharing.SharingPolicy);
            sharing = await GrantAsync(file, sharing.SharingVersion);
            var originalGrant = Assert.Single(sharing.Recipients).GrantId;
            sharing = await RevokeAsync(file, originalGrant, sharing.SharingVersion);
            Assert.Empty(sharing.Recipients);
            sharing = await GrantAsync(file, sharing.SharingVersion);
            var grant = Assert.Single(sharing.Recipients).GrantId;
            Assert.NotEqual(originalGrant, grant);

            // Each denied method has already executed its legitimate operation above.
            await DeniedAsync(HttpMethod.Get, foreign, null, "CROSS_TENANT_SHARING_READ");
            await DeniedAsync(HttpMethod.Put, foreign, new FileSharingPolicyUpdateRequest(true, 1), "CROSS_TENANT_POLICY_CHANGE");
            await DeniedAsync(HttpMethod.Post, foreign, new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, 1), "CROSS_TENANT_RECIPIENT_GRANT");
            await DeniedAsync(HttpMethod.Delete, foreign, null, "CROSS_TENANT_RECIPIENT_REVOKE", grant, 1);
            await DeniedAsync(HttpMethod.Post, file, new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantBOwnerUserId, sharing.SharingVersion), "FOREIGN_RECIPIENT_ELIGIBILITY");
            await DeniedAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(true, sharing.SharingVersion - 1), "STALE_POLICY_VERSION", code: "FILE_SHARING_STALE");
            await DeniedAsync(HttpMethod.Post, file, new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, sharing.SharingVersion - 1), "STALE_GRANT_VERSION", code: "FILE_SHARING_STALE");
            await DeniedAsync(HttpMethod.Delete, file, null, "STALE_REVOKE_VERSION", grant, sharing.SharingVersion - 1, "FILE_SHARING_STALE");

            await SetManagerRoleAsync("ReadOnly");
            try
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = SharingRequest(HttpMethod.Get, file, capture);
                using var response = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var redacted = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
                Assert.NotNull(redacted);
                Assert.Equal(file, redacted.FileObjectId);
                Assert.False(redacted.CanManageSharing);
                Assert.False(redacted.CanInspectSharing);
                Assert.Null(redacted.ExternalRecipientCount);
                Assert.Empty(redacted.Recipients);
                Assert.Empty(redacted.AvailableRecipients);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true);
                await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                Assert.Equal(before, await ComposedSharingSnapshotAsync(database, host));
                observations.Add(new { scenario = "CURRENT_UPLOADER_READ_REMAINS_ALLOWED_WITHOUT_MANAGER_DISCLOSURE", status = 200,
                    actionReceipt = receipt, state = before, sourceProjectionRedacted = true });
                await DeniedAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(true, sharing.SharingVersion), "CURRENT_MANAGER_ROLE_POLICY_DENIAL");
                await DeniedAsync(HttpMethod.Post, file, new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, sharing.SharingVersion), "CURRENT_MANAGER_ROLE_GRANT_DENIAL");
                await DeniedAsync(HttpMethod.Delete, file, null, "CURRENT_MANAGER_ROLE_REVOKE_DENIAL", grant, sharing.SharingVersion);
            }
            finally
            {
                await SetManagerRoleAsync("Owner");
            }
            sharing = await GetAsync(file);

            await SetSharingMembershipAsync(database, alphaWorkspace, SecurityCiFixtureSeed.TenantAOwnerUserId, false);
            try
            {
                await DeniedAsync(HttpMethod.Get, file, null, "CURRENT_MANAGER_MEMBERSHIP_READ_DENIAL");
                await DeniedAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(true, sharing.SharingVersion), "CURRENT_MANAGER_MEMBERSHIP_POLICY_DENIAL");
                await DeniedAsync(HttpMethod.Post, file, new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, sharing.SharingVersion), "CURRENT_MANAGER_MEMBERSHIP_GRANT_DENIAL");
                await DeniedAsync(HttpMethod.Delete, file, null, "CURRENT_MANAGER_MEMBERSHIP_REVOKE_DENIAL", grant, sharing.SharingVersion);
            }
            finally
            {
                await SetSharingMembershipAsync(database, alphaWorkspace, SecurityCiFixtureSeed.TenantAOwnerUserId, true);
            }
            sharing = await GetAsync(file);
            sharing = await MutateAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(true, sharing.SharingVersion), 0);
            sharing = await MutateAsync(HttpMethod.Put, file, new FileSharingPolicyUpdateRequest(false, sharing.SharingVersion), 0);
            sharing = await RevokeAsync(file, grant, sharing.SharingVersion);
            sharing = await GrantAsync(file, sharing.SharingVersion);
            Assert.Single(sharing.Recipients);
            Assert.Equal(authority, await ComposedSharingAuthorityAsync(database, role));
            await WritePrivateAsync("draft-rls-composed-web-file-sharing-management.json", database, role, new
            {
                observations, authority, host.WebAssemblyDigest, selectedPolicyCount = 6,
                legitimateHttpMethods = new[] { "GET", "PUT", "POST", "DELETE" },
                sourceCurrentAuthority = "WORKSPACE_MANAGEMENT_AND_EXPECTED_SHARING_VERSION",
                nativeVersionDigest = versionDigest, storageBytesDigest = FileBytesDigest(),
                applicationDenialsHaveRlsCredit = false, fullApplicationAtomicity = "UNVERIFIED",
                authenticationAuditAuthority = "UNVERIFIED_OWNER_REVIEW_REQUIRED"
            });

            async Task<Guid> UploadAsync(HttpClient client, Guid workspace, Guid tenant, Guid actor)
            {
                var capture = Guid.NewGuid();
                using var request = FileUploadRequest(workspace, capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var id = body.RootElement.GetProperty("fileObjectId").GetGuid();
                await AssertComposedNativeFileAsync(database, host, id, tenant, actor);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, tenant, actor, committed: true, efSaveCount: 1);
                await AssertSessionIdentityAsync(database, receipt, actor);
                observations.Add(new { scenario = "AUTHORIZED_UPLOAD_BEFORE_SHARING_CONTROLS", status = 200, actionReceipt = receipt,
                    nativeVersionDigest = await ComposedVersionDigestAsync(database, id), storageBytesDigest = FileBytesDigest() });
                return id;
            }

            async Task SetManagerRoleAsync(string workspaceRole)
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                    "UPDATE workspace_members SET \"Role\"=@role WHERE \"WorkspaceId\"=@workspace AND \"UserId\"=@user",
                    ("role", workspaceRole), ("workspace", alphaWorkspace), ("user", SecurityCiFixtureSeed.TenantAOwnerUserId));
                Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
                    SELECT EXISTS(SELECT 1 FROM workspace_members WHERE "WorkspaceId"=@workspace AND "UserId"=@user AND "Role"=@role AND "Status"='Active')
                    """, ("workspace", alphaWorkspace), ("user", SecurityCiFixtureSeed.TenantAOwnerUserId), ("role", workspaceRole)));
            }

            async Task<FileSharingResponse> GetAsync(Guid id)
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = SharingRequest(HttpMethod.Get, id, capture);
                using var response = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
                Assert.NotNull(result);
                Assert.Equal(id, result.FileObjectId);
                Assert.True(result.CanManageSharing);
                Assert.True(result.CanInspectSharing);
                Assert.Contains(result.AvailableRecipients, candidate => candidate.UserId == SecurityCiFixtureSeed.TenantARestrictedUserId);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true);
                await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                Assert.Equal(before, await ComposedSharingSnapshotAsync(database, host));
                observations.Add(new { scenario = "AUTHORIZED_AND_RESTORED_SHARING_INSPECTION", status = 200, actionReceipt = receipt, state = before });
                return result;
            }

            Task<FileSharingResponse> GrantAsync(Guid id, long version) => MutateAsync(HttpMethod.Post, id,
                new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, version), 1);
            Task<FileSharingResponse> RevokeAsync(Guid id, Guid grantId, long version) =>
                MutateAsync(HttpMethod.Delete, id, null, 0, grantId, version);

            async Task<FileSharingResponse> MutateAsync(HttpMethod method, Guid id, object? payload, int grantDelta, Guid? grantId = null, long? version = null)
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = SharingRequest(method, id, capture, payload, grantId, version);
                using var response = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
                Assert.NotNull(result);
                Assert.Equal(id, result.FileObjectId);
                Assert.True(result.CanManageSharing);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true, efSaveCount: 1);
                await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                var after = await ComposedSharingSnapshotAsync(database, host);
                AssertSharingMutation(before, after, grantDelta);
                await AssertNativeSharingStateAsync(database, id, result, SecurityCiFixtureSeed.TenantAOwnerUserId, method, grantId);
                Assert.Equal(versionDigest, await ComposedVersionDigestAsync(database, file));
                observations.Add(new { scenario = "AUTHORIZED_AND_RESTORED_SHARING_MUTATION", method = method.Method, status = 200,
                    actionReceipt = receipt, before, after, sharingVersion = result.SharingVersion });
                return result;
            }

            async Task DeniedAsync(HttpMethod method, Guid id, object? payload, string scenario,
                Guid? grantId = null, long? version = null, string code = "FILE_NOT_FOUND")
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = SharingRequest(method, id, capture, payload, grantId, version);
                using var response = await alpha.SendAsync(request);
                var denial = await AssertComposedFileDeniedAsync(response, code, id, foreign, grant, SecurityCiFixtureSeed.TenantARestrictedUserId);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true);
                await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                Assert.Equal(before, await ComposedSharingSnapshotAsync(database, host));
                Assert.Equal(authority, await ComposedSharingAuthorityAsync(database, role));
                observations.Add(new { scenario, method = method.Method, status = 400, denial, actionReceipt = receipt,
                    state = before, nativeRlsDenialCredit = false, protectedRowsAndStorageUnchanged = true });
            }
        }, fileProbe: true);

    [PostgreSqlFact]
    public Task ActualWebFileSharingRechecksCurrentRecipientAndRecordedWorkspaceOrProjectBoundary() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, _, alphaWorkspace, _) =>
        {
            await GrantComposedFileOperationsAsync(database, role);
            using var manager = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var recipient = await host.ClientAsync("restricted", SecurityCiFixtureSeed.TenantASlug);
            await host.LoginAsync(manager, SecurityCiFixtureSeed.TenantAOwnerEmail, password);
            await host.LoginAsync(recipient, SecurityCiFixtureSeed.TenantARestrictedEmail, password);
            var authority = await InstallComposedSharingPoliciesAsync(database, role);
            var observations = new List<object>();
            var uploadCapture = Guid.NewGuid();
            Guid file;
            using (var request = FileUploadRequest(alphaWorkspace, uploadCapture))
            using (var response = await manager.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                file = body.RootElement.GetProperty("fileObjectId").GetGuid();
            }
            await AssertComposedNativeFileAsync(database, host, file, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId);
            var uploadReceipt = await host.ReceiptAsync(uploadCapture);
            AssertReceipt(uploadReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true, efSaveCount: 1);
            await AssertSessionIdentityAsync(database, uploadReceipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
            observations.Add(new { scenario = "AUTHORIZED_UPLOAD_BEFORE_CURRENT_RECIPIENT_CONTROLS", status = 200, actionReceipt = uploadReceipt });
            var versionDigest = await ComposedVersionDigestAsync(database, file);
            var sharing = await GrantAsync(1, 1, "WorkspaceMember");
            var originalGrant = Assert.Single(sharing.Recipients).GrantId;
            await RecipientReadAsync(true, "CURRENT_WORKSPACE_RECIPIENT_POSITIVE");

            sharing = await MutateAsync(HttpMethod.Delete, null, originalGrant, sharing.SharingVersion, 0);
            Assert.Empty(sharing.Recipients);
            await RecipientReadAsync(false, "CURRENT_RECIPIENT_GRANT_REVOCATION");
            sharing = await GrantAsync(sharing.SharingVersion, 1, "WorkspaceMember");
            var currentGrant = Assert.Single(sharing.Recipients).GrantId;
            Assert.NotEqual(originalGrant, currentGrant);
            await RecipientReadAsync(true, "RESTORED_WORKSPACE_RECIPIENT_POSITIVE");

            await SetSharingMembershipAsync(database, alphaWorkspace, SecurityCiFixtureSeed.TenantARestrictedUserId, false);
            try
            {
                // The recorded WorkspaceMember grant cannot silently become an external grant.
                await RecipientReadAsync(false, "CURRENT_WORKSPACE_MEMBERSHIP_MAKES_RECORDED_INTERNAL_GRANT_INEFFECTIVE");
                var beforeTransition = await ComposedSharingSnapshotAsync(database, host);
                sharing = await GrantAsync(sharing.SharingVersion, 0, "ExternalProjectMember");
                Assert.Equal(currentGrant, Assert.Single(sharing.Recipients).GrantId);
                Assert.NotEqual(beforeTransition.GrantDigest, (await ComposedSharingSnapshotAsync(database, host)).GrantDigest);
                await RecipientReadAsync(true, "EXPLICIT_CURRENT_EXTERNAL_PROJECT_RECIPIENT_POSITIVE");

                var projects = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                    SELECT p."Id",p."Status" FROM projects p JOIN project_members m ON m."ProjectId"=p."Id"
                    WHERE p."TenantId"=@tenant AND p."WorkspaceId"=@workspace AND m."UserId"=@user
                        AND p."DeletedAt" IS NULL AND p."Status" NOT IN ('Archived','Deleted') ORDER BY p."Id"
                    """, reader => (Id: reader.GetGuid(0), Status: reader.GetString(1)), ("tenant", alphaTenant),
                    ("workspace", alphaWorkspace), ("user", SecurityCiFixtureSeed.TenantARestrictedUserId));
                Assert.NotEmpty(projects);
                try
                {
                    foreach (var project in projects)
                    {
                        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                            "UPDATE projects SET \"Status\"='Archived' WHERE \"Id\"=@id", ("id", project.Id));
                    }
                    await RecipientReadAsync(false, "CURRENT_EXTERNAL_PROJECT_BOUNDARY_REVOCATION");
                    var before = await ComposedSharingSnapshotAsync(database, host);
                    var capture = Guid.NewGuid();
                    using var request = SharingRequest(HttpMethod.Post, file, capture,
                        new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, sharing.SharingVersion));
                    using var response = await manager.SendAsync(request);
                    var denial = await AssertComposedFileDeniedAsync(response, "FILE_NOT_FOUND", file, currentGrant, SecurityCiFixtureSeed.TenantARestrictedUserId);
                    var receipt = await host.ReceiptAsync(capture);
                    AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true);
                    await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                    Assert.Equal(before, await ComposedSharingSnapshotAsync(database, host));
                    observations.Add(new { scenario = "CURRENT_RECIPIENT_INELIGIBLE_FOR_NEW_ADMISSION", method = "POST", status = 400,
                        denial, actionReceipt = receipt, state = before, revokedProjectBoundaryCount = projects.Count, nativeRlsDenialCredit = false });
                }
                finally
                {
                    foreach (var project in projects)
                    {
                        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                            "UPDATE projects SET \"Status\"=@status WHERE \"Id\"=@id", ("status", project.Status), ("id", project.Id));
                    }
                }
                await RecipientReadAsync(true, "RESTORED_EXTERNAL_PROJECT_RECIPIENT_POSITIVE");
            }
            finally
            {
                await SetSharingMembershipAsync(database, alphaWorkspace, SecurityCiFixtureSeed.TenantARestrictedUserId, true);
            }
            // Active Workspace membership invalidates the still-recorded external kind until explicit reconciliation.
            await RecipientReadAsync(false, "CURRENT_WORKSPACE_MEMBERSHIP_MAKES_RECORDED_EXTERNAL_GRANT_INEFFECTIVE");
            sharing = await GrantAsync(sharing.SharingVersion, 0, "WorkspaceMember");
            Assert.Equal(currentGrant, Assert.Single(sharing.Recipients).GrantId);
            await RecipientReadAsync(true, "RESTORED_AND_EXPLICITLY_RECORDED_WORKSPACE_RECIPIENT_POSITIVE");
            Assert.Equal(versionDigest, await ComposedVersionDigestAsync(database, file));
            Assert.Equal(authority, await ComposedSharingAuthorityAsync(database, role));
            await WritePrivateAsync("draft-rls-composed-web-file-sharing-recipient.json", database, role, new
            {
                observations, authority, host.WebAssemblyDigest, selectedPolicyCount = 6,
                nativeVersionDigest = versionDigest, storageBytesDigest = FileBytesDigest(),
                sourceCurrentAuthority = "CURRENT_EFFECTIVE_GRANT_RECORDED_RECIPIENT_KIND_AND_MEMBERSHIP",
                applicationDenialsHaveRlsCredit = false, fullApplicationAtomicity = "UNVERIFIED",
                authenticationAuditAuthority = "UNVERIFIED_OWNER_REVIEW_REQUIRED"
            });

            async Task<FileSharingResponse> GrantAsync(long version, int delta, string kind)
            {
                var result = await MutateAsync(HttpMethod.Post,
                    new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantARestrictedUserId, version), null, null, delta);
                Assert.Equal(kind, Assert.Single(result.Recipients).AccessKind);
                return result;
            }

            async Task<FileSharingResponse> MutateAsync(HttpMethod method, object? payload, Guid? grantId, long? version, int grantDelta)
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = SharingRequest(method, file, capture, payload, grantId, version);
                using var response = await manager.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
                Assert.NotNull(result);
                Assert.Equal(file, result.FileObjectId);
                Assert.True(result.CanManageSharing);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAOwnerUserId, committed: true, efSaveCount: 1);
                await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantAOwnerUserId);
                var after = await ComposedSharingSnapshotAsync(database, host);
                AssertSharingMutation(before, after, grantDelta);
                await AssertNativeSharingStateAsync(database, file, result, SecurityCiFixtureSeed.TenantAOwnerUserId, method, grantId);
                Assert.Equal(versionDigest, await ComposedVersionDigestAsync(database, file));
                observations.Add(new { scenario = "AUTHORIZED_AND_RESTORED_CURRENT_RECIPIENT_MUTATION", method = method.Method, status = 200,
                    actionReceipt = receipt, before, after, sharingVersion = result.SharingVersion });
                return result;
            }

            async Task RecipientReadAsync(bool permitted, string scenario)
            {
                var before = await ComposedSharingSnapshotAsync(database, host);
                foreach (var path in new[] { $"/api/files/{file:D}/versions/{file:D}/content", $"/api/files/{file:D}/sharing" })
                {
                    var capture = Guid.NewGuid();
                    using var request = Request(HttpMethod.Get, path, capture);
                    using var response = await recipient.SendAsync(request);
                    object? denial = null;
                    if (!permitted)
                    {
                        denial = await AssertComposedFileDeniedAsync(response, "FILE_NOT_FOUND", file, SecurityCiFixtureSeed.TenantARestrictedUserId);
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        if (path.EndsWith("/content", StringComparison.Ordinal))
                        {
                            Assert.Equal(ComposedFileBytes, await response.Content.ReadAsByteArrayAsync());
                            Assert.True(response.Headers.CacheControl?.NoStore);
                        }
                        else
                        {
                            var result = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
                            Assert.NotNull(result);
                            Assert.Equal(file, result.FileObjectId);
                            Assert.False(result.CanManageSharing);
                            Assert.False(result.CanInspectSharing);
                            Assert.Null(result.ExternalRecipientCount);
                            Assert.Empty(result.Recipients);
                            Assert.Empty(result.AvailableRecipients);
                        }
                    }
                    var receipt = await host.ReceiptAsync(capture);
                    AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantARestrictedUserId, committed: true);
                    await AssertSessionIdentityAsync(database, receipt, SecurityCiFixtureSeed.TenantARestrictedUserId);
                    Assert.Equal(before, await ComposedSharingSnapshotAsync(database, host));
                    Assert.Equal(versionDigest, await ComposedVersionDigestAsync(database, file));
                    Assert.Equal(authority, await ComposedSharingAuthorityAsync(database, role));
                    observations.Add(new { scenario, operation = path.EndsWith("/content", StringComparison.Ordinal) ? "VERSION_READ" : "REDACTED_SHARING_READ",
                        status = permitted ? 200 : 400, denial, actionReceipt = receipt, state = before,
                        nativeRlsDenialCredit = false, protectedRowsAndStorageUnchanged = true });
                }
            }
        }, fileProbe: true);

    private static HttpRequestMessage SharingRequest(HttpMethod method, Guid file, Guid capture, object? payload = null,
        Guid? grant = null, long? version = null)
    {
        var path = $"/api/files/{file:D}/sharing";
        if (method == HttpMethod.Post)
        {
            path += "/recipients";
        }
        else if (method == HttpMethod.Delete)
        {
            Assert.NotNull(grant);
            Assert.NotNull(version);
            path += $"/recipients/{grant.Value:D}?expectedSharingVersion={version.Value}";
        }
        var request = Request(method, path, capture);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, payload.GetType());
        }
        return request;
    }

    private static async Task<string> InstallComposedSharingPoliciesAsync(string database, string role)
    {
        await InstallComposedFilePoliciesAsync(database, role);
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
            GRANT INSERT ON file_access_grants TO "{role}";
            GRANT UPDATE ("SharingPolicy","SharingVersion","UpdatedAt") ON file_objects TO "{role}";
            GRANT UPDATE ("RecipientKind","GrantedByUserId","RevokedAt","RevokedByUserId","UpdatedAt") ON file_access_grants TO "{role}";
            ALTER TABLE file_access_grants ENABLE ROW LEVEL SECURITY;
            ALTER TABLE file_access_grants FORCE ROW LEVEL SECURITY;
            CREATE POLICY sec_arch_draft_composed_file_access_grants ON file_access_grants TO "{role}"
                USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
            """);
        Assert.Equal(6L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relrowsecurity AND c.relforcerowsecurity"));
        Assert.Equal(6L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
            "SELECT count(*) FROM pg_policies WHERE schemaname='public' AND policyname LIKE 'sec_arch_draft_composed_%'"));
        var updates = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT c.relname||'.'||a.attname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
            WHERE n.nspname='public' AND c.relname IN ('file_objects','file_versions','attachments','audit_logs','outbox_events','file_access_grants')
                AND has_column_privilege(@role,c.oid,a.attnum,'UPDATE') ORDER BY c.relname COLLATE "C",a.attname COLLATE "C"
            """, reader => reader.GetString(0), ("role", role));
        Assert.Equal(new[] { "file_access_grants.GrantedByUserId", "file_access_grants.RecipientKind", "file_access_grants.RevokedAt",
            "file_access_grants.RevokedByUserId", "file_access_grants.UpdatedAt", "file_objects.SharingPolicy", "file_objects.SharingVersion", "file_objects.UpdatedAt" }, updates);
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'SELECT'))=21
                AND count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'INSERT'))=8
                AND count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'UPDATE'))=2
                AND count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'DELETE'))=0
                AND count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'TRUNCATE'))=0
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p')
            """, ("role", role)));
        await AssertRoleAsync(database, role);
        return await ComposedSharingAuthorityAsync(database, role);
    }

    private static Task<string> ComposedSharingAuthorityAsync(string database, string role) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
            WITH selected AS (SELECT c.oid,c.relname,c.relrowsecurity,c.relforcerowsecurity,c.relacl
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
                AND c.relname IN ('file_objects','file_versions','attachments','audit_logs','outbox_events','file_access_grants')),
            identities AS (
                SELECT 'table:'||relname AS key,to_jsonb(selected)::text AS value FROM selected
                UNION ALL SELECT 'policy:'||p.tablename||':'||p.policyname,to_jsonb(p)::text FROM pg_policies p JOIN selected s ON s.relname=p.tablename WHERE p.schemaname='public'
                UNION ALL SELECT 'constraint:'||s.relname||':'||c.conname,pg_get_constraintdef(c.oid,true) FROM pg_constraint c JOIN selected s ON s.oid=c.conrelid
                UNION ALL SELECT 'trigger:'||s.relname||':'||t.tgname,pg_get_triggerdef(t.oid,true)||':'||pg_get_functiondef(t.tgfoid) FROM pg_trigger t JOIN selected s ON s.oid=t.tgrelid WHERE NOT t.tgisinternal
                UNION ALL SELECT 'column:'||s.relname||':'||a.attname,coalesce(a.attacl::text,'') FROM pg_attribute a JOIN selected s ON s.oid=a.attrelid WHERE a.attnum>0 AND NOT a.attisdropped
                UNION ALL SELECT 'role',to_jsonb(r)::text FROM pg_roles r WHERE r.rolname=@role)
            SELECT encode(sha256(convert_to(string_agg(key||':'||value,'|' ORDER BY key COLLATE "C"),'UTF8')),'hex') FROM identities
            """, ("role", role));

    private sealed record ComposedSharingSnapshot(ComposedFileSnapshot Files, long GrantCount, string GrantDigest);

    private static async Task<ComposedSharingSnapshot> ComposedSharingSnapshotAsync(string database, SecurityArchitectureRlsComposedHostFixture host)
    {
        var grants = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT count(*),encode(sha256(convert_to(coalesce(string_agg(to_jsonb(g)::text,'|' ORDER BY to_jsonb(g)::text COLLATE "C"),''),'UTF8')),'hex') FROM file_access_grants g
            """, reader => (Count: reader.GetInt64(0), Digest: reader.GetString(1))));
        return new(await ComposedFileSnapshotAsync(database, host), grants.Count, grants.Digest);
    }

    private static void AssertSharingMutation(ComposedSharingSnapshot before, ComposedSharingSnapshot after, int grantDelta)
    {
        Assert.Equal(before.Files.Files, after.Files.Files);
        Assert.Equal(before.Files.Versions, after.Files.Versions);
        Assert.Equal(before.Files.Attachments, after.Files.Attachments);
        Assert.Equal(before.Files.Audits + 1, after.Files.Audits);
        Assert.Equal(before.Files.Events + 1, after.Files.Events);
        Assert.NotEqual(before.Files.RowsDigest, after.Files.RowsDigest);
        Assert.Equal(before.Files.StorageCount, after.Files.StorageCount);
        Assert.Equal(before.Files.StorageDigest, after.Files.StorageDigest);
        Assert.Equal(before.GrantCount + grantDelta, after.GrantCount);
        if (grantDelta != 0)
        {
            Assert.NotEqual(before.GrantDigest, after.GrantDigest);
        }
    }

    private static async Task AssertNativeSharingStateAsync(string database, Guid file, FileSharingResponse response, Guid actor,
        HttpMethod method, Guid? revokedGrant)
    {
        Assert.True(response.SharingVersion > 1);
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT EXISTS (SELECT 1 FROM file_objects f WHERE f."Id"=@file AND f."SharingPolicy"=@policy AND f."SharingVersion"=@version
                AND (SELECT count(*) FROM audit_logs a WHERE a."EntityId"=f."Id" AND a."Action"='FileSharingChanged')=f."SharingVersion"-1
                AND (SELECT count(*) FROM outbox_events e WHERE e."AggregateId"=f."Id" AND e."EventType"='Files.FileChanged.v1')=f."SharingVersion")
            """, ("file", file), ("policy", response.SharingPolicy), ("version", response.SharingVersion)));
        if (method == HttpMethod.Post)
        {
            var grant = Assert.Single(response.Recipients);
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
                SELECT EXISTS(SELECT 1 FROM file_access_grants g JOIN file_objects f ON f."Id"=g."FileObjectId"
                    WHERE g."Id"=@grant AND g."FileObjectId"=@file AND g."TenantId"=f."TenantId" AND g."WorkspaceId"=f."WorkspaceId"
                    AND g."RecipientUserId"=@recipient AND g."RecipientKind"=@kind AND g."GrantedByUserId"=@actor
                    AND g."RevokedAt" IS NULL AND g."RevokedByUserId" IS NULL)
                """, ("grant", grant.GrantId), ("file", file), ("recipient", SecurityCiFixtureSeed.TenantARestrictedUserId),
                ("kind", grant.AccessKind), ("actor", actor)));
        }
        if (method == HttpMethod.Delete)
        {
            Assert.NotNull(revokedGrant);
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
                SELECT EXISTS(SELECT 1 FROM file_access_grants WHERE "Id"=@grant AND "FileObjectId"=@file
                    AND "RevokedAt" IS NOT NULL AND "RevokedByUserId"=@actor AND "UpdatedAt" IS NOT NULL)
                """, ("grant", revokedGrant.Value), ("file", file), ("actor", actor)));
        }
    }

    private static async Task SetSharingMembershipAsync(string database, Guid workspace, Guid user, bool active)
    {
        var status = active ? MembershipStatus.Active.ToString() : MembershipStatus.Suspended.ToString();
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
            "UPDATE workspace_members SET \"Status\"=@status WHERE \"WorkspaceId\"=@workspace AND \"UserId\"=@user",
            ("status", status), ("workspace", workspace), ("user", user));
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT EXISTS(SELECT 1 FROM workspace_members WHERE "WorkspaceId"=@workspace AND "UserId"=@user AND "Status"=@status)
            """, ("workspace", workspace), ("user", user), ("status", status)));
    }
}
