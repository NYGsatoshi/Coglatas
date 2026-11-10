using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Workspaces;

public sealed partial class Wpc02AWorkspaceAuthorizationTests
{
    [Theory]
    [InlineData(false, "unauthenticated", "AuthenticationRequired")]
    [InlineData(true, "unauthenticated", "AuthenticationRequired")]
    [InlineData(false, "hidden", "NotFound")]
    [InlineData(true, "hidden", "NotFound")]
    [InlineData(false, "missing", "NotFound")]
    [InlineData(true, "missing", "NotFound")]
    [InlineData(false, "deleted", "NotFound")]
    [InlineData(true, "deleted", "NotFound")]
    [InlineData(false, "soft-deleted", "NotFound")]
    [InlineData(true, "soft-deleted", "NotFound")]
    [InlineData(false, "wrong-lifecycle", "InvalidStateTransition")]
    [InlineData(true, "wrong-lifecycle", "InvalidStateTransition")]
    [InlineData(false, "insufficient-role", "CapabilityDenied")]
    [InlineData(true, "insufficient-role", "CapabilityDenied")]
    public async Task LifecycleDenialsPreserveWorkspaceAndProduceNoEffects(
        bool restoring,
        string scenario,
        string expectedCode)
    {
        var fixture = CreateLifecycleFixture(restoring ? WorkspaceStatus.Archived : WorkspaceStatus.Active);
        var actor = fixture.AddUser(SystemRole.User);
        fixture.AddMember(actor.Id, scenario == "insufficient-role" ? WorkspaceRole.Member : WorkspaceRole.Owner);
        fixture.PrepareLifecycleDenial(scenario, actor.Id);
        if (scenario != "missing") fixture.AddLifecycleWorkspaceSentinel();
        var service = fixture.CreateLifecycleService(actor.Id, scenario != "unauthenticated");
        var before = LifecycleSnapshot.Capture(fixture.Workspace);
        var membersBefore = fixture.CaptureLifecycleMembers();
        var workspacesBefore = fixture.CaptureLifecycleWorkspaceRepository();
        if (scenario == "missing") Assert.Empty(workspacesBefore);

        var result = restoring
            ? await service.RestoreAsync(before.Id)
            : await service.ArchiveAsync(before.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorDetail?.Code);
        Assert.Equal(before, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(before.ToScalars(), LifecycleSnapshot.Capture(fixture.Workspace).ToScalars());
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore);
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(membersBefore.SelectMany(member => member.ToScalars()),
            fixture.CaptureLifecycleMembers().SelectMany(member => member.ToScalars()));
        fixture.AssertNoLifecycleEffects();
    }

