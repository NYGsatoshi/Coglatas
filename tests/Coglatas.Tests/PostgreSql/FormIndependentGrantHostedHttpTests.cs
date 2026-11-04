using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Forms;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

// The same test source executes against baseline and candidate production DI.
// No new role policy is derived from these synthetic migration-role PostgreSQL controls.
public sealed class FormIndependentGrantHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task TeacherKeepsIndependentVisibleScopeFormAuthorityWithReadOnlyMembership() =>
        AssertIndependentGrantAsync(SystemRole.Teacher, groupAdministrator: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task AdminKeepsIndependentVisibleScopeFormAuthorityWithReadOnlyMembership() =>
        AssertIndependentGrantAsync(SystemRole.Admin, groupAdministrator: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GroupAdministratorKeepsIndependentFormAuthorityWithCurrentParentRead() =>
        AssertIndependentGrantAsync(SystemRole.User, groupAdministrator: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task SystemAdminRetainsExistingWorkspaceContributionOverride() =>
        AssertIndependentGrantAsync(SystemRole.SystemAdmin, groupAdministrator: false);

    private static Task AssertIndependentGrantAsync(SystemRole role, bool groupAdministrator) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await ConversationAdmissionHostedHttpTests.AdmissionHost.CreateAsync(database);
                Guid? groupId = null;
                var form = new InternalForm
                {
                    TenantId = host.Tenant.Id, WorkspaceId = host.Workspace.Id,
                    CreatedByUserId = host.Recipient.Id, Title = "Original grant probe", Status = FormStatus.Draft
                };
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var actor = await db.Users.SingleAsync(row => row.Id == host.Actor.Id);
                    actor.SystemRole = role;
                    if (groupAdministrator)
                    {
                        var group = new Group
                        {
                            TenantId = host.Tenant.Id, WorkspaceId = host.Workspace.Id,
                            Name = "Independent grant group", Slug = "independent-grant-group",
                            CreatedByUserId = host.Actor.Id
                        };
                        db.Groups.Add(group);
                        db.GroupMembers.Add(new GroupMember
                        {
                            TenantId = host.Tenant.Id, GroupId = group.Id,
                            UserId = host.Actor.Id, Role = GroupRole.Admin
                        });
                        groupId = group.Id;
                        form.WorkspaceId = null;
                        form.GroupId = group.Id;
                    }
                    db.InternalForms.Add(form);
                    await db.SaveChangesAsync();
                }
                await host.LoginAsync();
                using (var healthy = await PatchAsync(host.Client, form.Id, "Healthy independent update"))
                    Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);

                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var membership = await db.WorkspaceMembers.SingleAsync(row =>
                        row.WorkspaceId == host.Workspace.Id && row.UserId == host.Actor.Id);
                    membership.Role = WorkspaceRole.ReadOnly;
                    await db.SaveChangesAsync();
                    Assert.True(await db.TenantUsers.AnyAsync(row => row.TenantId == host.Tenant.Id &&
                        row.UserId == host.Actor.Id && row.Status == TenantUserStatus.Active));
                }

                bool canCreate;
                bool canManage;
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
                    var db = scope.ServiceProvider.GetRequiredService<Coglatas.Infrastructure.Persistence.AppDbContext>();
                    var stored = await db.InternalForms.SingleAsync(row => row.Id == form.Id);
                    Assert.NotEqual(host.Actor.Id, stored.CreatedByUserId);
                    var parent = scope.ServiceProvider.GetRequiredService<IWorkspaceAuthorizationService>();
                    Assert.True(await parent.CanViewWorkspace(host.Actor.Id, host.Workspace.Id));
                    Assert.Equal(role == SystemRole.SystemAdmin, await parent.CanContributeWorkspace(host.Actor.Id, host.Workspace.Id));
                    var authorization = scope.ServiceProvider.GetRequiredService<IFormAuthorizationService>();
                    canManage = await authorization.CanManageForm(host.Actor.Id, stored);
                    canCreate = await authorization.CanCreateForm(host.Actor.Id, stored.WorkspaceId, stored.GroupId, stored.ProjectId);
                }
                using (var authenticated = await host.Client.GetAsync("/api/auth/me"))
                    Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
                using (var readable = await host.Client.GetAsync($"/api/forms/{form.Id:D}"))
                    Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
                using var updated = await PatchAsync(host.Client, form.Id, "Allowed independent update");
                using var created = await host.PostAsync("/api/forms", new CreateFormRequest(
                    groupId.HasValue ? null : host.Workspace.Id, groupId, null, "Allowed independent creation", null));
                await using var verification = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var title = await verification.InternalForms.Where(row => row.Id == form.Id).Select(row => row.Title).SingleAsync();
                var formRows = await verification.InternalForms.CountAsync(row => row.TenantId == host.Tenant.Id);
                var auditRows = await verification.AuditLogs.CountAsync(row => row.TenantId == host.Tenant.Id && row.Action.StartsWith("Form"));
                Assert.True(updated.StatusCode == HttpStatusCode.OK && created.StatusCode == HttpStatusCode.OK,
                    $"Existing independent grant must remain: updateHTTP={(int)updated.StatusCode}; createHTTP={(int)created.StatusCode}; canManage={canManage}; canCreate={canCreate}; title={title}; formRows={formRows}; formAudits={auditRows}.");
                Assert.True(canManage);
                Assert.True(canCreate);
                Assert.Equal("Allowed independent update", title);
                Assert.Equal(2, formRows);
                Assert.Equal(3, auditRows);
            });

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid formId, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/forms/{formId:D}");
        request.Content = JsonContent.Create(new UpdateFormRequest(Title: title));
        using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token!.Token);
        return await client.SendAsync(request);
    }
}
