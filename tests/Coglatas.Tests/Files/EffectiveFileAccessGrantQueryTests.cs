using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Files;

public sealed class EffectiveFileAccessGrantQueryTests
{
    [Fact]
    public async Task RevocationAndMembershipLossRemovePrivateFileFromEveryGrantRead()
    {
        var currentTenant = new CurrentTenantService();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"file-grant-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options, currentTenant);

        var tenant = new Tenant { Name = "File grant tenant", Slug = "file-grant", DisplayName = "File grant tenant" };
        var otherTenant = new Tenant { Name = "Other tenant", Slug = "other-tenant", DisplayName = "Other tenant" };
        var owner = new User { DisplayName = "Owner", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var recipient = new User { DisplayName = "Recipient", Email = "recipient@example.test", NormalizedEmail = "RECIPIENT@EXAMPLE.TEST" };
        currentTenant.SetPlatformScope();
        db.Tenants.AddRange(tenant, otherTenant);
        db.Users.AddRange(owner, recipient);
        await db.SaveChangesAsync();

        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace { Name = "Files", Slug = "files", CreatedByUserId = owner.Id };
        var member = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = recipient.Id,
            Status = MembershipStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        };
        var file = new FileObject
        {
            WorkspaceId = workspace.Id,
            UploadedByUserId = owner.Id,
            OriginalFileName = "private.txt",
            SharingPolicy = FileSharingPolicy.Private
        };
        var attachment = new Attachment
        {
            WorkspaceId = workspace.Id,
            FileObjectId = file.Id,
            OwnerType = AttachmentOwnerType.Workspace,
            OwnerId = workspace.Id,
            OwnerUserId = owner.Id,
            UploadedByUserId = owner.Id,
            FileName = "private.txt"
        };
        var grant = new FileAccessGrant
        {
            WorkspaceId = workspace.Id,
            FileObjectId = file.Id,
            RecipientUserId = recipient.Id,
            GrantedByUserId = owner.Id,
            RecipientKind = FileAccessGrantRecipientKind.WorkspaceMember
        };
        db.TenantUsers.Add(new TenantUser
        {
            UserId = recipient.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(member);
        db.FileObjects.Add(file);
        db.Attachments.Add(attachment);
        db.FileAccessGrants.Add(grant);
        await db.SaveChangesAsync();

        var grants = new FileAccessGrantRepository(db);
        var files = new FileRepository(db);

        async Task AssertAccessAsync(bool expected)
        {
            Assert.Equal(expected, await grants.HasEffectiveGrantAsync(file.Id, recipient.Id));
            var summaries = await grants.GetEffectiveSummariesAsync([file.Id]);
            Assert.Equal(expected, summaries.ContainsKey(file.Id));
            var inventory = await files.ListAccessibleWorkspaceFileObjectsAsync(
                workspace.Id, recipient.Id, canManageSharing: false, 1, 20);
            Assert.Equal(expected, inventory.Items.Any(item => item.FileObjectId == file.Id));
        }

        await AssertAccessAsync(true);
        currentTenant.SetTenant(otherTenant.Id, otherTenant.Slug);
        await AssertAccessAsync(false);
        currentTenant.SetTenant(tenant.Id, tenant.Slug);

        grant.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await AssertAccessAsync(false);

        grant.RevokedAt = null;
        member.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        await AssertAccessAsync(false);
    }
}
