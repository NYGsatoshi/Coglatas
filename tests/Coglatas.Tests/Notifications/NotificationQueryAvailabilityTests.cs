using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Notifications;
using Coglatas.Application.Announcements;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Notifications;

[Trait("Scope", "Issue1056")]
public sealed class NotificationQueryAvailabilityTests
{
    [Fact]
    public async Task GenericRecipientPagesAndUnreadCountAvoidUnrelatedProtectedTargetQueries()
    {
        await using var fixture = await Fixture.CreateAsync(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            useMessaging: false);
        foreach (var notification in fixture.Notifications.Take(5)) notification.DeletedAt = FixedClock.Now;
        fixture.Tenant.SetPlatformScope();
        fixture.Db.Notifications.AddRange(
            new Notification { TenantId = fixture.TenantId, UserId = Guid.NewGuid(), NotificationType = NotificationType.System,
                Title = "Another recipient", RelatedEntityType = "TaskItem", RelatedEntityId = fixture.Task.Id },
            new Notification { TenantId = Guid.NewGuid(), UserId = fixture.UserId, NotificationType = NotificationType.System,
                Title = "Another tenant", RelatedEntityType = "Artifact", RelatedEntityId = fixture.Artifact.Id });
        await fixture.Db.SaveChangesAsync();
        fixture.Tenant.SetTenant(fixture.TenantId, fixture.TenantEntity.Slug);
        var service = new DbNotificationService(fixture.Db, fixture.Clock, fixture.Tenant,
            targets: new UnexpectedTargetQueryResolver());

        var page = await service.ListAsync(fixture.UserId, page: 2, pageSize: 1);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(fixture.Notifications[6].Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, await service.GetUnreadCountAsync(fixture.UserId));
        Assert.Empty((await service.ListAsync(fixture.UserId, page: 4, pageSize: 1)).Items);
    }

    [Fact]
    public async Task ProtectedReadNotificationsDoNotForceUnreadTargetQueryButStillRequireListAuthorization()
    {
        await using var fixture = await Fixture.CreateAsync(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            useMessaging: false);
        foreach (var notification in fixture.Notifications.Take(5)) notification.IsRead = true;
        await fixture.Db.SaveChangesAsync();
        var service = new DbNotificationService(fixture.Db, fixture.Clock, fixture.Tenant,
            targets: new UnexpectedTargetQueryResolver());

        Assert.Equal(2, await service.GetUnreadCountAsync(fixture.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(fixture.UserId, page: 1, pageSize: 20));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("workspace-archived")]
    [InlineData("workspace-member-suspended")]
    [InlineData("tenant-member-suspended")]
    [InlineData("user-suspended")]
    [InlineData("tenant-suspended")]
    [InlineData("project-planning")]
    [InlineData("project-suspended")]
    [InlineData("project-archived")]
    [InlineData("task-deleted")]
    [InlineData("artifact-deleted")]
    [InlineData("message-deleted")]
    [InlineData("notification-deleted")]
    [InlineData("another-recipient")]
    [InlineData("missing-target")]
    [InlineData("null-target")]
    [InlineData("task-workspace-mismatch")]
    [InlineData("digest-notification-mismatch")]
    [InlineData("wrong-tenant-context")]
    public async Task ComposableAvailabilityMatchesExistingResolverAndVisiblePagination(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            useMessaging: false);
        await fixture.MutateAsync(mutation);
        await AssertEquivalentAsync(fixture, includeMessage: false);
    }

