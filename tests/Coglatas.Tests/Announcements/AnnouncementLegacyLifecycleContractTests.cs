using Coglatas.Application.Announcements;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Announcements;

// Exercises the legacy Application service used by AnnouncementsController.
// Strict dependency contracts do not establish PostgreSQL, transport or current persisted authority.
public sealed class AnnouncementLegacyLifecycleContractTests
{
    [Theory]
    [InlineData("list", true, false)]
    [InlineData("list", false, true)]
    [InlineData("create", true, false)]
    [InlineData("create", false, true)]
    [InlineData("get", true, false)]
    [InlineData("get", false, true)]
    [InlineData("read", true, false)]
    [InlineData("read", false, true)]
    public async Task MissingIdentityOrAuthenticationDeniesBeforeAnyRepositoryOrEffects(
        string operation, bool identity, bool authenticated)
    {
        using var fixture = new Fixture(new Actor(identity ? Guid.NewGuid() : null, authenticated));

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Authentication is required.", result.Error);
        Assert.Empty(fixture.Dependencies.Calls);
    }

    [Theory]
    [InlineData(-4, -1, 1, 1)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 20, 3, 20)]
    [InlineData(int.MaxValue, int.MaxValue, 21474837, 100)]
    public async Task ListPreservesScopeAndReadProjectionWhileBoundingOffset(
        int page, int size, int expectedPage, int expectedSize)
    {
        using var fixture = new Fixture();
        var query = new AnnouncementListQuery(page, size, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fixture.ExpectUser();
        fixture.Dependencies.Expect("announcements.ListVisibleAsync",
            Task.FromResult(new PagedResponse<Announcement>([fixture.Announcement], expectedPage, expectedSize, 17)));
        fixture.Dependencies.Expect("announcements.GetReadAnnouncementIdsAsync",
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid> { fixture.Announcement.Id }));

        var result = await fixture.Service.ListAsync(query, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var pageResult = result.Value!;
        Assert.Equal((expectedPage, expectedSize, 17), (pageResult.Page, pageResult.PageSize, pageResult.TotalCount));
        var item = Assert.Single(pageResult.Items);
        Assert.Equal(fixture.Announcement.Id, item.Id);
        Assert.Equal(fixture.Announcement.Title, item.Title);
        Assert.True(item.IsRead);
        var call = fixture.Dependencies.Calls.Single(call => call.Method == "announcements.ListVisibleAsync");
        Assert.Equal(fixture.ActorId, call.Arguments[0]);
        Assert.False(Assert.IsType<bool>(call.Arguments[1]));
        Assert.Equal(query with { Page = expectedPage, PageSize = expectedSize }, call.Arguments[2]);
        Assert.Equal(fixture.Token, call.Arguments[3]);
        Assert.Equal([fixture.Announcement.Id],
            Assert.IsAssignableFrom<IEnumerable<Guid>>(fixture.Dependencies.Calls[^1].Arguments[1]));
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("get", "invisible")]
    [InlineData("get", "missing")]
    [InlineData("get", "deleted")]
    [InlineData("read", "invisible")]
    [InlineData("read", "missing")]
    [InlineData("read", "deleted")]
    public async Task InvisibleMissingOrDeletedAnnouncementCannotExposeDetailsOrStageReads(string operation, string state)
    {
        using var fixture = new Fixture();
        fixture.ExpectUser();
        fixture.Dependencies.Expect("announcements.IsVisibleToUserAsync", Task.FromResult(state != "invisible"),
            [fixture.Announcement.Id, fixture.ActorId, false, fixture.Token]);
        if (state != "invisible") fixture.ExpectAnnouncement(state);

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Announcement not found.", result.Error);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GlobalCreationRequiresActivePersistedAdministratorAndStagesTrimmedContent(bool active, bool administrator)
    {
        using var fixture = new Fixture();
        fixture.ExpectUser(new User
        {
            Id = fixture.ActorId, Status = active ? UserStatus.Active : UserStatus.Suspended,
            SystemRole = administrator ? SystemRole.SystemAdmin : SystemRole.Teacher
        });
        var allowed = active && administrator;
        if (allowed)
        {
            fixture.Dependencies.Expect("announcements.AddAsync", Task.CompletedTask);
            fixture.Dependencies.Expect("audit.LogUserActionAsync", Task.CompletedTask);
            fixture.Dependencies.Expect("audit.LogUserActionAsync", Task.CompletedTask);
            fixture.Dependencies.Expect("announcements.ListTargetUsersAsync", Task.FromResult<IReadOnlyList<AnnouncementTargetUser>>([]));
            fixture.ExpectInvalidation();
            fixture.ExpectSave();
        }

        var result = await fixture.Service.CreateAsync(new CreateAnnouncementRequest(null, null, null,
            "  Council update  ", "  Announcement body  ", AnnouncementPriority.Important,
            IsPinned: true, RequiresReadConfirmation: true), fixture.Token);

        Assert.Equal(allowed, result.IsSuccess);
        if (allowed)
        {
            var added = Assert.IsType<Announcement>(fixture.Dependencies.Calls[1].Arguments[0]);
            Assert.Equal(fixture.Token, fixture.Dependencies.Calls[1].Arguments[1]);
            Assert.Equal((fixture.ActorId, "Council update", "Announcement body", true, true, Fixture.Now),
                (added.AuthorUserId, added.Title, added.Body, added.IsPinned, added.RequiresReadConfirmation, added.PublishedAt));
            Assert.Equal(AnnouncementPriority.Important, added.Priority);
            Assert.Null(added.WorkspaceId);
            Assert.Null(added.GroupId);
            Assert.Null(added.ChannelId);
            Assert.Equal(added.Id, result.Value!.Id);
            Assert.False(result.Value.IsRead);
            var audits = fixture.Dependencies.Calls.Where(call => call.Method == "audit.LogUserActionAsync").ToArray();
            Assert.Equal(["AnnouncementCreated", "AnnouncementPinned"], audits.Select(call => call.Arguments[1]));
            Assert.All(audits, call =>
            {
                Assert.Equal(fixture.ActorId, call.Arguments[0]);
                Assert.Equal("Announcement", call.Arguments[2]);
                Assert.Equal(added.Id, call.Arguments[3]);
                Assert.Equal(fixture.Token, call.Arguments[^1]);
            });
            var invalidation = Assert.Single(fixture.Dependencies.Calls, call => call.Method == "invalidations.AnnouncementChangedAsync");
            Assert.Same(added, invalidation.Arguments[0]);
            Assert.Equal(fixture.ActorId, invalidation.Arguments[1]);
            Assert.Equal("created", invalidation.Arguments[2]);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Guid>>(invalidation.Arguments[3]));
            Assert.Equal(fixture.Token, invalidation.Arguments[4]);
        }
        else
        {
            Assert.Null(result.Value);
            Assert.Equal("Only system admins can create global announcements.", result.Error);
        }
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkReadStagesOnlyTheFirstReadAndInvalidatesOnlyTheReader(bool alreadyRead)
    {
        using var fixture = new Fixture();
        fixture.ExpectUser();
        fixture.Dependencies.Expect("announcements.IsVisibleToUserAsync", Task.FromResult(true));
        fixture.ExpectAnnouncement();
        fixture.Dependencies.Expect("announcements.HasReadAsync", Task.FromResult(alreadyRead),
            [fixture.Announcement.Id, fixture.ActorId, fixture.Token]);
        if (!alreadyRead)
        {
            fixture.Dependencies.Expect("announcements.AddReadAsync", Task.CompletedTask);
            fixture.ExpectAudit("AnnouncementMarkedRead");
            fixture.ExpectInvalidation();
            fixture.ExpectSave();
        }

        var result = await fixture.Service.MarkReadAsync(fixture.Announcement.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        if (!alreadyRead)
        {
            var call = fixture.Dependencies.Calls.Single(call => call.Method == "announcements.AddReadAsync");
            var read = Assert.IsType<AnnouncementRead>(call.Arguments[0]);
            Assert.Equal((fixture.Announcement.Id, fixture.ActorId, Fixture.Now),
                (read.AnnouncementId, read.UserId, read.ReadAt));
            Assert.Equal(fixture.Token, call.Arguments[1]);
            fixture.AssertInvalidationAudience([fixture.ActorId], "readStateChanged");
        }
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("create", "title")]
    [InlineData("create", "body")]
    [InlineData("create", "expiration")]
    [InlineData("update", "title")]
    [InlineData("update", "body")]
    [InlineData("update", "expiration")]
    public async Task InvalidContentOrExpirationDoesNotStageMutationOrEffects(string operation, string invalid)
    {
        using var fixture = new Fixture();
        var title = invalid == "title" ? " \t " : "Changed";
        var body = invalid == "body" ? " \t " : "Changed body";
        var expires = invalid == "expiration" ? Fixture.Now : Fixture.Now.AddDays(1);
        var before = (fixture.Announcement.Title, fixture.Announcement.Body, fixture.Announcement.ExpiresAt);
        if (operation == "update") fixture.ExpectAnnouncement();

        // The public records permit init/with copies after constructor validation.
        // Exercise the service boundary independently of that constructor guard.
        var result = operation == "create"
            ? await fixture.Service.CreateAsync(new CreateAnnouncementRequest(null, null, null, "Valid", "Valid body",
                PublishedAt: Fixture.Now, ExpiresAt: expires) with { Title = title, Body = body }, fixture.Token)
            : await fixture.Service.UpdateAsync(fixture.Announcement.Id,
                new UpdateAnnouncementRequest(PublishedAt: Fixture.Now, ExpiresAt: expires) with { Title = title, Body = body }, fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(invalid switch
        {
            "title" => "Announcement title is required.",
            "body" => "Announcement body is required.",
            _ => "Announcement expiration must be after publication."
        }, result.Error);
        Assert.Equal(before, (fixture.Announcement.Title, fixture.Announcement.Body, fixture.Announcement.ExpiresAt));
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UpdatePreservesUnspecifiedFieldsAndAuditsPinTransitions(bool wasPinned, bool nextPinned)
    {
        using var fixture = new Fixture { Announcement = { IsPinned = wasPinned } };
        fixture.ExpectAnnouncement();
        fixture.ExpectAudit("AnnouncementUpdated");
        if (wasPinned != nextPinned) fixture.ExpectAudit(nextPinned ? "AnnouncementPinned" : "AnnouncementUnpinned");
        fixture.ExpectAudience([]);
        fixture.ExpectInvalidation();
        fixture.ExpectSave();
        fixture.Dependencies.Expect("announcements.HasReadAsync", Task.FromResult(true));
        var originalBody = fixture.Announcement.Body;
        var originalPublication = fixture.Announcement.PublishedAt;

        var result = await fixture.Service.UpdateAsync(fixture.Announcement.Id,
            new UpdateAnnouncementRequest(Title: "  Renamed  ", IsPinned: nextPinned), fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Renamed", result.Value!.Title);
        Assert.Equal(originalBody, result.Value.Body);
        Assert.Equal(originalPublication, result.Value.PublishedAt);
        Assert.Equal(nextPinned, result.Value.IsPinned);
        Assert.True(result.Value.IsRead);
        fixture.AssertInvalidationAudience([], "updated");
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReturnsTheAuthorizedAnnouncementAndTheCurrentReadersOwnReadState(bool read)
    {
        using var fixture = new Fixture
        {
            Announcement = { WorkspaceId = Guid.NewGuid(), GroupId = Guid.NewGuid(), ChannelId = Guid.NewGuid() }
        };
        fixture.ExpectUser();
        fixture.Dependencies.Expect("announcements.IsVisibleToUserAsync", Task.FromResult(true),
            [fixture.Announcement.Id, fixture.ActorId, false, fixture.Token]);
        fixture.ExpectAnnouncement();
        fixture.Dependencies.Expect("announcements.HasReadAsync", Task.FromResult(read),
            [fixture.Announcement.Id, fixture.ActorId, fixture.Token]);

        var result = await fixture.Service.GetAsync(fixture.Announcement.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var detail = result.Value!;
        Assert.Equal((fixture.Announcement.Id, fixture.Announcement.WorkspaceId, fixture.Announcement.GroupId,
            fixture.Announcement.ChannelId, fixture.ActorId),
            (detail.Id, detail.WorkspaceId, detail.GroupId, detail.ChannelId, detail.AuthorUserId));
        Assert.Equal(("Original", "Original body", read, Fixture.Now, Fixture.Now.AddDays(2)),
            (detail.Title, detail.Body, detail.IsRead, detail.PublishedAt, detail.ExpiresAt));
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestConstructorsRejectBlankBodyBeforeServiceInvocation(bool update)
    {
        var error = Assert.Throws<System.Text.Json.JsonException>(() =>
        {
            if (update) _ = new UpdateAnnouncementRequest(Body: " \t ");
            else _ = new CreateAnnouncementRequest(null, null, null, "Valid title", " \t ");
        });

        Assert.Equal("Announcement body is required.", error.Message);
    }

    [Theory]
    [InlineData(0, 0, 501)]
    [InlineData(1, 0, 499)]
    [InlineData(3, 2, 500)]
    [InlineData(3, 2, 501)]
    public async Task ReadStatusAndReminderIncludeOnlyUnreadRecipientsWithBoundedBody(int count, int readCount, int bodyLength)
    {
        using var fixture = new Fixture { Announcement = { Body = new string('x', bodyLength) } };
        var targets = Enumerable.Range(0, count).Select(index => new AnnouncementTargetUser(
            Guid.NewGuid(), "Synthetic " + index, "recipient@example.test", index < readCount)).ToArray();
        fixture.ExpectAnnouncement();
        fixture.ExpectAudience(targets);

        var status = await fixture.Service.GetReadStatusAsync(fixture.Announcement.Id, fixture.Token);

        Assert.True(status.IsSuccess, status.Error);
        Assert.Equal((count, readCount, count - readCount),
            (status.Value!.TargetUserCount, status.Value.ReadCount, status.Value.UnreadCount));
        var expected = targets.Skip(readCount).Select(target => target.UserId).ToArray();
        Assert.Equal(expected, status.Value.UnreadUsers.Select(user => user.UserId));
        fixture.ExpectAnnouncement();
        fixture.ExpectAudience(targets);
        fixture.ExpectAnnouncement();
        fixture.Dependencies.Expect("notifications.CreateManyAsync", Task.FromResult<IReadOnlyList<Guid>>([]));
        fixture.ExpectAudit("AnnouncementUnreadReminderResent");
        fixture.ExpectInvalidation();
        fixture.ExpectSave();

        var result = await fixture.Service.ResendUnreadAsync(fixture.Announcement.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var call = fixture.Dependencies.Calls.Single(call => call.Method == "notifications.CreateManyAsync");
        Assert.Equal(expected, Assert.IsAssignableFrom<IEnumerable<Guid>>(call.Arguments[0]));
        Assert.Equal(NotificationType.Announcement, call.Arguments[1]);
        Assert.Equal("Reminder: Original", call.Arguments[2]);
        Assert.Equal(new string('x', Math.Min(bodyLength, 500)), call.Arguments[3]);
        Assert.Equal("Announcement", call.Arguments[4]);
        Assert.Equal(fixture.Announcement.Id, call.Arguments[5]);
        Assert.Equal(fixture.ActorId, call.Arguments[6]);
        Assert.Equal(fixture.Token, call.Arguments[7]);
        fixture.AssertInvalidationAudience(expected, "resent");
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task DeleteSoftArchivesAnnouncementAndPreservesRecipientInvalidationBeforeSave()
    {
        using var fixture = new Fixture();
        var recipient = Guid.NewGuid();
        fixture.ExpectAnnouncement();
        fixture.ExpectAudience([new AnnouncementTargetUser(recipient, "Synthetic", "recipient@example.test", false)]);
        fixture.ExpectAudit("AnnouncementDeleted");
        fixture.ExpectInvalidation();
        fixture.ExpectSave();

        var result = await fixture.Service.DeleteAsync(fixture.Announcement.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Fixture.Now, fixture.Announcement.DeletedAt);
        Assert.Equal("Original", fixture.Announcement.Title);
        fixture.AssertInvalidationAudience([recipient], "deleted");
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("disappeared")]
    public async Task ReminderOfAnUnavailableAnnouncementDoesNotNotifyAuditInvalidateOrSave(string state)
    {
        using var fixture = new Fixture();
        fixture.ExpectAnnouncement(state == "disappeared" ? "live" : state);
        if (state == "disappeared")
        {
            fixture.ExpectAudience([]);
            fixture.ExpectAnnouncement("missing");
        }

        var result = await fixture.Service.ResendUnreadAsync(fixture.Announcement.Id, fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Announcement not found.", result.Error);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("notification")]
    [InlineData("audit")]
    [InlineData("invalidation")]
    public async Task ReminderDependencyFailurePropagatesBeforeAnyLaterEffectsOrSave(string stage)
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic " + stage + " failure");
        fixture.ExpectAnnouncement();
        fixture.ExpectAudience([new AnnouncementTargetUser(Guid.NewGuid(), "Synthetic", "recipient@example.test", false)]);
        fixture.ExpectAnnouncement();
        fixture.Dependencies.Expect("notifications.CreateManyAsync", stage == "notification"
            ? Task.FromException<IReadOnlyList<Guid>>(failure) : Task.FromResult<IReadOnlyList<Guid>>([]));
        if (stage != "notification") fixture.ExpectAudit("AnnouncementUnreadReminderResent", stage == "audit" ? failure : null);
        if (stage == "invalidation") fixture.ExpectInvalidation(failure);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ResendUnreadAsync(fixture.Announcement.Id, fixture.Token));

        Assert.Same(failure, actual);
        fixture.Dependencies.VerifyComplete();
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        private readonly CancellationTokenSource _cancellation = new();
        public CancellationToken Token => _cancellation.Token;
        public Guid ActorId { get; }
        public Announcement Announcement { get; }
        public AnnouncementContractDependencies Dependencies { get; } = new();
        public IAnnouncementService Service { get; }

        public Fixture(ICurrentUser? actor = null)
        {
            actor ??= new Actor(Guid.NewGuid(), true);
            ActorId = actor.UserId ?? Guid.NewGuid();
            Announcement = new Announcement
            {
                AuthorUserId = ActorId, Title = "Original", Body = "Original body",
                PublishedAt = Now, ExpiresAt = Now.AddDays(2)
            };
            Service = new AnnouncementService(Dependencies.Create<IAnnouncementRepository>("announcements"),
                Dependencies.Create<IWorkspaceRepository>("workspaces"), Dependencies.Create<IGroupRepository>("groups"),
                Dependencies.Create<IChannelRepository>("channels"), Dependencies.Create<IUserRepository>("users"),
                Dependencies.Create<IWorkspaceAuthorizationService>("workspaceAuthority"),
                Dependencies.Create<IGroupAuthorizationService>("groupAuthority"),
                Dependencies.Create<IChannelAuthorizationService>("channelAuthority"), actor, new Clock(),
                Dependencies.Create<IAuditLogger>("audit"), Dependencies.Create<INotificationService>("notifications"),
                Dependencies.Create<IBusinessInvalidationPublisher>("invalidations"), Dependencies.Create<IUnitOfWork>("persistence"));
        }

        public void ExpectUser(User? user = null) => Dependencies.Expect("users.GetByIdAsync", Task.FromResult(user), [ActorId, Token]);

        public void ExpectAnnouncement(string state = "live")
        {
            if (state == "deleted") Announcement.MarkDeleted(Now.AddDays(-1));
            Dependencies.Expect("announcements.GetAsync", Task.FromResult(state == "missing" ? null : Announcement), [Announcement.Id, Token]);
        }

        public void ExpectAudience(IReadOnlyList<AnnouncementTargetUser> targets) =>
            Dependencies.Expect("announcements.ListTargetUsersAsync", Task.FromResult(targets), [Announcement, Token]);

        public void ExpectAudit(string action, Exception? failure = null) => Dependencies.Expect("audit.LogUserActionAsync",
            failure is null ? Task.CompletedTask : Task.FromException(failure),
            [ActorId, action, "Announcement", Announcement.Id, action switch
            {
                "AnnouncementMarkedRead" => "Announcement marked read.",
                "AnnouncementUpdated" => "Announcement updated.",
                "AnnouncementDeleted" => "Announcement deleted.",
                "AnnouncementUnreadReminderResent" => "Unread announcement reminder resent.",
                _ => null
            }, null, Token]);

        public void ExpectInvalidation(Exception? failure = null) => Dependencies.Expect("invalidations.AnnouncementChangedAsync",
            failure is null ? Task.CompletedTask : Task.FromException(failure));

        public void ExpectSave() => Dependencies.Expect("persistence.SaveChangesAsync", Task.FromResult(1), [Token]);

        public void AssertInvalidationAudience(IEnumerable<Guid> expected, string change)
        {
            var call = Assert.Single(Dependencies.Calls, call => call.Method == "invalidations.AnnouncementChangedAsync");
            Assert.Same(Announcement, call.Arguments[0]);
            Assert.Equal(ActorId, call.Arguments[1]);
            Assert.Equal(change, call.Arguments[2]);
            Assert.Equal(expected, Assert.IsAssignableFrom<IEnumerable<Guid>>(call.Arguments[3]));
            Assert.Equal(Token, call.Arguments[4]);
        }

        public async Task<Result> InvokeAsync(string operation)
        {
            return operation switch
            {
                "list" => Status(await Service.ListAsync(new AnnouncementListQuery(), Token)),
                "create" => Status(await Service.CreateAsync(new CreateAnnouncementRequest(null, null, null, "New", "Body"), Token)),
                "get" => Status(await Service.GetAsync(Announcement.Id, Token)),
                "read" => await Service.MarkReadAsync(Announcement.Id, Token),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        private static Result Status<T>(Result<T> result)
        {
            if (!result.IsSuccess) Assert.Null(result.Value);
            return new Result(result.IsSuccess, result.Error);
        }

        public void Dispose() => _cancellation.Dispose();
    }

    private sealed record Actor(Guid? UserId, bool IsAuthenticated) : ICurrentUser
    {
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Fixture.Now;
    }
}
