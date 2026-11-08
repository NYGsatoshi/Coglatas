using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Groups;

public sealed partial class GroupServiceUpdateValidationTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("\t", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\t", false)]
    public async Task InvalidGroupNameCannotStageParentForLaterCallerSave(string name, bool replaceParent)
        => await AssertInvalidNameAsync(name, replaceParent, null);

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("self")]
    public async Task ParentDenialPrecedesNameValidationAndLeavesExistingState(string parentKind)
        => await AssertParentDenialAsync(parentKind, null);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DeniedActorCannotStageGroupUpdates(bool authenticated, bool allowed)
        => await AssertActorDenialAsync(authenticated, allowed, null);

    [Fact]
    public async Task MissingGroupAfterAdmissionDoesNotSaveOrAudit()
        => await AssertMissingGroupAsync(null);

    [Theory]
    [InlineData(null, "Subject", "subject")]
    [InlineData(" Renamed ", "Renamed", "renamed")]
    public async Task ValidParentUpdatePreservesMetadataAndSavesOneAudit(
        string? name, string expectedName, string expectedSlug)
        => await AssertValidUpdateAsync(name, expectedName, expectedSlug, null);

    private static async Task AssertInvalidNameAsync(string name, bool replaceParent, string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        var result = await fixture.Service.UpdateAsync(fixture.Subject.Id,
            new UpdateGroupRequest(replaceParent ? fixture.NewParent.Id : null, name,
                "Uncommitted description", null, GroupStatus.Archived));

        Assert.False(result.IsSuccess);
        Assert.Equal("Group name is required.", result.Error);
        Assert.Equal(0, fixture.Unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        await fixture.AssertCallerSavePreservesGroupAsync();
    }

    private static async Task AssertParentDenialAsync(string parentKind, string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        var parent = parentKind switch
        {
            "foreign" => fixture.ForeignParent.Id,
            "self" => fixture.Subject.Id,
            _ => Guid.NewGuid()
        };
        var result = await fixture.Service.UpdateAsync(fixture.Subject.Id,
            new UpdateGroupRequest(parent, "", "Uncommitted description", null, GroupStatus.Archived));

        Assert.False(result.IsSuccess);
        Assert.Equal("Parent group must belong to the same workspace.", result.Error);
        Assert.Equal(0, fixture.Unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        await fixture.AssertCallerSavePreservesGroupAsync();
    }

    private static async Task AssertActorDenialAsync(bool authenticated, bool allowed, string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        fixture.Actor.IsAuthenticated = authenticated;
        fixture.Authorization.Allowed = allowed;
        var result = await fixture.Service.UpdateAsync(fixture.Subject.Id,
            new UpdateGroupRequest(fixture.NewParent.Id, "Renamed", "After", null, GroupStatus.Archived));

        Assert.False(result.IsSuccess);
        Assert.Equal("You are not allowed to manage this group.", result.Error);
        Assert.Equal(0, fixture.Unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        await fixture.AssertCallerSavePreservesGroupAsync();
    }

    private static async Task AssertMissingGroupAsync(string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        var result = await fixture.Service.UpdateAsync(Guid.NewGuid(),
            new UpdateGroupRequest(fixture.NewParent.Id, "Renamed", null, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal("Group not found.", result.Error);
        Assert.Equal(0, fixture.Unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        await fixture.AssertCallerSavePreservesGroupAsync();
    }

    private static async Task AssertValidUpdateAsync(
        string? name, string expectedName, string expectedSlug, string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        var result = await fixture.Service.UpdateAsync(fixture.Subject.Id,
            new UpdateGroupRequest(fixture.NewParent.Id, name, " After ", null, null));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(fixture.Subject.Id, result.Value.Id);
        Assert.Equal(fixture.Workspace.Id, result.Value.WorkspaceId);
        Assert.Equal(fixture.NewParent.Id, result.Value.ParentGroupId);
        Assert.Equal(expectedName, result.Value.Name);
        Assert.Equal("After", result.Value.Description);
        Assert.Equal(fixture.User.Id, result.Value.CreatedByUserId);
        Assert.Equal(1, fixture.Unit.SaveCalls);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("GroupUpdated", audit.Action);
        Assert.Equal(fixture.Subject.Id, audit.EntityId);
        Assert.Equal(fixture.User.Id, audit.ActorUserId);
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.Groups.SingleAsync(group => group.Id == fixture.Subject.Id);
        Assert.Equal(fixture.NewParent.Id, stored.ParentGroupId);
        Assert.Equal(expectedName, stored.Name);
        Assert.Equal(expectedSlug, stored.Slug);
        Assert.Equal("After", stored.Description);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Context { get; }
        public User User { get; }
        public Workspace Workspace { get; }
        private Group OldParent { get; }
        public Group NewParent { get; }
        public Group Subject { get; }
        public Group ForeignParent { get; }
        public Actor Actor { get; }
        public GrantedBoundary Authorization { get; } = new();
        public AuditRecorder Audit { get; } = new();
        public CountingUnitOfWork Unit { get; }
        public GroupService Service { get; }

        private Fixture(AppDbContext context, User user, Workspace workspace,
            Group oldParent, Group newParent, Group subject, Group foreignParent)
        {
            Context = context; User = user; Workspace = workspace;
            OldParent = oldParent; NewParent = newParent; Subject = subject; ForeignParent = foreignParent;
            Actor = new Actor(user.Id);
            Unit = new CountingUnitOfWork(context);
            Service = new GroupService(new GroupRepository(context), new WorkspaceRepository(context),
                new UserRepository(context), Authorization, Actor, new FixedClock(), Audit, Unit);
        }

        public static async Task<Fixture> CreateAsync(string? connection)
        {
            var scope = new CurrentTenantService();
            scope.SetPlatformScope();
            var suffix = Guid.NewGuid().ToString("N");
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (connection is null) options.UseInMemoryDatabase(suffix);
            else options.UseNpgsql(connection);
            var context = new AppDbContext(options.Options, scope);
            if (connection is not null) await context.Database.BeginTransactionAsync();
            var tenant = new Tenant { Name = "Validation", DisplayName = "Validation", Slug = suffix };
            var user = new User { DisplayName = "Actor", Email = suffix + "@example.test",
                NormalizedEmail = suffix.ToUpperInvariant() + "@EXAMPLE.TEST",
                PasswordHash = Guid.NewGuid().ToString("N"), Status = UserStatus.Active };
            context.AddRange(tenant, user);
            await context.SaveChangesAsync();
            scope.SetTenant(tenant.Id, tenant.Slug);
            var workspace = new Workspace { Name = "Before workspace", Slug = "before", CreatedByUserId = user.Id };
            var foreign = new Workspace { Name = "Other workspace", Slug = "other", CreatedByUserId = user.Id };
            context.Workspaces.AddRange(workspace, foreign);
            var oldParent = NewGroup(workspace.Id, user.Id, "Original parent");
            var newParent = NewGroup(workspace.Id, user.Id, "New parent");
            var subject = NewGroup(workspace.Id, user.Id, "Subject");
            subject.Slug = "subject";
            subject.ParentGroupId = oldParent.Id;
            var foreignParent = NewGroup(foreign.Id, user.Id, "Foreign parent");
            context.Groups.AddRange(oldParent, newParent, subject, foreignParent);
            await context.SaveChangesAsync();
            return new Fixture(context, user, workspace, oldParent, newParent, subject, foreignParent);
        }

        private static Group NewGroup(Guid workspaceId, Guid actor, string name) => new()
        {
            WorkspaceId = workspaceId, CreatedByUserId = actor, Name = name,
            Slug = Guid.NewGuid().ToString("N"), Description = "Before",
            GroupType = GroupType.Other, Status = GroupStatus.Active
        };

        public async Task AssertCallerSavePreservesGroupAsync()
        {
            Workspace.Name = "Independent caller update";
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            var stored = await Context.Groups.SingleAsync(group => group.Id == Subject.Id);
            Assert.Equal(OldParent.Id, stored.ParentGroupId);
            Assert.Equal("Subject", stored.Name);
            Assert.Equal("subject", stored.Slug);
            Assert.Equal("Before", stored.Description);
            Assert.Equal(GroupType.Other, stored.GroupType);
            Assert.Equal(GroupStatus.Active, stored.Status);
            Assert.Equal("Independent caller update",
                (await Context.Workspaces.SingleAsync(workspace => workspace.Id == Workspace.Id)).Name);
        }

        public async ValueTask DisposeAsync()
        {
            if (Context.Database.CurrentTransaction is { } transaction)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
            }
            await Context.DisposeAsync();
        }
    }

    private sealed class Actor(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated { get; set; } = true;
    }

    private sealed class GrantedBoundary : IGroupAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanManageGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
        public Task<bool> CanViewGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
        public Task<bool> CanCreateGroup(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class AuditRecorder : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork(AppDbContext context) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return context.SaveChangesAsync(cancellationToken);
        }
    }
}
