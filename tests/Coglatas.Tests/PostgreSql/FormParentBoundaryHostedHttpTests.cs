using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Forms;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

// Actual PostgreSQL, Kestrel, cookie sessions, CSRF and production DI; not a deployed-role/RLS qualification.
public sealed class FormParentBoundaryHostedHttpTests
{
    private static readonly string[] ReportReadSuffixes = ["/responses", "/summary", "/unanswered-users"];
    private static readonly string[] HistoricalReadSuffixes = ["", "/responses", "/summary", "/unanswered-users"];

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentReadOnlyCreatorCanReadDraftButCannotMutateThroughRetainedCookie() =>
        AssertParentTransitionAsync(retainedGroupManager: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GroupManagerCannotRetainFormMutationAfterWorkspaceSuspension() =>
        AssertParentTransitionAsync(retainedGroupManager: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ArchivedProjectManagerRetainsCookieAuthenticatedHistoricalFormReports() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await ConversationAdmissionHostedHttpTests.AdmissionHost.CreateAsync(database);
                var project = new Project
                {
                    TenantId = host.Tenant.Id, WorkspaceId = host.Workspace.Id,
                    Name = "Archived synthetic project", Slug = "archived-synthetic-project",
                    OwnerUserId = host.Actor.Id, CreatedByUserId = host.Actor.Id,
                    Status = ProjectStatus.Archived
                };
                var form = new InternalForm
                {
                    TenantId = host.Tenant.Id, ProjectId = project.Id,
                    CreatedByUserId = host.Recipient.Id, Title = "Historical synthetic form"
                };
                var question = new FormQuestion { TenantId = host.Tenant.Id, FormId = form.Id, QuestionText = "Historical question" };
                var response = new FormResponse { TenantId = host.Tenant.Id, FormId = form.Id, RespondentUserId = host.Recipient.Id, SubmittedAt = DateTimeOffset.UtcNow };
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    db.Projects.Add(project);
                    db.ProjectMembers.Add(new ProjectMember { TenantId = host.Tenant.Id, ProjectId = project.Id, UserId = host.Actor.Id, Role = ProjectRole.Manager });
                    db.InternalForms.Add(form);
                    db.FormQuestions.Add(question);
                    db.FormResponses.Add(response);
                    db.FormAnswers.Add(new FormAnswer { TenantId = host.Tenant.Id, FormResponseId = response.Id, FormQuestionId = question.Id, AnswerText = "Synthetic historical private answer" });
                    await db.SaveChangesAsync();
                }
                await host.LoginAsync();
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
                    var projects = scope.ServiceProvider.GetRequiredService<IProjectAuthorizationService>();
                    Assert.False(await projects.CanViewProject(host.Actor.Id, project.Id));
                    Assert.True(await projects.CanManageProject(host.Actor.Id, project.Id));
                }
                var before = await StateAsync(database, form.Id);
                foreach (var suffix in HistoricalReadSuffixes)
                {
                    using var allowed = await host.Client.GetAsync($"/api/forms/{form.Id:D}{suffix}");
                    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                    if (suffix == "/responses")
                        Assert.Contains("Synthetic historical private answer", await allowed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                }
                using (var rejectedByExistingValidator = await PatchAsync(host.Client, $"/api/forms/{form.Id:D}", "Archived mutation"))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, rejectedByExistingValidator.StatusCode);
                    Assert.Contains("Project not found.", await rejectedByExistingValidator.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                }
                Assert.Equal(before, await StateAsync(database, form.Id));
            });

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
                var question = new FormQuestion { TenantId = host.Tenant.Id, FormId = form.Id, QuestionText = "Synthetic existing question" };
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
                    db.FormQuestions.Add(question);
                    await db.SaveChangesAsync();
                }

                var route = $"/api/forms/{form.Id:D}";
                using (var anonymous = await host.Client.GetAsync(route))
                    Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
                // Unsafe Forms routes enforce CSRF before their coarse authorization gate.
                using (var anonymousWrite = await PatchAsync(host.Client, route, "Anonymous mutation", includeCsrf: false))
                    Assert.Equal(HttpStatusCode.Forbidden, anonymousWrite.StatusCode);
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
                    foreach (var suffix in ReportReadSuffixes)
                    {
                        using var report = await host.Client.GetAsync(route + suffix);
                        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
                    }
                }
                using var denied = await PatchAsync(host.Client, route, "Unauthorized synthetic mutation");
                var after = await StateAsync(database, form.Id);
                Assert.True(denied.StatusCode == HttpStatusCode.BadRequest,
                    $"Parent boundary must deny mutation: actualHTTP={(int)denied.StatusCode}; persistedTitle={after.Title}; formAudits={after.FormAudits}.");
                Assert.Equal(before, after);
                Assert.Equal(before.Status, after.Status);
                Assert.Equal(before.DeletedAt, after.DeletedAt);
                Assert.Equal(before.Questions, after.Questions);
                Assert.Equal(before.Notifications, after.Notifications);
                Assert.Equal(before.OutboxEvents, after.OutboxEvents);
                foreach (var (method, suffix, body) in new (HttpMethod, string, object?)[]
                {
                    (HttpMethod.Delete, "", null),
                    (HttpMethod.Post, "/open", null),
                    (HttpMethod.Post, "/close", null),
                    (HttpMethod.Post, "/questions", new CreateFormQuestionRequest("Denied new question", FormQuestionType.ShortText)),
                    (HttpMethod.Patch, $"/questions/{question.Id:D}", new UpdateFormQuestionRequest(QuestionText: "Denied question edit")),
                    (HttpMethod.Delete, $"/questions/{question.Id:D}", null)
                })
                {
                    using var forbidden = await SendWithCsrfAsync(host.Client, method, route + suffix, body);
                    Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
                    Assert.Equal(before, await StateAsync(database, form.Id));
                }
                if (retainedGroupManager)
                {
                    using var hidden = await host.Client.GetAsync(route);
                    Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
                    foreach (var suffix in ReportReadSuffixes)
                    {
                        using var report = await host.Client.GetAsync(route + suffix);
                        Assert.Equal(HttpStatusCode.BadRequest, report.StatusCode);
                        Assert.DoesNotContain(host.Actor.Email, await report.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                    }
                }
            });

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string route, string title, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route);
        request.Content = JsonContent.Create(new UpdateFormRequest(Title: title));
        if (!includeCsrf)
        {
            return await client.SendAsync(request);
        }

        using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
        tokenResponse.EnsureSuccessStatusCode();
        var payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        request.Headers.Add(SecurityOptions.CsrfHeaderName, payload!.Token);
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

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(HttpClient client, HttpMethod method, string route, object? body)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
        tokenResponse.EnsureSuccessStatusCode();
        var payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        request.Headers.Add(SecurityOptions.CsrfHeaderName, payload!.Token);
        return await client.SendAsync(request);
    }

    private sealed record ProtectedState(string Title, FormStatus Status, DateTimeOffset? DeletedAt,
        int Questions, int FormAudits, int Notifications, int OutboxEvents);
}