    [Fact]
    public void ProductionAvailabilityComposesRecursiveMessageReadabilityWithDatabasePaging()
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(Guid.NewGuid(), "translation-test");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only").Options, tenant);
        var resolver = new CanonicalCurrentAuthorizationTargetResolver(db, tenant,
            new CurrentAuthorizationTargetResolver(db, tenant, new MessagingRepository(db)));
        var available = Assert.IsAssignableFrom<IQueryable<Guid>>(resolver.QueryAvailableNotificationIds(tenant.TenantId, Guid.NewGuid()));
        var sql = db.Notifications.Where(notification => available.Contains(notification.Id))
            .OrderByDescending(notification => notification.CreatedAt).ThenByDescending(notification => notification.Id)
            .Skip(5).Take(5).ToQueryString();
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlAvailabilityMatchesResolverForEveryProtectedTargetAndRevocation()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                foreach (var mutation in new[]
                {
                    "active", "workspace-archived", "workspace-member-suspended", "tenant-member-suspended",
                    "user-suspended", "tenant-suspended", "project-planning", "project-suspended", "project-archived",
                    "task-deleted", "artifact-deleted", "message-deleted", "notification-deleted", "another-recipient",
                    "missing-target", "null-target", "task-workspace-mismatch", "digest-notification-mismatch", "wrong-tenant-context"
                })
                {
                    await using var fixture = await Fixture.CreateAsync(new DbContextOptionsBuilder<AppDbContext>()
                        .UseNpgsql(database).Options, useMessaging: true);
                    if (mutation == "task-workspace-mismatch")
                    {
                        // PostgreSQL prevents this malformed scope from being persisted.
                        // The InMemory theory retains resolver coverage for legacy corruption.
                        var available = Assert.IsAssignableFrom<IQueryable<Guid>>(
                            fixture.Canonical.QueryAvailableNotificationIds(fixture.TenantId, fixture.UserId));
                        var originalAvailability = await available.OrderBy(id => id).ToArrayAsync();
                        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.MutateAsync(mutation));
                        var postgres = Assert.IsType<Npgsql.PostgresException>(failure.InnerException);
                        Assert.Equal("P0001", postgres.SqlState);
                        Assert.Equal("Task tenant/workspace/project scope mismatch", postgres.MessageText);
                        fixture.Db.ChangeTracker.Clear();
                        var persisted = await fixture.Db.TaskItems.AsNoTracking().SingleAsync(item => item.Id == fixture.Task.Id);
                        Assert.Equal(fixture.TenantId, persisted.TenantId);
                        Assert.Equal(fixture.Project.WorkspaceId, persisted.WorkspaceId);
                        Assert.Equal(fixture.Project.Id, persisted.ProjectId);
                        Assert.Equal(originalAvailability, await available.OrderBy(id => id).ToArrayAsync());
                    }
                    else
                    {
                        await fixture.MutateAsync(mutation);
                    }
                    await AssertEquivalentAsync(fixture, includeMessage: mutation == "active");
                }
            });
    }

    private static async Task AssertEquivalentAsync(Fixture fixture, bool includeMessage)
    {
        foreach (var resolver in new INotificationTargetResolver[] { fixture.Legacy, fixture.Canonical })
        {
            var availableQuery = Assert.IsAssignableFrom<IQueryable<Guid>>(
                resolver.QueryAvailableNotificationIds(fixture.TenantId, fixture.UserId));
            var actual = await availableQuery.ToHashSetAsync();
            var expected = new HashSet<Guid>();
            foreach (var notification in fixture.Notifications)
            {
                var resolution = await resolver.ResolveAsync(fixture.TenantId, fixture.UserId, notification.Id);
                if (resolution.IsOwned && resolution.IsAvailable &&
                    NotificationCurrentAuthorizationPolicy.RequiresCurrentTargetResolution(notification.RelatedEntityType))
                    expected.Add(notification.Id);
            }
            Assert.True(expected.SetEquals(actual), $"Availability differs for {resolver.GetType().Name}.");
        }

        var expectedVisible = new List<Notification>();
        foreach (var notification in await fixture.Db.Notifications.AsNoTracking()
            .Where(item => item.UserId == fixture.UserId && item.DeletedAt == null)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id).ToListAsync())
        {
            if (!NotificationCurrentAuthorizationPolicy.RequiresCurrentTargetResolution(notification.RelatedEntityType) ||
                (await fixture.Canonical.ResolveAsync(fixture.TenantId, fixture.UserId, notification.Id)).IsAvailable)
                expectedVisible.Add(notification);
        }
        var service = new DbNotificationService(fixture.Db, fixture.Clock, fixture.Tenant, targets: fixture.Canonical);
        var first = await service.ListAsync(fixture.UserId, 1, 2);
        var second = await service.ListAsync(fixture.UserId, 2, 2);
        Assert.Equal(expectedVisible.Count, first.TotalCount);
        Assert.Equal(expectedVisible.Count, second.TotalCount);
        Assert.Equal(expectedVisible.Take(2).Select(item => item.Id), first.Items.Select(item => item.Id));
        Assert.Equal(expectedVisible.Skip(2).Take(2).Select(item => item.Id), second.Items.Select(item => item.Id));
        Assert.Equal(expectedVisible.Count(item => !item.IsRead), await service.GetUnreadCountAsync(fixture.UserId));
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        if (includeMessage && fixture.WorkspaceMember.Status == MembershipStatus.Active)
            Assert.Contains(expectedVisible, item => item.RelatedEntityType == "Message");
    }

    [Fact]
    public async Task InboxPageBatchPreservesLatestMessageAuthorUnreadMentionAndPrivateState()
    {
        await using var fixture = await Fixture.CreateAsync(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, useMessaging: false);
        var sender = new User { DisplayName = "Sender", Email = "sender@example.invalid", NormalizedEmail = "SENDER@EXAMPLE.INVALID", Status = UserStatus.Active };
        var member = await fixture.Db.ConversationMembers.SingleAsync();
        member.IsMuted = true;
        member.IsLater = true;
        member.IsArchived = true;
        var latest = new Message { TenantId = fixture.TenantId, WorkspaceId = fixture.Workspace.Id, ConversationId = fixture.Message.ConversationId, AuthorUserId = sender.Id, Body = "Latest message" };
        fixture.Db.AddRange(sender,
            new TenantUser { TenantId = fixture.TenantId, UserId = sender.Id, Status = TenantUserStatus.Active },
            new ConversationMember { TenantId = fixture.TenantId, ConversationId = latest.ConversationId, UserId = sender.Id }, latest,
            new Notification { TenantId = fixture.TenantId, UserId = fixture.UserId, NotificationType = NotificationType.Mention, Title = "Mention", RelatedEntityType = "Message", RelatedEntityId = latest.Id });
        await fixture.Db.SaveChangesAsync();
        // Fixture insertion stamps creation times. Set the ordering/read cursor
        // explicitly afterwards, as the deterministic production fixture does.
        fixture.Message.CreatedAt = FixedClock.Now;
        latest.CreatedAt = FixedClock.Now.AddMinutes(1);
        fixture.Db.ReadStates.Add(new ReadState { TenantId = fixture.TenantId, ConversationId = latest.ConversationId, UserId = fixture.UserId, LastReadAt = FixedClock.Now });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var repository = new MessagingRepository(fixture.Db);
        var conversation = await fixture.Db.Conversations.AsNoTracking().SingleAsync();
        var batch = (await repository.GetInboxPageDetailsAsync(fixture.UserId, [conversation]))!;
        var details = Assert.Single(batch).Value;
        var oldLatest = Assert.Single((await repository.ListMessagesAsync(conversation.Id, 1, null)).Items);
        var read = await repository.GetReadStateAsync(conversation.Id, fixture.UserId);
        Assert.Equal(oldLatest.Id, details.LastMessage!.Id);
        Assert.Equal(oldLatest.Body, details.LastMessage.Body);
        Assert.Equal(oldLatest.AuthorUser!.DisplayName, details.LastMessage.AuthorUser!.DisplayName);
        Assert.Equal(await repository.CountUnreadMessagesAsync(conversation.Id, fixture.UserId, read?.LastReadAt), details.UnreadCount);
        Assert.Equal(1, details.UnreadCount);
        Assert.True(details.HasUnreadMention);
        Assert.True(details.CurrentMember!.IsMuted);
        Assert.True(details.CurrentMember.IsLater);
        Assert.True(details.CurrentMember.IsArchived);
        Assert.Empty((await repository.GetInboxPageDetailsAsync(fixture.UserId, []))!);
        await fixture.Db.Notifications.Where(item => item.NotificationType == NotificationType.Mention)
            .ForEachAsync(item => item.IsRead = true);
        await fixture.Db.SaveChangesAsync();
        Assert.False((await repository.GetInboxPageDetailsAsync(fixture.UserId, [conversation]))![conversation.Id].HasUnreadMention);
    }

    [Fact]
    public async Task AnnouncementPageBatchesRecipientReadStateWithoutCrossUserOrOffPageReads()
    {
        await using var fixture = await Fixture.CreateAsync(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, useMessaging: false);
        var announcements = Enumerable.Range(0, 4).Select(index => new Announcement
        {
            TenantId = fixture.TenantId, WorkspaceId = fixture.Workspace.Id, AuthorUserId = fixture.UserId,
            Title = "Announcement " + index, Body = "Body", PublishedAt = FixedClock.Now.AddMinutes(-index), RequiresReadConfirmation = true
        }).ToArray();
        fixture.Db.Announcements.AddRange(announcements);
        fixture.Db.AnnouncementReads.AddRange(
            new AnnouncementRead { TenantId = fixture.TenantId, AnnouncementId = announcements[0].Id, UserId = fixture.UserId, ReadAt = FixedClock.Now },
            new AnnouncementRead { TenantId = fixture.TenantId, AnnouncementId = announcements[1].Id, UserId = Guid.NewGuid(), ReadAt = FixedClock.Now },
            new AnnouncementRead { TenantId = fixture.TenantId, AnnouncementId = announcements[3].Id, UserId = fixture.UserId, ReadAt = FixedClock.Now });
        await fixture.Db.SaveChangesAsync();
        var repository = new AnnouncementRepository(fixture.Db, fixture.Clock, fixture.Tenant);
        var page = await repository.ListVisibleAsync(fixture.UserId, false, new AnnouncementListQuery(Page: 1, PageSize: 2));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(announcements.Take(2).Select(item => item.Id), page.Items.Select(item => item.Id));
        var readIds = await repository.GetReadAnnouncementIdsAsync(fixture.UserId, page.Items.Select(item => item.Id).ToArray());
        Assert.Equal(announcements[0].Id, Assert.Single(readIds));
        foreach (var announcement in page.Items)
            Assert.Equal(await repository.HasReadAsync(announcement.Id, fixture.UserId), readIds.Contains(announcement.Id));
        Assert.Empty(await repository.GetReadAnnouncementIdsAsync(fixture.UserId, []));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public required CurrentTenantService Tenant { get; init; }
        public required CurrentAuthorizationTargetResolver Legacy { get; init; }
        public required CanonicalCurrentAuthorizationTargetResolver Canonical { get; init; }
        public required Tenant TenantEntity { get; init; }
        public required User User { get; init; }
        public required TenantUser TenantMember { get; init; }
        public required Workspace Workspace { get; init; }
        public required WorkspaceMember WorkspaceMember { get; init; }
        public required Project Project { get; init; }
        public required TaskItem Task { get; init; }
        public required Artifact Artifact { get; init; }
        public required Message Message { get; init; }
        public required TaskDeadlineDigestJob Digest { get; init; }
        public required Notification[] Notifications { get; init; }
        public FixedClock Clock { get; } = new();
        public Guid TenantId => TenantEntity.Id;
        public Guid UserId => User.Id;

        public static async Task<Fixture> CreateAsync(DbContextOptions<AppDbContext> options, bool useMessaging)
        {
            var scope = new CurrentTenantService();
            scope.SetPlatformScope();
            var db = new AppDbContext(options, scope);
            var suffix = Guid.NewGuid().ToString("N");
            var tenant = new Tenant { Name = "Query tenant", DisplayName = "Query tenant", Slug = "query-" + suffix, Status = TenantStatus.Active };
            var user = new User { DisplayName = "Recipient", Email = suffix + "@example.invalid", NormalizedEmail = suffix.ToUpperInvariant() + "@EXAMPLE.INVALID", PasswordHash = "hash", Status = UserStatus.Active };
            db.AddRange(tenant, user);
            await db.SaveChangesAsync();
            scope.SetTenant(tenant.Id, tenant.Slug);
            var tenantMember = new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active };
            var workspace = new Workspace { TenantId = tenant.Id, Name = "Query workspace", Slug = "query-" + suffix, Status = WorkspaceStatus.Active, CreatedByUserId = user.Id };
            var member = new WorkspaceMember { TenantId = tenant.Id, WorkspaceId = workspace.Id, UserId = user.Id, Status = MembershipStatus.Active, Role = WorkspaceRole.Member };
            var project = new Project { TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = "Query project", Slug = "query-" + suffix, Status = ProjectStatus.Active, CreatedByUserId = user.Id, OwnerUserId = user.Id };
            var task = new TaskItem { TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id, Title = "Task", CreatedByUserId = user.Id };
            var artifact = new Artifact { TenantId = tenant.Id, ProjectId = project.Id, Name = "Artifact", CreatedByUserId = user.Id };
            var conversation = new Conversation { TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id, Type = ConversationType.ProjectChannel, Title = "Conversation", CreatedByUserId = user.Id };
            var message = new Message { TenantId = tenant.Id, WorkspaceId = workspace.Id, ConversationId = conversation.Id, AuthorUserId = user.Id, Body = "Message" };
            var digest = new TaskDeadlineDigestJob
            {
                TenantId = tenant.Id, WorkspaceId = workspace.Id, UserId = user.Id,
                LocalDate = new DateOnly(2026, 10, 4), PolicyVersion = TaskDeadlineDigestPolicy.PolicyVersion,
                ScheduledForUtc = FixedClock.Now, NextAttemptAt = FixedClock.Now
            };
            var specs = new (string? Type, Guid? Id)[] { ("TaskItem", task.Id), ("Task", task.Id), ("Artifact", artifact.Id), ("Message", message.Id), (TaskDeadlineDigestPolicy.RelatedEntityType, digest.Id), (null, null), ("Announcement", Guid.NewGuid()), ("Unknown", null) };
            var notifications = specs.Select((spec, index) => new Notification
            {
                TenantId = tenant.Id, UserId = user.Id, NotificationType = NotificationType.System,
                Title = "Notification " + index, RelatedEntityType = spec.Type, RelatedEntityId = spec.Id,
                CreatedAt = FixedClock.Now.AddMinutes(index), IsRead = index == 6, StateVersion = index + 1
            }).ToArray();
            digest.NotificationId = notifications[4].Id;
            db.AddRange(tenantMember, workspace, member, project, task, artifact, conversation, message, digest,
                new ConversationMember { TenantId = tenant.Id, ConversationId = conversation.Id, UserId = user.Id, JoinedAt = FixedClock.Now });
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();
            var legacy = new CurrentAuthorizationTargetResolver(db, scope, useMessaging ? new MessagingRepository(db) : null);
            return new Fixture
            {
                Db = db, Tenant = scope, Legacy = legacy, Canonical = new CanonicalCurrentAuthorizationTargetResolver(db, scope, legacy),
                TenantEntity = tenant, User = user, TenantMember = tenantMember, Workspace = workspace, WorkspaceMember = member,
                Project = project, Task = task, Artifact = artifact, Message = message, Digest = digest, Notifications = notifications
            };
        }

        public async Task MutateAsync(string mutation)
        {
            switch (mutation)
            {
                case "workspace-archived": Workspace.Status = WorkspaceStatus.Archived; break;
                case "workspace-member-suspended": WorkspaceMember.Status = MembershipStatus.Suspended; break;
                case "tenant-member-suspended": TenantMember.Status = TenantUserStatus.Suspended; break;
                case "user-suspended": User.Status = UserStatus.Suspended; break;
                case "tenant-suspended": TenantEntity.Status = TenantStatus.Suspended; break;
                case "project-planning": Project.Status = ProjectStatus.Planning; break;
                case "project-suspended": Project.Status = ProjectStatus.Suspended; break;
                case "project-archived": Project.Status = ProjectStatus.Archived; break;
                case "task-deleted": Task.MarkDeleted(FixedClock.Now); break;
                case "artifact-deleted": Artifact.MarkDeleted(FixedClock.Now); break;
                case "message-deleted": Message.MarkDeleted(FixedClock.Now); break;
                case "notification-deleted": foreach (var item in Notifications) item.DeletedAt = FixedClock.Now; break;
                case "another-recipient":
                    var recipientSuffix = Guid.NewGuid().ToString("N");
                    var another = new User { DisplayName = "Other recipient", Email = recipientSuffix + "@example.invalid", NormalizedEmail = recipientSuffix.ToUpperInvariant() + "@EXAMPLE.INVALID", PasswordHash = "hash", Status = UserStatus.Active };
                    Db.Users.Add(another);
                    foreach (var item in Notifications) item.UserId = another.Id;
                    break;
                case "missing-target": foreach (var item in Notifications.Take(5)) item.RelatedEntityId = Guid.NewGuid(); break;
                case "null-target": foreach (var item in Notifications.Take(5)) item.RelatedEntityId = null; break;
                case "task-workspace-mismatch":
                    var otherWorkspace = new Workspace { TenantId = TenantId, Name = "Other workspace", Slug = "other-" + Guid.NewGuid().ToString("N"), Status = WorkspaceStatus.Active, CreatedByUserId = UserId };
                    Db.Workspaces.Add(otherWorkspace);
                    Task.WorkspaceId = otherWorkspace.Id;
                    break;
                case "digest-notification-mismatch": Digest.NotificationId = Notifications[5].Id; break;
                case "wrong-tenant-context": Tenant.SetTenant(Guid.NewGuid(), "another-tenant"); return;
            }
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock : IClock
    {
        public static DateTimeOffset Now => new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class UnexpectedTargetQueryResolver : INotificationTargetResolver
    {
        public IQueryable<Guid>? QueryAvailableNotificationIds(Guid tenantId, Guid userId) =>
            throw new InvalidOperationException("A protected target query was requested.");

        public Task<NotificationTargetResolution> ResolveAsync(Guid tenantId, Guid userId, Guid notificationId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("A target was resolved.");

        public Task<bool> CanDeliverCreatedAsync(Guid tenantId, Guid recipientUserId, DurableEventEnvelope envelope,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Delivery was resolved.");

        public Task<bool> CanDeliverReadStateAsync(Guid tenantId, Guid recipientUserId, DurableEventEnvelope envelope,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Read delivery was resolved.");
    }
}
