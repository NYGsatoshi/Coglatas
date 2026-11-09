using Coglatas.Application.Admin;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Security;

namespace Coglatas.Tests.Admin;

public sealed partial class AdminServiceTests
{
    /// <summary>Rejects invalid input before an earlier valid invitation can remain staged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkInviteValidationFailureLeavesNoStagedInvitations(bool invalidLast)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        string[] emails = invalidLast
            ? ["first@example.invalid", "invalid"]
            : ["invalid", "first@example.invalid"];

        var result = await fixture.Service.BulkCreateInvitesAsync(new BulkCreateInviteRequest(
            Guid.NewGuid(), emails, WorkspaceRole.Member, null));

        Assert.False(result.IsSuccess);
        Assert.Equal("A valid email address is required.", result.Error);
        Assert.Equal(0, fixture.Repository.Invites.Count);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Preserves distinct valid recipients, scoped stored hashes and the existing single-save contract.</summary>
    [Fact]
    public async Task ValidBulkInvitesPreserveRecipientsScopeHashesAndSingleAuditSave()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var workspaceId = Guid.NewGuid();
        var expiresAt = fixture.Clock.UtcNow.AddDays(1);

        var result = await fixture.Service.BulkCreateInvitesAsync(new BulkCreateInviteRequest(
            workspaceId, ["first@example.invalid", "FIRST@example.invalid", "second@example.invalid"],
            WorkspaceRole.Member, expiresAt));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(2, fixture.Repository.Invites.Count);
        Assert.Equal(new[] { "FIRST@EXAMPLE.INVALID", "SECOND@EXAMPLE.INVALID" },
            fixture.Repository.Invites.Select(invite => invite.NormalizedEmail).Order().ToArray());
        foreach (var stored in fixture.Repository.Invites)
        {
            var response = Assert.Single(result.Value.Where(invite => invite.Id == stored.Id));
            Assert.Equal((fixture.Repository.WorkspaceTenantId, workspaceId, WorkspaceRole.Member, expiresAt, fixture.ActorUser.Id),
                (stored.TenantId, stored.WorkspaceId, stored.Role, stored.ExpiresAt, stored.InvitedByUserId));
            Assert.False(string.IsNullOrWhiteSpace(response.InviteToken));
            Assert.NotEqual(response.InviteToken, stored.TokenHash);
            Assert.Equal(new Sha256TokenHasher().HashToken(response.InviteToken!), stored.TokenHash);
            Assert.Equal((stored.Id, stored.WorkspaceId, stored.Email, stored.Role, stored.ExpiresAt),
                (response.Id, response.WorkspaceId, response.Email, response.Role, response.ExpiresAt));
        }
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(((Guid?)fixture.ActorUser.Id, "BulkInviteCreated", "Invite", (Guid?)null),
            (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId));
        Assert.Equal("2 invites created.", audit.Summary);
        Assert.Empty(fixture.Sessions.Revocations);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }
}
