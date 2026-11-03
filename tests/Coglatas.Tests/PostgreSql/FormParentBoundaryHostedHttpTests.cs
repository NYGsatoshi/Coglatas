using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Forms;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

// Actual PostgreSQL, Kestrel, cookie sessions, CSRF and production DI; not a deployed-role/RLS qualification.
public sealed class FormParentBoundaryHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentReadOnlyCreatorCanReadDraftButCannotMutateThroughRetainedCookie() =>
        AssertParentTransitionAsync(retainedGroupManager: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GroupManagerCannotRetainFormMutationAfterWorkspaceSuspension() =>
        AssertParentTransitionAsync(retainedGroupManager: true);

    private static Task AssertParentTransitionAsync(bool retainedGroupManager) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                // Reuse the established real-login host; its development tenant-header,
                // loopback HTTP and disabled rate-limiter settings remain explicit fixture limits.
                await using var host = await ConversationAdmissionHostedHttpTests.AdmissionHost.CreateAsync(database);
                var form = new InternalForm
                {
                    TenantId = host.Tenant.Id, WorkspaceId = host.Workspace.Id,
                    CreatedByUserId = retainedGroupManager ? host.Recipient.Id : host.Actor.Id,
                    Title = "Original synthetic form", Status = FormStatus.Draft
                };
                Guid? groupId = null;
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    if (retainedGroupManager)
                    {
                        var group = new Group
                        {
                            TenantId = host.Tenant.Id, WorkspaceId = host.Workspace.Id,
                            Name = "Synthetic managed group", Slug = "synthetic-managed-group",
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

                var route = $"/api/forms/{form.Id:D}";
                using (var anonymous = await PatchAsync(host.Client, route, "Anonymous mutation", includeCsrf: false))
                    Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
                await host.LoginAsync();
                using (var missingCsrf = await PatchAsync(host.Client, route, "Missing CSRF mutation", includeCsrf: false))
                    Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
                using (var healthyRead = await host.Client.GetAsync(route))
                    Assert.Equal(HttpStatusCode.OK, healthyRead.StatusCode);
                using (var healthyWrite = await PatchAsync(host.Client, route, "Healthy synthetic update"))
                    Assert.Equal(HttpStatusCode.OK, healthyWrite.StatusCode);
                var before = await StateAsync(database, form.Id);
                Assert.Equal("Healthy synthetic update", before.Title);
                Assert.Equal(1, before.FormAudits);

                // Commit the transition in another context; subsequent HTTP requests have fresh scopes.
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var member = await db.WorkspaceMembers.SingleAsync(row =>
                        row.WorkspaceId == host.Workspace.Id && row.UserId == host.Actor.Id);
                    if (retainedGroupManager) member.Status = MembershipStatus.Suspended;
                    else member.Role = WorkspaceRole.ReadOnly;
                    await db.SaveChangesAsync();
                    Assert.True(await db.TenantUsers.AnyAsync(row => row.TenantId == host.Tenant.Id &&
                        row.UserId == host.Actor.Id && row.Status == TenantUserStatus.Active));
                }
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
                    var workspaces = scope.ServiceProvider.GetRequiredService<IWorkspaceAuthorizationService>();
                    Assert.Equal(!retainedGroupManager, await workspaces.CanViewWorkspace(host.Actor.Id, host.Workspace.Id));
                    Assert.False(await workspaces.CanContributeWorkspace(host.Actor.Id, host.Workspace.Id));
                    if (groupId.HasValue)
                    {
                        // A retained child management role must not replace lost parent access.
                        Assert.True(await scope.ServiceProvider.GetRequiredService<IGroupAuthorizationService>()
                            .CanManageGroup(host.Actor.Id, groupId.Value));
                    }
                }
                using (var authenticated = await host.Client.GetAsync("/api/auth/me"))
                    Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
                if (!retainedGroupManager)
                {
                    using var readable = await host.Client.GetAsync(route);
                    Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
                }
                using var denied = await PatchAsync(host.Client, route, "Unauthorized synthetic mutation");
                var after = await StateAsync(database, form.Id);
                Assert.True(denied.StatusCode == HttpStatusCode.BadRequest,
                    $"Parent boundary must deny mutation: actualHTTP={(int)denied.StatusCode}; persistedTitle={after.Title}; formAudits={after.FormAudits}.");
                Assert.Equal(before, after);
                if (retainedGroupManager)
                {
                    using var hidden = await host.Client.GetAsync(route);
                    Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
                }
            });

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string route, string title, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route)
        {
            Content = JsonContent.Create(new UpdateFormRequest(Title: title))
        };
        if (includeCsrf)
        {
            using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
            tokenResponse.EnsureSuccessStatusCode();
            var payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
            request.Headers.Add(SecurityOptions.CsrfHeaderName, payload!.Token);
        }
        return await client.SendAsync(request);
    }

    private static async Task<ProtectedState> StateAsync(string database, Guid formId)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var form = await db.InternalForms.AsNoTracking().SingleAsync(row => row.Id == formId);
        return new ProtectedState(form.Title, form.Status, form.DeletedAt,
            await db.FormQuestions.CountAsync(row => row.FormId == formId),
            await db.AuditLogs.CountAsync(row => row.EntityId == formId && row.Action.StartsWith("Form")),
            await db.Notifications.CountAsync(), await db.OutboxEvents.CountAsync());
    }

    private sealed record ProtectedState(string Title, FormStatus Status, DateTimeOffset? DeletedAt,
        int Questions, int FormAudits, int Notifications, int OutboxEvents);
}
