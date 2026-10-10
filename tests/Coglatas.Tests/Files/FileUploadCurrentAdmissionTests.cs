using Coglatas.Application.Channels;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Groups;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Files;

public sealed class FileUploadCurrentAdmissionTests
{
    [Theory]
    [InlineData("WorkspaceMember", AttachmentOwnerType.Workspace)]
    [InlineData("WorkspaceRole", AttachmentOwnerType.Workspace)]
    [InlineData("WorkspaceLifecycle", AttachmentOwnerType.TaskItem)]
    [InlineData("PlatformRole", AttachmentOwnerType.Workspace)]
    [InlineData("PlatformStatus", AttachmentOwnerType.Workspace)]
    [InlineData("PlatformWithoutMember", AttachmentOwnerType.TaskItem)]
    [InlineData("ProjectMember", AttachmentOwnerType.TaskItem)]
    [InlineData("ProjectLifecycle", AttachmentOwnerType.ArtifactVersion)]
    [InlineData("ProjectMember", AttachmentOwnerType.Comment)]
    [InlineData("ProjectMember", AttachmentOwnerType.ActivityLog)]
    [InlineData("ConversationPost", AttachmentOwnerType.Message)]
    [InlineData("ConversationLock", AttachmentOwnerType.Message)]
    [InlineData("ConversationParentPost", AttachmentOwnerType.Message)]
    [InlineData("ChannelMember", AttachmentOwnerType.Post)]
    [InlineData("ChannelStatus", AttachmentOwnerType.Post)]
    [InlineData("GroupMember", AttachmentOwnerType.Post)]
    [InlineData("GroupManager", AttachmentOwnerType.Post)]
    public async Task CurrentAdmissionUsesPersistedQueriesAcrossConcreteOwnerBranchesWithoutChangingTrackedWrites(
        string mutation, AttachmentOwnerType ownerType)
    {
        var fixture = new Fixture(mutation);
        await fixture.SeedAsync();
        await using var context = fixture.Context();
        var files = new FileRepository(context);
        var authorization = Authorization(context, files);
        var ownerId = fixture.OwnerId(ownerType);
        Assert.True(await authorization.CanUploadAttachment(fixture.UserId, ownerType, ownerId));

        var workspace = await context.Workspaces.SingleAsync();
        workspace.Name = "Caller pending write";
        context.ChangeTracker.DetectChanges();
        var tracked = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray();
        Assert.Contains(workspace, tracked);
        await fixture.SetRevokedAsync(true);

        // Every scenario deliberately keeps the stale admission true. A passing
        // current read therefore proves more than an already-fresh filter denial.
        Assert.True(await authorization.CanUploadAttachment(fixture.UserId, ownerType, ownerId));
        Assert.False(await files.ReadCurrentUploadAdmissionAsync(token =>
            authorization.CanUploadAttachment(fixture.UserId, ownerType, ownerId, token)));
        Assert.Equal(QueryTrackingBehavior.TrackAll, context.ChangeTracker.QueryTrackingBehavior);
        Assert.Equal(tracked, context.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray());
        Assert.Equal("Caller pending write", workspace.Name);
        Assert.Equal(EntityState.Modified, context.Entry(workspace).State);

        await fixture.SetRevokedAsync(false);
        Assert.True(await files.ReadCurrentUploadAdmissionAsync(token =>
            authorization.CanUploadAttachment(fixture.UserId, ownerType, ownerId, token)));
        Assert.Equal(QueryTrackingBehavior.TrackAll, context.ChangeTracker.QueryTrackingBehavior);
        Assert.Equal(tracked, context.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray());
        await context.SaveChangesAsync();
        await using var persisted = fixture.Context();
        Assert.Equal("Caller pending write", (await persisted.Workspaces.SingleAsync()).Name);
    }

