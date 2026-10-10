using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsComposedHostTests
{
    private static readonly byte[] ComposedFileBytes = "Synthetic selected HTTP File/version bytes."u8.ToArray();
    private static readonly string[] ComposedFileTables = ["file_objects", "file_versions", "attachments", "audit_logs", "outbox_events"];

    [PostgreSqlFact]
    public Task ActualWebFileUploadAndNativeVersionReadUseAuthenticatedSelectedTenantContext() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, betaTenant, alphaWorkspace, betaWorkspace) =>
        {
            await GrantComposedFileOperationsAsync(database, role);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "UPDATE workspace_members SET \"Role\"=@role WHERE \"WorkspaceId\"=@workspace AND \"UserId\"=@user",
                ("role", WorkspaceRole.Member.ToString()), ("workspace", alphaWorkspace), ("user", SecurityCiFixtureSeed.TenantARestrictedUserId));
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            using var beta = await host.ClientAsync("beta", SecurityCiFixtureSeed.TenantBSlug);
            using var restricted = await host.ClientAsync("restricted", SecurityCiFixtureSeed.TenantASlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await host.LoginAsync(beta, SecurityCiFixtureSeed.TenantBOwnerEmail, password);
            await host.LoginAsync(restricted, SecurityCiFixtureSeed.TenantARestrictedEmail, password);
            // Authentication audit writes precede the explicitly selected post-auth action context.
            await InstallComposedFilePoliciesAsync(database, role);
            var observations = new List<object>();
            var betaFile = await UploadPositiveAsync(beta, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId, betaWorkspace);
            var alphaFile = await UploadPositiveAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, alphaWorkspace);
            var restrictedFile = await UploadPositiveAsync(restricted, alphaTenant, SecurityCiFixtureSeed.TenantARestrictedUserId, alphaWorkspace);
            await VersionAsync(beta, betaTenant, SecurityCiFixtureSeed.TenantBOwnerUserId, betaFile, HttpStatusCode.OK);
            await VersionAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, alphaFile, HttpStatusCode.OK);
            await VersionAsync(restricted, alphaTenant, SecurityCiFixtureSeed.TenantARestrictedUserId, restrictedFile, HttpStatusCode.OK);
            await ActivityAsync(alpha, alphaFile, HttpStatusCode.OK);

            var beforeDenied = await ComposedFileSnapshotAsync(database, host);
            await UploadDeniedAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, betaWorkspace);
            await VersionAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, betaFile, HttpStatusCode.BadRequest);
            await VersionAsync(restricted, alphaTenant, SecurityCiFixtureSeed.TenantARestrictedUserId, alphaFile, HttpStatusCode.BadRequest);
            Assert.Equal(beforeDenied, await ComposedFileSnapshotAsync(database, host));

            await SetComposedFileMembershipAsync(database, alphaWorkspace, active: false);
            var beforeRevoked = await ComposedFileSnapshotAsync(database, host);
            await UploadDeniedAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, alphaWorkspace);
            await ActivityAsync(alpha, alphaFile, HttpStatusCode.BadRequest);
            await VersionAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, alphaFile, HttpStatusCode.BadRequest);
            Assert.Equal(beforeRevoked, await ComposedFileSnapshotAsync(database, host));
            await SetComposedFileMembershipAsync(database, alphaWorkspace, active: true);
            var restored = await UploadPositiveAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, alphaWorkspace);
            await ActivityAsync(alpha, restored, HttpStatusCode.OK);
            await VersionAsync(alpha, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, restored, HttpStatusCode.OK);

            var beforeMissing = await ComposedFileSnapshotAsync(database, host);
            using (var missingContext = await alpha.GetAsync($"/api/files/{restored}/activity"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, missingContext.StatusCode);
                await AssertComposedFileDeniedAsync(missingContext, "FILE_NOT_FOUND", restored);
            }
            Assert.Equal(beforeMissing, await ComposedFileSnapshotAsync(database, host));
            await WritePrivateAsync("draft-rls-composed-web-file-current-authority.json", database, role, new
            {
                observations, host.WebAssemblyDigest, selectedPolicyCount = ComposedFileTables.Length,
                beforeDenied, beforeRevoked, beforeMissing,
                crossTenantAndSameTenantPrivateReadNoFileEffects = true, currentWorkspaceSuspensionNoFileEffects = true,
                missingSelectedTransactionStatus = 400, missingContextCompatibility = "UNVERIFIED",
                sessionRevocationAuthority = "UNVERIFIED_PRE_AUTHENTICATION_AUDIT_BOUNDARY",
                loginBeforeSelectedPolicies = true, fullStartupAndOperationalRoleAuthority = "UNVERIFIED"
            });

            async Task<Guid> UploadPositiveAsync(HttpClient client, Guid tenant, Guid subject, Guid workspace)
            {
                var before = await ComposedFileSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = FileUploadRequest(workspace, capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var file = body.RootElement.GetProperty("fileObjectId").GetGuid();
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, tenant, subject, committed: true, efSaveCount: 1);
                await AssertSessionIdentityAsync(database, receipt, subject);
                await AssertComposedNativeFileAsync(database, host, file, tenant, subject);
                AssertComposedFileAdded(before, await ComposedFileSnapshotAsync(database, host));
                observations.Add(new { scenario = "AUTHORIZED_UPLOAD", status = 200, actionReceipt = receipt,
                    nativeVersionDigest = await ComposedVersionDigestAsync(database, file), storedBytesDigest = FileBytesDigest() });
                return file;
            }

            async Task UploadDeniedAsync(HttpClient client, Guid tenant, Guid subject, Guid workspace)
            {
                var capture = Guid.NewGuid();
                using var request = FileUploadRequest(workspace, capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var error = await AssertComposedFileDeniedAsync(response, "FileMetadataFailed", workspace, tenant, subject);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, tenant, subject, committed: true);
                observations.Add(new { scenario = "CURRENT_UPLOAD_ADMISSION_DENIAL", status = 400, actionReceipt = receipt,
                    error, rlsDenialCredit = false });
            }

            async Task VersionAsync(HttpClient client, Guid tenant, Guid subject, Guid file, HttpStatusCode expected)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Get, $"/api/files/{file}/versions/{file}/content", capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(expected, response.StatusCode);
                if (expected == HttpStatusCode.OK)
                {
                    Assert.Equal(ComposedFileBytes, await response.Content.ReadAsByteArrayAsync());
                    Assert.True(response.Headers.CacheControl!.NoStore);
                    Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
                }
                else
                {
                    await AssertComposedFileDeniedAsync(response, "FILE_NOT_FOUND", file, tenant, subject);
                }
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, tenant, subject, committed: true);
                observations.Add(new { scenario = "NATIVE_VERSION_READ", status = (int)expected, actionReceipt = receipt,
                    bodyDigest = expected == HttpStatusCode.OK ? FileBytesDigest() : null, rlsDenialCredit = false });
            }

            async Task ActivityAsync(HttpClient client, Guid file, HttpStatusCode expected)
            {
                var capture = Guid.NewGuid();
                using var request = Request(HttpMethod.Get, $"/api/files/{file}/activity", capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(expected, response.StatusCode);
                if (expected == HttpStatusCode.OK)
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var version = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray()).GetProperty("version");
                    Assert.Equal(file, version.GetProperty("versionId").GetGuid());
                    Assert.Equal(1, version.GetProperty("versionNumber").GetInt32());
                }
                else
                {
                    await AssertComposedFileDeniedAsync(response, "FILE_NOT_FOUND", file);
                }
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true);
                observations.Add(new { scenario = "ACTUAL_NATIVE_ACTIVITY_PROJECTION", status = (int)expected, actionReceipt = receipt });
            }
        }, fileProbe: true);

    [PostgreSqlFact]
    public Task RevokedActualWebCookieCannotReachFileActionAndRetainsExactPreAuthAuditRlsCompatibilityHold() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, _, alphaWorkspace, _) =>
        {
            await GrantComposedFileOperationsAsync(database, role);
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await InstallComposedFilePoliciesAsync(database, role);
            var beforePositive = await ComposedFileSnapshotAsync(database, host);
            var positiveCapture = Guid.NewGuid();
            Guid file;
            {
                using var request = FileUploadRequest(alphaWorkspace, positiveCapture);
                using var response = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                file = body.RootElement.GetProperty("fileObjectId").GetGuid();
            }
            var positiveReceipt = await host.ReceiptAsync(positiveCapture);
            AssertReceipt(positiveReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true, efSaveCount: 1);
            await AssertSessionIdentityAsync(database, positiveReceipt, SecurityCiFixtureSeed.TenantAMemberUserId);
            await AssertComposedNativeFileAsync(database, host, file, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId);
            AssertComposedFileAdded(beforePositive, await ComposedFileSnapshotAsync(database, host));
            var readCapture = Guid.NewGuid();
            {
                using var request = Request(HttpMethod.Get, $"/api/files/{file}/versions/{file}/content", readCapture);
                using var response = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(ComposedFileBytes, await response.Content.ReadAsByteArrayAsync());
            }
            var readReceipt = await host.ReceiptAsync(readCapture);
            AssertReceipt(readReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true);
            var session = positiveReceipt.GetProperty("sessionId").GetGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "UPDATE sessions SET \"RevokedAt\"=now() WHERE \"Id\"=@session", ("session", session));
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                "SELECT \"RevokedAt\" IS NOT NULL AND \"UserId\"=@user FROM sessions WHERE \"Id\"=@session",
                ("session", session), ("user", SecurityCiFixtureSeed.TenantAMemberUserId)));
            var beforeFailure = await ComposedFileSnapshotAsync(database, host);
            var failedCapture = Guid.NewGuid();
            {
                using var request = Request(HttpMethod.Get, $"/api/files/{file}/versions/{file}/content", failedCapture);
                using var failed = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            Assert.False(host.HasCapture(failedCapture));
            var preAuth = await host.PreAuthReceiptAsync(failedCapture);
            Assert.Equal(1, preAuth.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("DRAFT", preAuth.GetProperty("approval").GetString());
            Assert.Equal(JsonValueKind.Null, preAuth.GetProperty("ownerApproval").ValueKind);
            Assert.Equal("ACTUAL_WEB_PRE_AUTHENTICATION_AUDIT_COMPATIBILITY_FAILURE", preAuth.GetProperty("executionScope").GetString());
            Assert.Equal(role, preAuth.GetProperty("databaseRole").GetString());
            Assert.True(preAuth.GetProperty("backendPid").GetInt32() > 0);
            Assert.True(string.IsNullOrEmpty(preAuth.GetProperty("tenantContext").GetString()));
            Assert.Equal(1, preAuth.GetProperty("observedTransactionCount").GetInt32());
            Assert.Equal(0, preAuth.GetProperty("selectedActionTransactionCount").GetInt32());
            Assert.Equal(1, preAuth.GetProperty("pendingSessionValidationAuditCount").GetInt32());
            Assert.Equal("42501", preAuth.GetProperty("nativeSqlState").GetString());
            Assert.Equal(JsonValueKind.Null, preAuth.GetProperty("nativeTable").ValueKind);
            Assert.Equal("ExecWithCheckOptions", preAuth.GetProperty("nativeRoutine").GetString());
            Assert.Equal("audit_logs", preAuth.GetProperty("rlsRejectedTable").GetString());
            Assert.Equal("RLS_POLICY", preAuth.GetProperty("denialMechanism").GetString());
            Assert.False(preAuth.GetProperty("authenticatedSessionDenialCredit").GetBoolean());
            Assert.Equal("UNVERIFIED", preAuth.GetProperty("authenticationAuditAuthority").GetString());
            Assert.Equal("UNVERIFIED", preAuth.GetProperty("operationalRoleEquivalence").GetString());
            Assert.Equal("PRE-AVALONIA SEC-ARCH: BLOCKED", preAuth.GetProperty("preAvaloniaVerdict").GetString());
            Assert.Equal(beforeFailure, await ComposedFileSnapshotAsync(database, host));
            await WritePrivateAsync("draft-rls-composed-web-file-pre-auth-audit-hold.json", database, role, new
            {
                positiveReceipt, readReceipt, preAuth, host.WebAssemblyDigest, beforeFailure,
                nativeVersionDigest = await ComposedVersionDigestAsync(database, file), storedBytesDigest = FileBytesDigest(),
                observedStatus = 500, selectedActionCaptureCount = 0, persistedSessionRevoked = true,
                protectedFilesAndStorageUnchanged = true, authenticatedSessionDenialCredit = false,
                authenticationAuditAuthority = "UNVERIFIED_OWNER_REVIEW_REQUIRED", productPolicyOrPrivilegeChanges = 0
            });
        }, fileProbe: true);

    private static async Task<object> AssertComposedFileDeniedAsync(HttpResponseMessage response, string expectedCode, params Guid[] protectedIds)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(new[] { "error", "requestId", "status", "traceId" }, root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("requestId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        var error = root.GetProperty("error");
        Assert.Equal(new[] { "code", "details", "message", "redactionApplied", "target" }, error.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.Equal("The request could not be completed.", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("target").ValueKind);
        Assert.Empty(error.GetProperty("details").EnumerateArray());
        Assert.True(error.GetProperty("redactionApplied").GetBoolean());
        foreach (var id in protectedIds)
        {
            Assert.DoesNotContain(id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(id.ToString("N"), body, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var protectedValue in new[] { "synthetic.txt", "Synthetic selected HTTP File/version bytes.",
                     "fileObjectId", "versionId", "storageKey", "viewPath", "tenantId", "ownerId" })
        {
            Assert.DoesNotContain(protectedValue, body, StringComparison.OrdinalIgnoreCase);
        }
        return new { code = expectedCode, status = 400, message = error.GetProperty("message").GetString(),
            redactionApplied = true, protectedIdentifiersAndContentAbsent = true };
    }

    [PostgreSqlFact]
    public Task ActualWebFilePersistenceFailureCompensatesStorageButOuterRollbackRetainsBlobHold() =>
        WithHostAsync(async (database, host, role, password, alphaTenant, _, alphaWorkspace, _) =>
        {
            await GrantComposedFileOperationsAsync(database, role);
            using var alpha = await host.ClientAsync("alpha", SecurityCiFixtureSeed.TenantASlug);
            await host.LoginAsync(alpha, SecurityCiFixtureSeed.TenantAMemberEmail, password);
            await InstallComposedFilePoliciesAsync(database, role);
            var observations = new List<object>();
            await PositiveAsync(alpha);
            var beforeFailure = await ComposedFileSnapshotAsync(database, host);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER POLICY sec_arch_draft_composed_file_versions ON file_versions WITH CHECK (false)");
            try
            {
                Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                    "SELECT has_table_privilege(@role,'file_versions','INSERT')", ("role", role)));
                var capture = Guid.NewGuid();
                using var request = FileUploadRequest(alphaWorkspace, capture);
                using var failed = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: false,
                    efSaveFailureCount: 1, nativeSqlState: "42501", expectedRlsTable: "file_versions");
                Assert.Equal(beforeFailure, await ComposedFileSnapshotAsync(database, host));
                observations.Add(new { scenario = "NATIVE_VERSION_INSERT_RLS_POLICY_FAILURE", status = 500, actionReceipt = receipt,
                    beforeFailure, insertPrivilegeRetained = true, applicationStorageCompensated = true, databaseFingerprintsPreserved = true });
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    ALTER POLICY sec_arch_draft_composed_file_versions ON file_versions
                        WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                    """);
            }
            await PositiveAsync(alpha);
            var beforeRollback = await ComposedFileSnapshotAsync(database, host);
            var pathsBefore = StoredFilePaths(host);
            var rollbackCapture = Guid.NewGuid();
            using (var request = FileUploadRequest(alphaWorkspace, rollbackCapture))
            {
                request.Headers.Add("X-Sec-Arch-Composed-Exception", "true");
                using var rolledBack = await alpha.SendAsync(request);
                Assert.Equal(HttpStatusCode.InternalServerError, rolledBack.StatusCode);
            }
            var rollbackReceipt = await host.ReceiptAsync(rollbackCapture);
            AssertReceipt(rollbackReceipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: false, efSaveCount: 1);
            var afterRollback = await ComposedFileSnapshotAsync(database, host);
            Assert.Equal(beforeRollback.RowsDigest, afterRollback.RowsDigest);
            Assert.Equal(beforeRollback.Files, afterRollback.Files);
            Assert.Equal(beforeRollback.StorageCount + 1, afterRollback.StorageCount);
            var orphan = Assert.Single(StoredFilePaths(host).Except(pathsBefore, StringComparer.Ordinal));
            Assert.Equal(ComposedFileBytes, await File.ReadAllBytesAsync(orphan));
            var key = Path.GetRelativePath(host.FileStorageRoot, orphan).Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal(0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT count(*) FROM file_objects WHERE \"StorageKey\"=@key", ("key", key)));
            observations.Add(new { scenario = "CALLER_OWNED_OUTER_ROLLBACK_AFTER_FILE_SERVICE_SUCCESS", status = 500,
                actionReceipt = rollbackReceipt, databaseRowsDigest = afterRollback.RowsDigest, retainedBlobDigest = FileBytesDigest(),
                beforeRollback, afterRollback,
                persistedFileCount = 0, retainedBlobCount = 1, callerRollbackStorageCompensation = "UNVERIFIED", fullApplicationAtomicity = "UNVERIFIED" });
            // Explicit fixture cleanup is distinct from application compensation.
            File.Delete(orphan);
            Assert.Equal(beforeRollback, await ComposedFileSnapshotAsync(database, host));
            await PositiveAsync(alpha);
            await WritePrivateAsync("draft-rls-composed-web-file-compensation.json", database, role, new
            {
                observations, host.WebAssemblyDigest, selectedPolicyCount = ComposedFileTables.Length,
                nativeVersionTriggerSecurityDefiner = false, persistenceFailureStorageCompensation = true,
                successfulOuterRollbackStorageCompensation = "UNVERIFIED", fullApplicationAtomicity = "UNVERIFIED",
                productFileTransactionOwnership = "UNCHANGED", prototypeTransactionOwnership = "EXPLICIT_TEST_OWNED_SELECTED_ACTION"
            });

            async Task PositiveAsync(HttpClient client)
            {
                var before = await ComposedFileSnapshotAsync(database, host);
                var capture = Guid.NewGuid();
                using var request = FileUploadRequest(alphaWorkspace, capture);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var file = body.RootElement.GetProperty("fileObjectId").GetGuid();
                await AssertComposedNativeFileAsync(database, host, file, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId);
                AssertComposedFileAdded(before, await ComposedFileSnapshotAsync(database, host));
                var receipt = await host.ReceiptAsync(capture);
                AssertReceipt(receipt, role, alphaTenant, SecurityCiFixtureSeed.TenantAMemberUserId, committed: true, efSaveCount: 1);
                observations.Add(new { scenario = "AUTHORIZED_AND_RESTORED_UPLOAD", status = 200, actionReceipt = receipt, nativeVersionDigest = await ComposedVersionDigestAsync(database, file) });
            }
        }, fileProbe: true);

    private static HttpRequestMessage FileUploadRequest(Guid workspace, Guid capture)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(((int)AttachmentOwnerType.Workspace).ToString(CultureInfo.InvariantCulture)), "OwnerType");
        form.Add(new StringContent(workspace.ToString("D")), "OwnerId");
        var bytes = new ByteArrayContent(ComposedFileBytes);
        bytes.Headers.ContentType = new("text/plain");
        form.Add(bytes, "File", "synthetic.txt");
        var request = Request(HttpMethod.Post, "/api/files", capture);
        request.Content = form;
        return request;
    }

    private static Task GrantComposedFileOperationsAsync(string database, string role) => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
        GRANT SELECT ON file_objects,file_versions,attachments,audit_logs,outbox_events,tenant_settings,subscriptions,plans,usage_records,
            projects,project_members,groups,group_members,task_items,file_access_grants TO "{role}";
        GRANT INSERT ON file_objects,file_versions,attachments,outbox_events TO "{role}";
        """);

    private static async Task InstallComposedFilePoliciesAsync(string database, string role)
    {
        var privileges = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'SELECT')),
                count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'INSERT')),
                count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'UPDATE')),
                count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'DELETE')),
                count(*) FILTER (WHERE has_table_privilege(@role,c.oid,'TRUNCATE'))
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p')
            """, reader => (Select: reader.GetInt64(0), Insert: reader.GetInt64(1), Update: reader.GetInt64(2),
                Delete: reader.GetInt64(3), Truncate: reader.GetInt64(4)), ("role", role)));
        Assert.Equal(21L, privileges.Select);
        Assert.Equal(7L, privileges.Insert);
        Assert.Equal(2L, privileges.Update);
        Assert.Equal(0L, privileges.Delete);
        Assert.Equal(0L, privileges.Truncate);
        foreach (var table in ComposedFileTables)
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY; ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_draft_composed_{table} ON "{table}" TO "{role}"
                    USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                    WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                """);
        }
        Assert.Equal(5L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relrowsecurity AND c.relforcerowsecurity"));
        Assert.Equal(5L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
            "SELECT count(*) FROM pg_policies WHERE schemaname='public' AND policyname LIKE 'sec_arch_draft_composed_%'"));
        Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
            "SELECT prosecdef FROM pg_proc WHERE proname='coglatas_capture_initial_file_version'"));
    }

    private static Task SetComposedFileMembershipAsync(string database, Guid workspace, bool active) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
            "UPDATE workspace_members SET \"Status\"=@status WHERE \"WorkspaceId\"=@workspace AND \"UserId\"=@user",
            ("status", active ? MembershipStatus.Active.ToString() : MembershipStatus.Suspended.ToString()),
            ("workspace", workspace), ("user", SecurityCiFixtureSeed.TenantAMemberUserId));

    private static async Task AssertComposedNativeFileAsync(string database, SecurityArchitectureRlsComposedHostFixture host, Guid file, Guid tenant, Guid subject)
    {
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT EXISTS(SELECT 1 FROM file_objects f JOIN file_versions v ON v."FileObjectId"=f."Id"
                JOIN attachments a ON a."FileObjectId"=f."Id" WHERE f."Id"=@file AND f."TenantId"=@tenant
                AND v."TenantId"=@tenant AND a."TenantId"=@tenant AND f."UploadedByUserId"=@subject
                AND v."Id"=f."Id" AND v."VersionNumber"=1 AND v."StorageKey"=f."StorageKey"
                AND v."HashSha256" IS NOT DISTINCT FROM f."HashSha256" AND f."SharingPolicy"='Private'
                AND v."CreatedByUserId"=@subject AND v."SizeBytes"=@size
                AND (SELECT count(*) FROM audit_logs l WHERE l."EntityId"=f."Id" AND l."Action"='FileUploaded')=1
                AND (SELECT count(*) FROM outbox_events e WHERE e."AggregateId"=f."Id" AND e."EventType"='Files.FileChanged.v1')=1)
            """, ("file", file), ("tenant", tenant), ("subject", subject), ("size", (long)ComposedFileBytes.Length)));
        var key = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
            "SELECT \"StorageKey\" FROM file_objects WHERE \"Id\"=@file", ("file", file));
        var path = Path.GetFullPath(Path.Combine(host.FileStorageRoot, key));
        Assert.StartsWith(Path.GetFullPath(host.FileStorageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ComposedFileBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(FileBytesDigest(), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant());
    }

    private static Task<string> ComposedVersionDigestAsync(string database, Guid file) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
        "SELECT encode(sha256(convert_to(string_agg(to_jsonb(v)::text,'|' ORDER BY v.\"Id\"),'UTF8')),'hex') FROM file_versions v WHERE v.\"FileObjectId\"=@file", ("file", file));
    private static string FileBytesDigest() => Convert.ToHexString(SHA256.HashData(ComposedFileBytes)).ToLowerInvariant();
    private static string[] StoredFilePaths(SecurityArchitectureRlsComposedHostFixture host) => Directory.Exists(host.FileStorageRoot)
        ? Directory.GetFiles(host.FileStorageRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [];
    private sealed record ComposedFileSnapshot(long Files, long Versions, long Attachments, long Audits, long Events,
        string RowsDigest, int StorageCount, string StorageDigest);

    private static async Task<ComposedFileSnapshot> ComposedFileSnapshotAsync(string database, SecurityArchitectureRlsComposedHostFixture host)
    {
        var counts = new List<long>();
        var identities = new List<string>();
        foreach (var table in ComposedFileTables)
        {
            var row = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(database, $"""
                SELECT count(*),encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,'|' ORDER BY to_jsonb(r)::text COLLATE "C"),''),'UTF8')),'hex')
                FROM public."{table}" r
                """, reader => (Count: reader.GetInt64(0), Digest: reader.GetString(1))));
            counts.Add(row.Count);
            identities.Add(table + ":" + row.Digest);
        }
        var storageIdentities = new List<string>();
        foreach (var path in StoredFilePaths(host))
        {
            await using var content = File.OpenRead(path);
            storageIdentities.Add(Path.GetRelativePath(host.FileStorageRoot, path) + ":" + Convert.ToHexString(await SHA256.HashDataAsync(content)).ToLowerInvariant());
        }
        return new(counts[0], counts[1], counts[2], counts[3], counts[4], Digest(identities), storageIdentities.Count, Digest(storageIdentities));
        static string Digest(IEnumerable<string> values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)))).ToLowerInvariant();
    }

    private static void AssertComposedFileAdded(ComposedFileSnapshot before, ComposedFileSnapshot after)
    {
        Assert.Equal(before.Files + 1, after.Files);
        Assert.Equal(before.Versions + 1, after.Versions);
        Assert.Equal(before.Attachments + 1, after.Attachments);
        Assert.Equal(before.Audits + 1, after.Audits);
        Assert.Equal(before.Events + 1, after.Events);
        Assert.NotEqual(before.RowsDigest, after.RowsDigest);
        Assert.Equal(before.StorageCount + 1, after.StorageCount);
        Assert.NotEqual(before.StorageDigest, after.StorageDigest);
    }
}
