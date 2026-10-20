using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class WorkspaceMemberManagementHostedHttpTests
{
    [PostgreSqlFact]
    public Task CurrentManagerRolesAndSystemAdminAreRequiredOnBothReads() => WithManagerAsync(async host =>
    {
        foreach (var role in Enum.GetValues<WorkspaceRole>())
        {
            await SetActorRoleAsync(host, role);
            var expected = role is WorkspaceRole.Owner or WorkspaceRole.Admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            await AssertBothReadsAsync(host, expected);
        }
        await WithDbAsync(host, async db =>
        {
            await db.Users.Where(user => user.Id == host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.SystemRole, SystemRole.SystemAdmin));
            await db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Actor.Id)
                .ExecuteDeleteAsync();
        });
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.Workspaces.Where(workspace => workspace.Id == host.Workspace.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(workspace => workspace.Status, WorkspaceStatus.Archived)));
        await AssertBothReadsAsync(host, HttpStatusCode.NotFound);
    });

    [PostgreSqlFact]
    public Task MissingForeignAndNonmemberScopesAreMaskedWhileArchivedManagerCannotManage() => WithManagerAsync(async host =>
    {
        var scopes = new List<Guid> { Guid.NewGuid() };
        await WithDbAsync(host, async db => scopes.AddRange(await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.Id != host.Workspace.Id).Select(workspace => workspace.Id).ToArrayAsync()));
        foreach (var workspaceId in scopes)
            await AssertBothReadsAsync(host, HttpStatusCode.NotFound, workspaceId);
        host.Client.DefaultRequestHeaders.Remove("X-Tenant-Slug");
        host.Client.DefaultRequestHeaders.Add("X-Tenant-Slug", "missing-synthetic-tenant");
        using (var response = await host.Client.GetAsync(OptionsRoute(host)))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Tenant-Slug");
        host.Client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
        await WithDbAsync(host, db => db.Workspaces.Where(workspace => workspace.Id == host.Workspace.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(workspace => workspace.Status, WorkspaceStatus.Archived)));
        await AssertBothReadsAsync(host, HttpStatusCode.Forbidden);
    });

    [PostgreSqlFact]
    public Task RevokedWorkspaceAuthorityCannotReuseSuccessfulPreview() => WithManagerAsync(async host =>
    {
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended)));
        await AssertBothReadsAsync(host, HttpStatusCode.NotFound);
        using var command = await host.PostAsync($"/api/workspaces/{host.Workspace.Id}/members", new { userId = host.OtherWorkspaceRecipient.Id, role = 3 });
        Assert.Equal(HttpStatusCode.NotFound, command.StatusCode);
        await WithDbAsync(host, async db => Assert.False(await db.WorkspaceMembers.AnyAsync(member =>
            member.WorkspaceId == host.Workspace.Id && member.UserId == host.OtherWorkspaceRecipient.Id)));
    });

    [PostgreSqlFact]
    public Task AccountTenantMembershipAndSessionRevocationDenyWithoutDirectoryData() => WithManagerAsync(async host =>
    {
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.TenantUsers.Where(member => member.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, TenantUserStatus.Suspended)));
        await AssertBothReadsAsync(host, HttpStatusCode.Unauthorized);
        await WithDbAsync(host, db => db.TenantUsers.Where(member => member.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, TenantUserStatus.Active)));
        await host.LoginAsync();
        await AssertBothReadsAsync(host, HttpStatusCode.OK);
        await WithDbAsync(host, db => db.Users.Where(user => user.Id == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.Status, UserStatus.Suspended)));
        await AssertBothReadsAsync(host, HttpStatusCode.Unauthorized);
        await WithDbAsync(host, db => db.Users.Where(user => user.Id == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.Status, UserStatus.Active)));
        await host.LoginAsync();
        await WithDbAsync(host, db => db.Sessions.Where(session => session.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow)));
        await AssertBothReadsAsync(host, HttpStatusCode.Unauthorized);
    });

    [PostgreSqlFact]
    public Task CandidatePaginationExcludesEveryExistingRelationshipAndInactiveOrForeignIdentity() => WithManagerAsync(async host =>
    {
        var expected = new List<Guid>();
        await WithDbAsync(host, async db =>
        {
            for (var index = 0; index < 8; index++)
            {
                var user = NewCandidate("Directory duplicate");
                db.Users.Add(user);
                db.TenantUsers.Add(new TenantUser { UserId = user.Id, Status = index == 7 ? TenantUserStatus.Suspended : TenantUserStatus.Active });
                if (index < 3) expected.Add(user.Id);
                if (index is >= 3 and <= 5)
                    db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = host.Workspace.Id, UserId = user.Id, Status = (MembershipStatus)(index - 3) });
                if (index == 6) user.Status = UserStatus.Suspended;
            }
            var foreign = await db.Users.SingleAsync(user => user.Id == host.ForeignRecipient.Id);
            foreign.DisplayName = "Directory duplicate";
            await db.SaveChangesAsync();
        });
        var seen = new List<Guid>();
        Guid? after = null;
        for (var page = 0; page < 3; page++)
        {
            using var response = await host.Client.GetAsync($"{CandidatesRoute(host)}?q=Directory&take=1" + (after.HasValue ? $"&afterUserId={after}" : ""));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
            var id = item.GetProperty("userId").GetGuid();
            seen.Add(id);
            var hasMore = body.RootElement.GetProperty("hasMore").GetBoolean();
            Assert.Equal(page < 2, hasMore);
            after = hasMore ? body.RootElement.GetProperty("nextAfterUserId").GetGuid() : null;
            if (hasMore) Assert.Equal(id, after);
            else Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("nextAfterUserId").ValueKind);
        }
        // PostgreSQL's UUID ordering is exercised against its actual keyset predicate.
        Assert.Equal(expected.OrderBy(id => id), seen);
        using var foreignCursor = await host.Client.GetAsync($"{CandidatesRoute(host)}?q=Directory&afterUserId={host.ForeignRecipient.Id}");
        Assert.Equal(HttpStatusCode.OK, foreignCursor.StatusCode);
        using var remaining = JsonDocument.Parse(await foreignCursor.Content.ReadAsStringAsync());
        Assert.All(remaining.RootElement.GetProperty("items").EnumerateArray(), item =>
            Assert.Contains(item.GetProperty("userId").GetGuid(), expected));
    });

    [PostgreSqlFact]
    public Task PrefixUsesUnicodeScalarBoundsAndLiteralPrefixSemantics() => WithManagerAsync(async host =>
    {
        var hundred = string.Concat(Enumerable.Repeat("😀", 100));
        await WithDbAsync(host, async db =>
        {
            foreach (var name in new[] { "😀😀 candidate", hundred, "%_literal", "other literal" })
            {
                var user = NewCandidate(name);
                db.Users.Add(user);
                db.TenantUsers.Add(new TenantUser { UserId = user.Id, Status = TenantUserStatus.Active });
            }
            await db.SaveChangesAsync();
        });
        foreach (var prefix in new[] { "😀😀", hundred, "  %_  " })
        {
            using var response = await host.Client.GetAsync($"{CandidatesRoute(host)}?q={Uri.EscapeDataString(prefix)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(body.RootElement.GetProperty("items").EnumerateArray());
            Assert.All(body.RootElement.GetProperty("items").EnumerateArray(), item =>
                Assert.StartsWith(prefix.Trim(), item.GetProperty("displayName").GetString(), StringComparison.Ordinal));
        }
        foreach (var query in new[] { "", "q=", "q=%20%20", "q=日", "q=😀", $"q={Uri.EscapeDataString(hundred + "😀")}", "q=ab%00cd", "q=ab&take=0", "q=ab&take=51", "q=ab&take=not-an-int", $"q=ab&afterUserId={Guid.Empty}", "q=ab&afterUserId=not-a-guid" })
        {
            using var response = await host.Client.GetAsync($"{CandidatesRoute(host)}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoStore(response);
        }
    });

    [PostgreSqlFact]
    public async Task CanonicalProductionLoggingObservesOutcomesWithoutQueryNamesOrReturnedIdentities()
    {
        using var logs = new CapturedLogs();
        await WithManagerAsync(async host =>
        {
            const string prefix = "PrivateSyntheticPrefix";
            const string name = "PrivateSyntheticPrefix DisplayIdentity";
            await WithDbAsync(host, db => db.Users.Where(user => user.Id == host.OtherWorkspaceRecipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.DisplayName, name)));
            logs.Clear();
            using (var response = await host.Client.GetAsync($"{CandidatesRoute(host)}?q={prefix}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(name, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            await SetActorRoleAsync(host, WorkspaceRole.ReadOnly);
            using (var denied = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={host.OtherWorkspaceRecipient.Id}"))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var recorded = logs.Messages.ToArray();
            Assert.Contains(recorded, message => message.Contains("candidates completed with status 200", StringComparison.Ordinal));
            Assert.Contains(recorded, message => message.Contains("options completed with status 403", StringComparison.Ordinal));
            foreach (var message in recorded)
            {
                Assert.DoesNotContain(prefix, message, StringComparison.Ordinal);
                Assert.DoesNotContain(name, message, StringComparison.Ordinal);
                Assert.DoesNotContain(host.OtherWorkspaceRecipient.Id.ToString(), message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(host.OtherWorkspaceRecipient.Email, message, StringComparison.OrdinalIgnoreCase);
            }
        }, logs);
    }

    private static User NewCandidate(string name)
    {
        var email = $"workspace-options-{Guid.NewGuid():N}@example.test";
        return new User { DisplayName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(), Status = UserStatus.Active };
    }

    private static Task SetActorRoleAsync(AdmissionHost host, WorkspaceRole role) => WithDbAsync(host, db =>
        db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, role)));

    private static async Task AssertBothReadsAsync(AdmissionHost host, HttpStatusCode expected, Guid? workspaceId = null)
    {
        foreach (var suffix in new[] { "options", "candidates?q=Other" })
        {
            using var response = await host.Client.GetAsync($"/api/workspaces/{workspaceId ?? host.Workspace.Id}/members/management/{suffix}");
            Assert.Equal(expected, response.StatusCode);
            AssertNoStore(response);
            if (expected != HttpStatusCode.OK)
                Assert.DoesNotContain(host.OtherWorkspaceRecipient.DisplayName, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public void Clear() => Messages.Clear();
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);
        public void Dispose() { }

        private sealed class CapturedLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