    [Theory]
    [InlineData(QueryTrackingBehavior.TrackAll)]
    [InlineData(QueryTrackingBehavior.NoTracking)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution)]
    public async Task CurrentAdmissionRestoresOriginalTrackingAfterSuccessExceptionAndCancellation(QueryTrackingBehavior original)
    {
        var fixture = new Fixture("WorkspaceMember");
        await fixture.SeedAsync();
        await using var context = fixture.Context();
        var files = new FileRepository(context);
        var tracked = await context.Workspaces.SingleAsync();
        tracked.Name = "Pending write survives failed admission";
        context.ChangeTracker.QueryTrackingBehavior = original;
        Assert.True(await files.ReadCurrentUploadAdmissionAsync(async token =>
        {
            Assert.Equal(QueryTrackingBehavior.NoTracking, context.ChangeTracker.QueryTrackingBehavior);
            Assert.NotSame(tracked, await context.Workspaces.SingleAsync(token));
            return true;
        }));
        Assert.Equal(original, context.ChangeTracker.QueryTrackingBehavior);

        var expected = new InvalidOperationException("Current query failed.");
        var observed = await AssertFailedAdmissionAsync(files, (context, tracked, expected));
        Assert.Same(expected, observed);
        Assert.Equal(original, context.ChangeTracker.QueryTrackingBehavior);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await AssertCancelledAdmissionAsync(files, context, cancellation.Token);
        Assert.Equal(original, context.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(tracked, Assert.Single(context.ChangeTracker.Entries<Workspace>()).Entity);
        // This write deliberately runs after the cancelled admission check.
        await context.SaveChangesAsync(CancellationToken.None);
        await using var persisted = fixture.Context();
        Assert.Equal(tracked.Name, (await persisted.Workspaces.SingleAsync(CancellationToken.None)).Name);
    }

    private static Task<InvalidOperationException> AssertFailedAdmissionAsync(FileRepository files,
        (AppDbContext Context, Workspace Tracked, InvalidOperationException Expected) state) =>
        Assert.ThrowsAsync<InvalidOperationException>(() => files.ReadCurrentUploadAdmissionAsync(async token =>
        {
            Assert.NotSame(state.Tracked, await state.Context.Workspaces.SingleAsync(token));
            throw state.Expected;
        }));

    private static Task<OperationCanceledException> AssertCancelledAdmissionAsync(FileRepository files, AppDbContext context,
        CancellationToken cancellation) =>
        Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.ReadCurrentUploadAdmissionAsync(async token =>
        {
            await context.Workspaces.SingleAsync(token);
            return true;
        }, cancellation));

    private static FileAuthorizationService Authorization(AppDbContext context, FileRepository files)
    {
        var workspaceRepository = new WorkspaceRepository(context);
        var workspace = new WorkspaceAuthorizationService(new UserRepository(context), workspaceRepository);
        var groupRepository = new GroupRepository(context);
        var group = new GroupAuthorizationService(groupRepository, workspaceRepository, workspace);
        var project = new ProjectAuthorizationService(new ProjectRepository(context), workspace, group, groupRepository);
        return new FileAuthorizationService(files, project,
            new ConversationAuthorizationService(new MessagingRepository(context), project, workspace),
            new ChannelAuthorizationService(new ChannelRepository(context), groupRepository, group), workspace);
    }

    private sealed class Fixture(string mutation)
    {
        private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"file-current-admission-{Guid.NewGuid():N}").Options;
        private readonly Guid _tenantId = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        private Guid WorkspaceId { get; } = Guid.NewGuid();
        private Guid ProjectId { get; } = Guid.NewGuid();
        private Guid GroupId { get; } = Guid.NewGuid();
        private Guid ChannelId { get; } = Guid.NewGuid();
        private Guid ConversationId { get; } = Guid.NewGuid();
        private Guid ParentConversationId { get; } = Guid.NewGuid();
        private Guid TaskId { get; } = Guid.NewGuid();
        private Guid MessageId { get; } = Guid.NewGuid();
        private Guid PostId { get; } = Guid.NewGuid();
        private Guid ArtifactVersionId { get; } = Guid.NewGuid();
        private Guid CommentId { get; } = Guid.NewGuid();
        private Guid ActivityLogId { get; } = Guid.NewGuid();

        public AppDbContext Context() => new(_options, new Tenant(_tenantId));

        public Guid OwnerId(AttachmentOwnerType ownerType) => ownerType switch
        {
            AttachmentOwnerType.Workspace => WorkspaceId,
            AttachmentOwnerType.TaskItem => TaskId,
            AttachmentOwnerType.Message => MessageId,
            AttachmentOwnerType.Post => PostId,
            AttachmentOwnerType.ArtifactVersion => ArtifactVersionId,
            AttachmentOwnerType.Comment => CommentId,
            AttachmentOwnerType.ActivityLog => ActivityLogId,
            _ => throw new ArgumentOutOfRangeException(nameof(ownerType))
        };

        public async Task SeedAsync()
        {
            await using var context = Context();
            context.Tenants.Add(new Coglatas.Domain.Entities.Tenant
            {
                Id = _tenantId, Name = "Synthetic Tenant", Slug = "synthetic", Status = TenantStatus.Active
            });
            await context.SaveChangesAsync();
            var artifactId = Guid.NewGuid();
            context.AddRange(
                new User { Id = UserId, DisplayName = "Synthetic actor", Email = "current@example.test", NormalizedEmail = "CURRENT@EXAMPLE.TEST", Status = UserStatus.Active },
                new Workspace { Id = WorkspaceId, TenantId = _tenantId, Name = "Synthetic Workspace", Slug = "workspace", CreatedByUserId = UserId },
                new WorkspaceMember { TenantId = _tenantId, WorkspaceId = WorkspaceId, UserId = UserId, Status = MembershipStatus.Active, Role = WorkspaceRole.Member },
                new Group { Id = GroupId, TenantId = _tenantId, WorkspaceId = WorkspaceId, Name = "Synthetic Group", Slug = "group", CreatedByUserId = UserId },
                new GroupMember { TenantId = _tenantId, GroupId = GroupId, UserId = UserId, Role = GroupRole.Member },
                new Channel { Id = ChannelId, TenantId = _tenantId, WorkspaceId = WorkspaceId, GroupId = GroupId, Name = "Synthetic Channel", Slug = "channel", Type = ChannelType.Private },
                new ChannelMember { TenantId = _tenantId, ChannelId = ChannelId, UserId = UserId, Role = ChannelRole.Member },
                new Post { Id = PostId, TenantId = _tenantId, ChannelId = ChannelId, AuthorUserId = UserId, Body = "Synthetic Post" },
                new Project { Id = ProjectId, TenantId = _tenantId, WorkspaceId = WorkspaceId, Name = "Synthetic Project", Slug = "project", OwnerUserId = UserId, CreatedByUserId = UserId, Status = ProjectStatus.Active, ActivationState = ProjectActivationState.Activated },
                new ProjectMember { TenantId = _tenantId, ProjectId = ProjectId, UserId = UserId, Role = ProjectRole.Contributor },
                new TaskItem { Id = TaskId, TenantId = _tenantId, WorkspaceId = WorkspaceId, ProjectId = ProjectId, Title = "Synthetic Task", CreatedByUserId = UserId },
                new Conversation { Id = ConversationId, TenantId = _tenantId, WorkspaceId = WorkspaceId, Type = ConversationType.DirectMessage },
                new ConversationMember { TenantId = _tenantId, ConversationId = ConversationId, UserId = UserId, CanPost = true },
                new Message { Id = MessageId, TenantId = _tenantId, WorkspaceId = WorkspaceId, ConversationId = ConversationId, AuthorUserId = UserId, Body = "Synthetic Message" },
                new Artifact { Id = artifactId, TenantId = _tenantId, ProjectId = ProjectId, Name = "Synthetic Artifact", CreatedByUserId = UserId },
                new ArtifactVersion { Id = ArtifactVersionId, TenantId = _tenantId, ArtifactId = artifactId, CreatedByUserId = UserId },
                new Comment { Id = CommentId, TenantId = _tenantId, WorkspaceId = WorkspaceId, TargetType = CommentTargetType.Project, TargetId = ProjectId, AuthorUserId = UserId, Body = "Synthetic Comment" },
                new ActivityLog { Id = ActivityLogId, TenantId = _tenantId, ProjectId = ProjectId, AuthorUserId = UserId, Body = "Synthetic Activity" });
            await context.SaveChangesAsync();
            if (mutation == "ConversationParentPost")
            {
                context.Conversations.Add(new Conversation
                {
                    Id = ParentConversationId, TenantId = _tenantId, WorkspaceId = WorkspaceId,
                    Type = ConversationType.DirectMessage
                });
                context.ConversationMembers.Add(new ConversationMember
                {
                    TenantId = _tenantId, ConversationId = ParentConversationId, UserId = UserId, CanPost = true
                });
                var child = await context.Conversations.SingleAsync(conversation => conversation.Id == ConversationId);
                child.Type = ConversationType.Thread;
                child.ParentConversationId = ParentConversationId;
                child.RootConversationId = ParentConversationId;
                await context.SaveChangesAsync();
            }
            await SetRevokedAsync(false);
        }

        public async Task SetRevokedAsync(bool revoked)
        {
            await using var context = Context();
            switch (mutation)
            {
                case "WorkspaceMember":
                    (await context.WorkspaceMembers.SingleAsync()).Status = revoked ? MembershipStatus.Suspended : MembershipStatus.Active;
                    break;
                case "WorkspaceRole":
                    (await context.WorkspaceMembers.SingleAsync()).Role = revoked ? WorkspaceRole.ReadOnly : WorkspaceRole.Member;
                    break;
                case "WorkspaceLifecycle":
                    (await context.Workspaces.SingleAsync()).Status = revoked ? WorkspaceStatus.Archived : WorkspaceStatus.Active;
                    break;
                case "PlatformRole":
                case "PlatformStatus":
                    (await context.WorkspaceMembers.SingleAsync()).Role = WorkspaceRole.ReadOnly;
                    var user = await context.Users.SingleAsync();
                    user.SystemRole = mutation == "PlatformRole" && revoked ? SystemRole.User : SystemRole.SystemAdmin;
                    user.Status = mutation == "PlatformStatus" && revoked ? UserStatus.Suspended : UserStatus.Active;
                    break;
                case "PlatformWithoutMember":
                    var member = await context.WorkspaceMembers.SingleOrDefaultAsync();
                    if (member is not null)
                    {
                        context.WorkspaceMembers.Remove(member);
                    }
                    (await context.Users.SingleAsync()).SystemRole = SystemRole.SystemAdmin;
                    (await context.Workspaces.SingleAsync()).Status = revoked ? WorkspaceStatus.Archived : WorkspaceStatus.Active;
                    break;
                case "ProjectMember":
                    (await context.ProjectMembers.SingleAsync()).Role = revoked ? ProjectRole.Viewer : ProjectRole.Contributor;
                    break;
                case "ProjectLifecycle":
                    (await context.Projects.SingleAsync()).Status = revoked ? ProjectStatus.Completed : ProjectStatus.Active;
                    break;
                case "ConversationPost":
                    (await context.ConversationMembers.SingleAsync()).CanPost = !revoked;
                    break;
                case "ConversationLock":
                    (await context.Conversations.SingleAsync()).IsLocked = revoked;
                    break;
                case "ConversationParentPost":
                    (await context.ConversationMembers.SingleAsync(item => item.ConversationId == ParentConversationId)).CanPost = !revoked;
                    break;
                case "ChannelMember":
                    (await context.ChannelMembers.SingleAsync()).Role = revoked ? ChannelRole.ReadOnly : ChannelRole.Member;
                    break;
                case "ChannelStatus":
                    (await context.Channels.SingleAsync()).Status = revoked ? ChannelStatus.Archived : ChannelStatus.Active;
                    break;
                case "GroupMember":
                    (await context.Channels.SingleAsync()).Type = ChannelType.Public;
                    (await context.GroupMembers.SingleAsync()).Role = revoked ? GroupRole.ReadOnly : GroupRole.Member;
                    break;
                case "GroupManager":
                    (await context.ChannelMembers.SingleAsync()).Role = ChannelRole.ReadOnly;
                    (await context.GroupMembers.SingleAsync()).Role = revoked ? GroupRole.Member : GroupRole.Admin;
                    break;
                default:
                    throw new InvalidOperationException("Unknown admission mutation.");
            }

            await context.SaveChangesAsync();
        }
    }

    private sealed record Tenant(Guid TenantId) : ICurrentTenant
    {
        public bool IsAvailable => true;
        public string TenantSlug => "synthetic";
        public bool IsPlatformScope => false;
    }
}