    [Fact]
    public async Task ArchiveAndRestoreInvalidateOnlyDistinctActiveMembersAndRejectRepeatedCommands()
    {
        var fixture = CreateLifecycleFixture(WorkspaceStatus.Active);
        var actor = fixture.AddUser(SystemRole.User);
        var member = fixture.AddUser(SystemRole.User);
        var pending = fixture.AddUser(SystemRole.User);
        var suspended = fixture.AddUser(SystemRole.User);
        var foreign = fixture.AddUser(SystemRole.User);
        fixture.AddMember(actor.Id, WorkspaceRole.Owner);
        fixture.AddMember(member.Id, WorkspaceRole.Member);
        fixture.AddMember(member.Id, WorkspaceRole.Member);
        fixture.AddMember(pending.Id, WorkspaceRole.Member, MembershipStatus.Pending);
        fixture.AddMember(suspended.Id, WorkspaceRole.Member, MembershipStatus.Suspended);
        fixture.AddForeignLifecycleMember(foreign.Id);
        fixture.AddLifecycleWorkspaceSentinel();
        var service = fixture.CreateLifecycleService(actor.Id);
        var before = LifecycleSnapshot.Capture(fixture.Workspace);
        var membersBefore = fixture.CaptureLifecycleMembers();
        var workspacesBefore = fixture.CaptureLifecycleWorkspaceRepository();
        LifecycleInvalidation[] expectedArchivedChanges =
        [
            new(before.TenantId, actor.Id, "workspace", before.Id, "archived"),
            new(before.TenantId, member.Id, "workspace", before.Id, "archived")
        ];
        LifecycleInvalidation[] expectedBothTransitions =
        [
            ..expectedArchivedChanges,
            new(before.TenantId, actor.Id, "workspace", before.Id, "restored"),
            new(before.TenantId, member.Id, "workspace", before.Id, "restored")
        ];

        var archived = await service.ArchiveAsync(before.Id);

        Assert.True(archived.IsSuccess, archived.Error);
        Assert.Equal(before with { Status = WorkspaceStatus.Archived }, LifecycleSnapshot.Capture(fixture.Workspace));
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore, before with { Status = WorkspaceStatus.Archived });
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(1, fixture.LifecycleSaveCalls);
        Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceArchived", "Workspace", before.Id),
            Assert.Single(fixture.LifecycleAudit));
        Assert.Equal(expectedArchivedChanges.OrderBy(change => change.UserId),
            fixture.LifecycleInvalidations.OrderBy(change => change.UserId));
        Assert.Equal(expectedArchivedChanges.OrderBy(change => change.UserId).SelectMany(change => change.ToScalars()),
            fixture.LifecycleInvalidations.OrderBy(change => change.UserId).SelectMany(change => change.ToScalars()));

        var repeatedArchive = await service.ArchiveAsync(before.Id);

        Assert.False(repeatedArchive.IsSuccess);
        Assert.Equal("InvalidStateTransition", repeatedArchive.ErrorDetail?.Code);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore, before with { Status = WorkspaceStatus.Archived });
        Assert.Equal(before with { Status = WorkspaceStatus.Archived }, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(1, fixture.LifecycleSaveCalls);
        Assert.Single(fixture.LifecycleAudit);
        Assert.Equal(expectedArchivedChanges.OrderBy(change => change.UserId),
            fixture.LifecycleInvalidations.OrderBy(change => change.UserId));

        var restored = await service.RestoreAsync(before.Id);

        Assert.True(restored.IsSuccess, restored.Error);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore);
        Assert.Equal(before, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(2, fixture.LifecycleSaveCalls);
        Assert.Collection(fixture.LifecycleAudit,
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceArchived", "Workspace", before.Id), entry),
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceRestored", "Workspace", before.Id), entry));
        Assert.Equal(expectedBothTransitions.OrderBy(change => change.Change).ThenBy(change => change.UserId),
            fixture.LifecycleInvalidations.OrderBy(change => change.Change).ThenBy(change => change.UserId));

        var repeatedRestore = await service.RestoreAsync(before.Id);

        Assert.False(repeatedRestore.IsSuccess);
        Assert.Equal("InvalidStateTransition", repeatedRestore.ErrorDetail?.Code);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore);
        Assert.Equal(before, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(2, fixture.LifecycleSaveCalls);
        Assert.Collection(fixture.LifecycleAudit,
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceArchived", "Workspace", before.Id), entry),
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceRestored", "Workspace", before.Id), entry));
        Assert.Equal(expectedBothTransitions.OrderBy(change => change.Change).ThenBy(change => change.UserId),
            fixture.LifecycleInvalidations.OrderBy(change => change.Change).ThenBy(change => change.UserId));
    }

    [Theory]
    [InlineData("name", "ValidationFailed", "body.name")]
    [InlineData("description", "ValidationFailed", "body.description")]
    [InlineData("icon", "ValidationFailed", "body.icon")]
    [InlineData("status", "InvalidStateTransition", "body.status")]
    public async Task MetadataValidationDoesNotPartiallyApplyOtherRequestedFields(
        string invalidField,
        string expectedCode,
        string expectedTarget)
    {
        var fixture = CreateLifecycleFixture(WorkspaceStatus.Active);
        var actor = fixture.AddUser(SystemRole.User);
        fixture.AddMember(actor.Id, WorkspaceRole.Owner);
        fixture.AddLifecycleWorkspaceSentinel();
        var service = fixture.CreateLifecycleService(actor.Id);
        var before = LifecycleSnapshot.Capture(fixture.Workspace);
        var membersBefore = fixture.CaptureLifecycleMembers();
        var workspacesBefore = fixture.CaptureLifecycleWorkspaceRepository();
        var request = new UpdateWorkspaceRequest(
            invalidField == "name" ? "invalid\0name" : "Replacement name",
            invalidField == "description" ? "invalid\0description" : "Replacement description",
            invalidField == "icon" ? "invalid\0icon" : "Replacement icon",
            invalidField == "status" ? WorkspaceStatus.Archived : WorkspaceStatus.Active);

        var result = await service.UpdateAsync(before.Id, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorDetail?.Code);
        Assert.Equal(expectedTarget, result.ErrorDetail?.Target);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore);
        Assert.Equal(before, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        fixture.AssertNoLifecycleEffects();
    }

    [Fact]
    public async Task MetadataUpdatesTrimValuesRetainOmittedFieldsAndNeverChangeLifecycle()
    {
        var fixture = CreateLifecycleFixture(WorkspaceStatus.Active);
        var actor = fixture.AddUser(SystemRole.User);
        fixture.AddMember(actor.Id, WorkspaceRole.Admin);
        fixture.AddLifecycleWorkspaceSentinel();
        var service = fixture.CreateLifecycleService(actor.Id);
        var before = LifecycleSnapshot.Capture(fixture.Workspace);
        var membersBefore = fixture.CaptureLifecycleMembers();
        var workspacesBefore = fixture.CaptureLifecycleWorkspaceRepository();
        var expectedResponse = new WorkspaceDetailResponse(before.Id, "Updated workspace", "Updated description", "updated-icon",
            WorkspaceStatus.Active, before.CreatedByUserId, before.CreatedAt, before.UpdatedAt);

        var updated = await service.UpdateAsync(before.Id,
            new UpdateWorkspaceRequest("  Updated workspace  ", "  Updated description  ", "  updated-icon  ", WorkspaceStatus.Active));

        Assert.True(updated.IsSuccess, updated.Error);
        var value = Assert.IsType<WorkspaceDetailResponse>(updated.Value);
        Assert.Equal(expectedResponse, value);
        Assert.Equal("Updated workspace", value.Name);
        Assert.Equal("Updated description", value.Description);
        Assert.Equal("updated-icon", value.Icon);
        Assert.Equal(WorkspaceStatus.Active, value.Status);
        Assert.Equal("Updated workspace", fixture.Workspace.Name);
        Assert.Equal("Updated description", fixture.Workspace.Description);
        Assert.Equal("updated-icon", fixture.Workspace.Icon);
        Assert.Equal($"updated-workspace-{before.Id:N}", fixture.Workspace.Slug);
        Assert.Equal(before.Id, value.Id);
        Assert.Equal(before.CreatedByUserId, value.CreatedByUserId);
        Assert.Equal(before.CreatedAt, value.CreatedAt);
        Assert.Equal(before.UpdatedAt, value.UpdatedAt);
        Assert.Equal(before with
        {
            Name = "Updated workspace",
            Slug = $"updated-workspace-{before.Id:N}",
            Description = "Updated description",
            Icon = "updated-icon"
        }, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(1, fixture.LifecycleSaveCalls);
        Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceUpdated", "Workspace", before.Id),
            Assert.Single(fixture.LifecycleAudit));
        Assert.Empty(fixture.LifecycleInvalidations);

        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore, before with
        {
            Name = "Updated workspace",
            Slug = $"updated-workspace-{before.Id:N}",
            Description = "Updated description",
            Icon = "updated-icon"
        });

        var retained = await service.UpdateAsync(before.Id, new UpdateWorkspaceRequest(null, null, null, null));

        Assert.True(retained.IsSuccess, retained.Error);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore, before with
        {
            Name = "Updated workspace",
            Slug = $"updated-workspace-{before.Id:N}",
            Description = "Updated description",
            Icon = "updated-icon"
        });
        Assert.Equal(expectedResponse, retained.Value);
        Assert.Equal(before with
        {
            Name = "Updated workspace",
            Slug = $"updated-workspace-{before.Id:N}",
            Description = "Updated description",
            Icon = "updated-icon"
        }, LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(2, fixture.LifecycleSaveCalls);
        Assert.Collection(fixture.LifecycleAudit,
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceUpdated", "Workspace", before.Id), entry),
            entry => Assert.Equal(new AuditLogEntry(actor.Id, "WorkspaceUpdated", "Workspace", before.Id), entry));
        Assert.Empty(fixture.LifecycleInvalidations);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LifecycleDependencyFailuresPropagateWithoutClaimingStagedEffectsWereCommitted(
        bool restoring,
        bool failingSave)
    {
        var fixture = CreateLifecycleFixture(restoring ? WorkspaceStatus.Archived : WorkspaceStatus.Active);
        var actor = fixture.AddUser(SystemRole.User);
        fixture.AddMember(actor.Id, WorkspaceRole.Owner);
        var failure = new InvalidOperationException("Synthetic lifecycle dependency failure.");
        fixture.FailLifecycleDependency(failure, failingSave);
        fixture.AddLifecycleWorkspaceSentinel();
        var service = fixture.CreateLifecycleService(actor.Id);
        var before = LifecycleSnapshot.Capture(fixture.Workspace);
        var membersBefore = fixture.CaptureLifecycleMembers();
        var workspacesBefore = fixture.CaptureLifecycleWorkspaceRepository();

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => restoring
            ? service.RestoreAsync(before.Id)
            : service.ArchiveAsync(before.Id));

        Assert.Same(failure, observed);
        fixture.AssertLifecycleWorkspaceRepository(workspacesBefore,
            before with { Status = restoring ? WorkspaceStatus.Active : WorkspaceStatus.Archived });
        Assert.Equal(before with { Status = restoring ? WorkspaceStatus.Active : WorkspaceStatus.Archived },
            LifecycleSnapshot.Capture(fixture.Workspace));
        Assert.Equal(membersBefore, fixture.CaptureLifecycleMembers());
        Assert.Equal(failingSave ? 1 : 0, fixture.LifecycleSaveCalls);
        Assert.Equal(new AuditLogEntry(actor.Id, restoring ? "WorkspaceRestored" : "WorkspaceArchived", "Workspace", before.Id),
            Assert.Single(fixture.LifecycleAudit));
        Assert.Equal(new LifecycleInvalidation(before.TenantId, actor.Id, "workspace", before.Id,
            restoring ? "restored" : "archived"), Assert.Single(fixture.LifecycleInvalidations));
    }

    private static Fixture CreateLifecycleFixture(WorkspaceStatus status)
    {
        var fixture = Fixture.Create(status);
        fixture.Workspace.TenantId = Guid.NewGuid();
        fixture.Workspace.Description = "Original description";
        fixture.Workspace.Icon = "original-icon";
        fixture.Workspace.TimeZone = "UTC";
        return fixture;
    }

    private sealed record LifecycleSnapshot(
        Guid Id, Guid TenantId, string Name, string Slug, string? Description, string? Icon,
        WorkspaceStatus Status, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
        DateTimeOffset? DeletedAt, Guid? DeletedByUserId, string? DeleteReason,
        string? TimeZone, TimeOnly DigestTime, long NotificationVersion)
    {
        public object?[] ToScalars() =>
        [
            Id, TenantId, Name, Slug, Description, Icon, Status, CreatedByUserId, CreatedAt, UpdatedAt,
            DeletedAt, DeletedByUserId, DeleteReason, TimeZone, DigestTime, NotificationVersion
        ];

        public static LifecycleSnapshot Capture(Workspace workspace) => new(
            workspace.Id, workspace.TenantId, workspace.Name, workspace.Slug, workspace.Description, workspace.Icon,
            workspace.Status, workspace.CreatedByUserId, workspace.CreatedAt, workspace.UpdatedAt, workspace.DeletedAt,
            workspace.DeletedByUserId, workspace.DeleteReason,
            workspace.TimeZone, workspace.DefaultTaskDeadlineDigestLocalTime, workspace.TaskNotificationSettingsVersion);
    }

    private sealed record LifecycleInvalidation(Guid TenantId, Guid UserId, string ScopeType, Guid? ScopeId, string Change)
    {
        public object?[] ToScalars() => [TenantId, UserId, ScopeType, ScopeId, Change];
    }

    private sealed record LifecycleMembership(
        Guid Id, Guid TenantId, Guid WorkspaceId, Guid UserId, WorkspaceRole Role, MembershipStatus Status,
        DateTimeOffset? JoinedAt, TimeOnly? DigestTime, long PreferenceVersion,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)
    {
        public object?[] ToScalars() =>
        [
            Id, TenantId, WorkspaceId, UserId, Role, Status, JoinedAt, DigestTime, PreferenceVersion, CreatedAt, UpdatedAt
        ];
    }

    private sealed partial class Fixture
    {
        public void AddLifecycleWorkspaceSentinel()
        {
            var sentinel = new Workspace
            {
                TenantId = Guid.NewGuid(), Name = "Unrelated retained workspace", Slug = "unrelated-retained-workspace",
                Description = "Unrelated retained description", Icon = "unrelated-icon", Status = WorkspaceStatus.Archived,
                CreatedByUserId = Guid.NewGuid(), CreatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), TimeZone = "UTC",
                DefaultTaskDeadlineDigestLocalTime = new TimeOnly(8, 30), TaskNotificationSettingsVersion = 4
            };
            sentinel.MarkDeleted(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
                sentinel.CreatedByUserId, "Unrelated retained deletion marker");
            Workspaces.Items.Add(sentinel.Id, sentinel);
        }

        public IReadOnlyDictionary<Guid, (Workspace Instance, LifecycleSnapshot Snapshot)> CaptureLifecycleWorkspaceRepository() =>
            Workspaces.Items.ToDictionary(row => row.Key,
                row => (Instance: row.Value, Snapshot: LifecycleSnapshot.Capture(row.Value)));

        public void AssertLifecycleWorkspaceRepository(
            IReadOnlyDictionary<Guid, (Workspace Instance, LifecycleSnapshot Snapshot)> before,
            LifecycleSnapshot? selectedExpected = null)
        {
            Assert.Equal(before.Keys.Order(), Workspaces.Items.Keys.Order());
            foreach (var row in before.OrderBy(row => row.Key))
            {
                var stored = Workspaces.Items[row.Key];
                Assert.Same(row.Value.Instance, stored);
                var expected = selectedExpected is { } replacement && row.Key == replacement.Id
                    ? replacement : row.Value.Snapshot;
                Assert.Equal(expected, LifecycleSnapshot.Capture(stored));
                Assert.Equal(expected.ToScalars(), LifecycleSnapshot.Capture(stored).ToScalars());
            }
        }

        private LifecycleAuditRecorder LifecycleAuditRecorder { get; } = new();
        private LifecycleSaveRecorder LifecycleSaveRecorder { get; } = new();
        private LifecyclePublisher LifecyclePublisher { get; } = new();
        public IReadOnlyList<AuditLogEntry> LifecycleAudit => LifecycleAuditRecorder.Entries;
        public IReadOnlyList<LifecycleInvalidation> LifecycleInvalidations => LifecyclePublisher.Entries;
        public int LifecycleSaveCalls => LifecycleSaveRecorder.Calls;

        public WorkspaceService CreateLifecycleService(Guid userId, bool authenticated = true)
        {
            foreach (var member in Workspaces.Members.Where(member => member.WorkspaceId == Workspace.Id))
            {
                member.TenantId = Workspace.TenantId;
            }
            return new WorkspaceService(Workspaces, Users, Authorization,
                new LifecycleCurrentUser(userId, authenticated), new LifecycleClock(),
                LifecycleAuditRecorder, LifecycleSaveRecorder,
                new LifecycleTenant(Workspace.TenantId), LifecyclePublisher);
        }

        public IReadOnlyList<LifecycleMembership> CaptureLifecycleMembers() => Workspaces.Members
            .OrderBy(member => member.Id)
            .Select(member => new LifecycleMembership(member.Id, member.TenantId, member.WorkspaceId, member.UserId,
                member.Role, member.Status, member.JoinedAt, member.TaskDeadlineDigestLocalTime, member.TaskNotificationPreferenceVersion,
                member.CreatedAt, member.UpdatedAt))
            .ToArray();

        public void PrepareLifecycleDenial(string scenario, Guid actorUserId)
        {
            switch (scenario)
            {
                case "hidden":
                    Workspaces.Members.RemoveAll(member => member.UserId == actorUserId);
                    break;
                case "missing":
                    Workspaces.Items.Remove(Workspace.Id);
                    break;
                case "deleted":
                    Workspace.Status = WorkspaceStatus.Deleted;
                    break;
                case "soft-deleted":
                    Workspace.MarkDeleted(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero),
                        actorUserId, "Synthetic retained deletion metadata");
                    break;
                case "wrong-lifecycle":
                    Workspace.Status = Workspace.Status == WorkspaceStatus.Active ? WorkspaceStatus.Archived : WorkspaceStatus.Active;
                    break;
            }
        }

        public void AddForeignLifecycleMember(Guid userId) => Workspaces.Members.Add(new WorkspaceMember
        {
            TenantId = Workspace.TenantId, WorkspaceId = Guid.NewGuid(), UserId = userId,
            Role = WorkspaceRole.Member, Status = MembershipStatus.Active
        });

        public void FailLifecycleDependency(InvalidOperationException failure, bool failingSave)
        {
            if (failingSave) LifecycleSaveRecorder.Failure = failure;
            else LifecyclePublisher.Failure = failure;
        }

        public void AssertNoLifecycleEffects()
        {
            Assert.Equal(0, LifecycleSaveCalls);
            Assert.Empty(LifecycleAudit);
            Assert.Empty(LifecycleInvalidations);
        }
    }

    private sealed class LifecycleCurrentUser(Guid userId, bool authenticated) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string Email => "workspace-lifecycle@example.test";
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => authenticated;
    }

    private sealed class LifecycleClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class LifecycleTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid TenantId => tenantId;
        public bool IsAvailable => true;
        public string TenantSlug => "workspace-lifecycle";
        public bool IsPlatformScope => false;
    }

    private sealed class LifecycleAuditRecorder : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class LifecycleSaveRecorder : IUnitOfWork
    {
        public int Calls { get; private set; }
        public InvalidOperationException? Failure { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is { } failure ? Task.FromException<int>(failure) : Task.FromResult(1);
        }
    }

    private sealed class LifecyclePublisher : IAuthorizationStateChangePublisher
    {
        public List<LifecycleInvalidation> Entries { get; } = [];
        public InvalidOperationException? Failure { get; set; }
        public Task PublishAsync(Guid tenantId, Guid affectedUserId, string scopeType, Guid? scopeId, string change,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(new LifecycleInvalidation(tenantId, affectedUserId, scopeType, scopeId, change));
            return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }
}
