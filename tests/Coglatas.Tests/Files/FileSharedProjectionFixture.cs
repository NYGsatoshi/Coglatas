using Coglatas.Application.Channels;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Coglatas.Tests.Files;

/// <summary>EF InMemory fixture: query/service evidence only, never PostgreSQL qualification.</summary>
internal sealed class FileSharedProjectionFixture : IAsyncDisposable
{
    private FileSharedProjectionFixture(AppDbContext db, CurrentTenantService tenant)
    {
        Db = db;
        TenantContext = tenant;
        Actor = new ActorContext();
        WorkspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(db), new WorkspaceRepository(db));
        Grants = new FileAccessGrantRepository(db);
        var unused = new DenyOtherOwnerScopes();
        FileAuthorization = new FileAuthorizationService(new FileRepository(db), unused, unused, unused,
            WorkspaceAuthorization, Grants);
        Repository = new FileSharedProjectionRepository(db, tenant);
    }

    public AppDbContext Db { get; }
    public CurrentTenantService TenantContext { get; }
    public ActorContext Actor { get; }
    public Tenant Tenant { get; } = new() { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic-shared" };
    public Tenant OtherTenant { get; } = new() { Name = "Other", DisplayName = "Other", Slug = "other-shared" };
    public Workspace Workspace { get; private set; } = null!;
    public Workspace OtherWorkspace { get; private set; } = null!;
    public User Owner { get; } = UserNamed("owner");
    public User Recipient { get; } = UserNamed("recipient");
    public User SecondRecipient { get; } = UserNamed("second");
    public User Unrelated { get; } = UserNamed("unrelated");
    public User External { get; } = UserNamed("external");
    public WorkspaceAuthorizationService WorkspaceAuthorization { get; }
    public FileAuthorizationService FileAuthorization { get; }
    public FileAccessGrantRepository Grants { get; }
    public FileSharedProjectionRepository Repository { get; }

    public FileSharedProjectionService Service(ICurrentUser? actor = null, IFileSharedProjectionRepository? repository = null, IFileSharingService? sharing = null) =>
        new(repository ?? Repository, FileAuthorization, WorkspaceAuthorization,
            // Only GetListPresentationsAsync is invoked; mutation dependencies must stay unused.
            sharing ?? new FileSharingService(Grants, FileAuthorization, WorkspaceAuthorization, actor ?? Actor,
                TenantContext, new FixedClock(), null!, null!, null!), actor ?? Actor, TenantContext);

    public static async Task<FileSharedProjectionFixture> CreateAsync()
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"shared-projection-{Guid.NewGuid():N}").Options, tenant);
        var fixture = new FileSharedProjectionFixture(db, tenant);
        db.Tenants.AddRange(fixture.Tenant, fixture.OtherTenant);
        db.Users.AddRange(fixture.Owner, fixture.Recipient, fixture.SecondRecipient, fixture.Unrelated, fixture.External);
        await db.SaveChangesAsync();
        tenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        fixture.Workspace = new Workspace { Name = "Synthetic shared", Slug = "shared", CreatedByUserId = fixture.Owner.Id };
        fixture.OtherWorkspace = new Workspace { Name = "Inaccessible", Slug = "inaccessible", CreatedByUserId = fixture.Owner.Id };
        db.Workspaces.AddRange(fixture.Workspace, fixture.OtherWorkspace);
        foreach (var user in new[] { fixture.Owner, fixture.Recipient, fixture.SecondRecipient, fixture.Unrelated, fixture.External })
        {
            db.TenantUsers.Add(new TenantUser { UserId = user.Id, Status = TenantUserStatus.Active });
            if (user != fixture.External)
            {
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = fixture.Workspace.Id, UserId = user.Id,
                    Status = MembershipStatus.Active, Role = WorkspaceRole.Member });
            }
        }
        await db.SaveChangesAsync();
        fixture.Actor.UserId = fixture.Owner.Id;
        return fixture;
    }

    public async Task<Attachment> AddFileAsync(string name, Guid? uploader = null, Guid? owner = null,
        FileSharingPolicy policy = FileSharingPolicy.Private, Guid? fixedId = null)
    {
        var file = new FileObject { WorkspaceId = Workspace.Id, UploadedByUserId = uploader ?? Owner.Id,
            OriginalFileName = name, ContentType = "text/plain", SizeBytes = 17, SharingPolicy = policy };
        if (fixedId.HasValue) file.Id = fixedId.Value;
        var attachment = new Attachment { WorkspaceId = Workspace.Id, FileObjectId = file.Id, FileObject = file,
            OwnerType = AttachmentOwnerType.Workspace, OwnerId = Workspace.Id,
            UploadedByUserId = uploader ?? Owner.Id, OwnerUserId = owner ?? uploader ?? Owner.Id,
            FileName = name, ContentType = "text/plain", ScanStatus = FileScanStatus.Clean };
        Db.FileObjects.Add(file);
        Db.Attachments.Add(attachment);
        await Db.SaveChangesAsync();
        return attachment;
    }

    public async Task<FileAccessGrant> GrantAsync(Attachment file, Guid recipient, Guid? grantor = null,
        DateTimeOffset? at = null, FileAccessGrantRecipientKind kind = FileAccessGrantRecipientKind.WorkspaceMember)
    {
        var grant = new FileAccessGrant { WorkspaceId = Workspace.Id, FileObjectId = file.FileObjectId,
            RecipientUserId = recipient, GrantedByUserId = grantor ?? Owner.Id, RecipientKind = kind,
            CreatedAt = at ?? new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero) };
        Db.FileAccessGrants.Add(grant);
        await Db.SaveChangesAsync();
        return grant;
    }

    public async Task SetRoleAsync(Guid user, WorkspaceRole role)
    {
        var member = await Db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == Workspace.Id && m.UserId == user);
        member.Role = role;
        await Db.SaveChangesAsync();
    }

    public AppDbContext IndependentContext() => new((DbContextOptions<AppDbContext>)Db.GetService<IDbContextOptions>(), TenantContext);

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    private static User UserNamed(string name) => new() { DisplayName = name,
        Email = $"{name}@example.test", NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST", Status = UserStatus.Active };

    internal sealed class ActorContext : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => global::Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated { get; set; } = true;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class DenyOtherOwnerScopes : IProjectAuthorizationService, IConversationAuthorizationService, IChannelAuthorizationService
    {
        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanViewConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanSendMessage(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanManageConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanModerateConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanCreateThread(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanViewChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPostToChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanManageChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPinPost(Guid userId, Guid postId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
