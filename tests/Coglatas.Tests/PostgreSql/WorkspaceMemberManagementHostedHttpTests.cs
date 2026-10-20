using System.Net;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class WorkspaceMemberManagementHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task OptionsAndCandidatesUsePlainMinimalCurrentManagementProjection() => WithManagerAsync(async host =>
    {
        using var optionsResponse = await host.Client.GetAsync(OptionsRoute(host));
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var options = JsonDocument.Parse(await optionsResponse.Content.ReadAsStringAsync());
        Assert.Equal(host.Workspace.Id, options.RootElement.GetProperty("workspaceId").GetGuid());
        Assert.True(options.RootElement.GetProperty("canAddMember").GetBoolean());
        Assert.Equal(JsonValueKind.Null, options.RootElement.GetProperty("target").ValueKind);
        AssertNoStore(optionsResponse);

        using var targetResponse = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={host.Actor.Id}");
        Assert.Equal(HttpStatusCode.OK, targetResponse.StatusCode);
        using var target = JsonDocument.Parse(await targetResponse.Content.ReadAsStringAsync());
        var member = target.RootElement.GetProperty("target");
        Assert.Equal(host.Actor.Id, member.GetProperty("userId").GetGuid());
        Assert.Equal((int)WorkspaceRole.Owner, member.GetProperty("currentRole").GetInt32());
        Assert.Equal((int)MembershipStatus.Active, member.GetProperty("membershipStatus").GetInt32());
        Assert.True(member.GetProperty("canChangeRole").GetBoolean());
        Assert.True(member.GetProperty("canRemove").GetBoolean());
        Assert.Equal([0, 1, 2, 3, 4], member.GetProperty("allowedRoles").EnumerateArray().Select(role => role.GetInt32()));

        using var candidatesResponse = await host.Client.GetAsync($"{CandidatesRoute(host)}?q=Other&take=1");
        Assert.Equal(HttpStatusCode.OK, candidatesResponse.StatusCode);
        AssertNoStore(candidatesResponse);
        using var candidates = JsonDocument.Parse(await candidatesResponse.Content.ReadAsStringAsync());
        Assert.Equal(host.Workspace.Id, candidates.RootElement.GetProperty("workspaceId").GetGuid());
        var candidate = Assert.Single(candidates.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(host.OtherWorkspaceRecipient.Id, candidate.GetProperty("userId").GetGuid());
        Assert.Equal(host.OtherWorkspaceRecipient.DisplayName, candidate.GetProperty("displayName").GetString());
        Assert.Equal([0, 1, 2, 3, 4], candidate.GetProperty("allowedRoles").EnumerateArray().Select(role => role.GetInt32()));
        Assert.Equal(["allowedRoles", "displayName", "userId"], candidate.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.False(candidates.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, candidates.RootElement.GetProperty("nextAfterUserId").ValueKind);
        Assert.DoesNotContain("email", candidates.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", candidates.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task UndefinedRoleCannotCreateWorkspaceMembershipThroughExistingCommand() => WithManagerAsync(async host =>
    {
        using var response = await host.PostAsync($"/api/workspaces/{host.Workspace.Id}/members", new
        {
            userId = host.OtherWorkspaceRecipient.Id,
            role = 999
        });
        var created = false;
        await WithDbAsync(host, async db => created = await db.WorkspaceMembers.AsNoTracking().AnyAsync(member =>
            member.WorkspaceId == host.Workspace.Id && member.UserId == host.OtherWorkspaceRecipient.Id));
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest && !created,
            $"Undefined role must return400 without membership creation; actual status={(int)response.StatusCode}, created={created}.");
    });

    private static string OptionsRoute(AdmissionHost host) =>
        $"/api/workspaces/{host.Workspace.Id}/members/management/options";

    private static string CandidatesRoute(AdmissionHost host) =>
        $"/api/workspaces/{host.Workspace.Id}/members/management/candidates";

    private static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
    }

    private static Task WithManagerAsync(Func<AdmissionHost, Task> test,
        Microsoft.Extensions.Logging.ILoggerProvider? logProvider = null) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database, logProvider);
                var workspaceId = host.Workspace.Id;
                var actorId = host.Actor.Id;
                await WithDbAsync(host, db => db.WorkspaceMembers
                    .Where(member => member.WorkspaceId == workspaceId && member.UserId == actorId)
                    .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.Owner)));
                await host.LoginAsync();
                await test(host);
            });

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
