using System.Net;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class WorkspaceMemberManagementHostedHttpTests
{
    [PostgreSqlFact]
    public Task AnonymousReadsAreUnauthorizedAndPrivateNoStore() => WithManagerAsync(async host =>
    {
        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };
        anonymous.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
        foreach (var route in new[] { OptionsRoute(host), $"{CandidatesRoute(host)}?q=Other" })
        {
            using var response = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AssertNoStore(response);
            Assert.DoesNotContain(host.OtherWorkspaceRecipient.DisplayName, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    });

    [PostgreSqlFact]
    public Task ExpiredSessionAndDeletedAccountCannotReadManagementPreviews() => WithManagerAsync(async host =>
    {
        await WithDbAsync(host, db => db.Sessions.Where(session => session.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await AssertBothReadsAsync(host, HttpStatusCode.Unauthorized);
        await host.LoginAsync();
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.Users.Where(user => user.Id == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.DeletedAt, DateTimeOffset.UtcNow)));
        await AssertBothReadsAsync(host, HttpStatusCode.Unauthorized);
    });

    [PostgreSqlFact]
    public Task InactiveTenantCannotResolveManagementDirectoryScope() => WithManagerAsync(async host =>
    {
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.Tenants.Where(tenant => tenant.Id == host.Tenant.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(tenant => tenant.Status, TenantStatus.Suspended)));
        await AssertBothReadsAsync(host, HttpStatusCode.NotFound);
    });

    [PostgreSqlFact]
    public Task MissingForeignAndUndefinedPersistedTargetOptionsNeverInventCanonicalRoles() => WithManagerAsync(async host =>
    {
        foreach (var userId in new[] { Guid.NewGuid(), host.ForeignRecipient.Id, host.OtherWorkspaceRecipient.Id })
        {
            using var response = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={userId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertNoStore(response);
        }
        await WithDbAsync(host, db => db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, (WorkspaceRole)999)));
        using (var response = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={host.Recipient.Id}"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await WithDbAsync(host, db => db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.Member)
                .SetProperty(member => member.Status, (MembershipStatus)999)));
        using (var response = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={host.Recipient.Id}"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    });
}
