using Coglatas.Application.Admin;
using Coglatas.Application.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Admin;

public sealed partial class AdminServiceTests
{
    /// <summary>Activates an existing suspended account while preserving its identity and role.</summary>
    [Fact]
    public async Task ActivateSuspendedAccountPreservesProfileAndAuditsTheTarget()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        subject.Status = UserStatus.Suspended;
        var identity = (subject.Id, subject.DisplayName, subject.Email, subject.NormalizedEmail, subject.SystemRole, subject.CreatedAt);

        var result = await fixture.Service.ActivateUserAsync(subject.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Active, subject.Status);
        Assert.Null(subject.DeletedAt);
        Assert.Equal(identity, (subject.Id, subject.DisplayName, subject.Email, subject.NormalizedEmail, subject.SystemRole, subject.CreatedAt));
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(((Guid?)fixture.ActorUser.Id, "UserActivated", "User", (Guid?)subject.Id),
            (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId));
        Assert.Empty(fixture.Sessions.Revocations);
        Assert.Empty(fixture.Repository.Invites);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    /// <summary>Retains original deletion time and targets session revocation to the archived account.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveAccountPreservesExistingDeletionAndTargetsSessionRevocation(bool previouslyDeleted)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        var previousDeletion = fixture.Clock.UtcNow.AddDays(-2);
        if (previouslyDeleted) subject.MarkDeleted(previousDeletion);
        var identity = (subject.Id, subject.Email, subject.SystemRole, subject.CreatedAt);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.ArchiveUserAsync(subject.Id, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Archived, subject.Status);
        Assert.Equal(previouslyDeleted ? previousDeletion : fixture.Clock.UtcNow, subject.DeletedAt);
        Assert.Equal(identity, (subject.Id, subject.Email, subject.SystemRole, subject.CreatedAt));
        Assert.Equal((subject.Id, (Guid?)fixture.ActorUser.Id, "UserArchived", (Guid?)null, cancellation.Token),
            Assert.Single(fixture.Sessions.Revocations));
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(((Guid?)fixture.ActorUser.Id, "UserArchived", "User", (Guid?)subject.Id),
            (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId));
        Assert.Equal(UserStatus.Active, fixture.ActorUser.Status);
        Assert.Null(fixture.ActorUser.DeletedAt);
        Assert.Empty(fixture.Repository.Invites);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    /// <summary>Rejects removal of the last active administrator before any observable write effect.</summary>
    [Fact]
    public async Task ArchiveLastSystemAdministratorPreservesTheAccountAndAllWriteEffects()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);

        var result = await fixture.Service.ArchiveUserAsync(fixture.ActorUser.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Cannot archive the last SystemAdmin.", result.Error);
        Assert.Equal(UserStatus.Active, fixture.ActorUser.Status);
        Assert.Equal(SystemRole.SystemAdmin, fixture.ActorUser.SystemRole);
        Assert.Null(fixture.ActorUser.DeletedAt);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Preserves account fields and binds role-change audit, session and authorization effects to one target.</summary>
    [Theory]
    [InlineData(SystemRole.User, SystemRole.SystemAdmin)]
    [InlineData(SystemRole.SystemAdmin, SystemRole.User)]
    public async Task ChangeAccountRoleTargetsAuditSessionRevocationAndCurrentTenantInvalidation(SystemRole before, SystemRole after)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        subject.SystemRole = before;
        var identity = (subject.Id, subject.DisplayName, subject.Email, subject.Status, subject.CreatedAt);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.ChangeSystemRoleAsync(subject.Id, new ChangeSystemRoleRequest(after), cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(after, subject.SystemRole);
        Assert.Equal(after, result.Value!.SystemRole);
        Assert.Equal(subject.Id, result.Value.Id);
        Assert.Equal(identity, (subject.Id, subject.DisplayName, subject.Email, subject.Status, subject.CreatedAt));
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(((Guid?)fixture.ActorUser.Id, "SystemRoleChanged", "User", (Guid?)subject.Id),
            (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId));
        Assert.NotNull(audit.Metadata);
        Assert.Equal(before.ToString(), audit.Metadata["oldRole"]);
        Assert.Equal(after.ToString(), audit.Metadata["newRole"]);
        Assert.Equal((subject.Id, (Guid?)fixture.ActorUser.Id, "SystemRoleChanged", (Guid?)null, cancellation.Token),
            Assert.Single(fixture.Sessions.Revocations));
        Assert.Equal((fixture.Tenant.TenantId, subject.Id, "account", (Guid?)null, "membershipChanged"),
            Assert.Single(fixture.AuthorizationChanges.Items));
        Assert.Empty(fixture.Repository.Invites);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    /// <summary>Rejects unavailable targets while preserving their original fields and all effect queues.</summary>
    [Theory]
    [InlineData("activate", false)]
    [InlineData("activate", true)]
    [InlineData("suspend", false)]
    [InlineData("suspend", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    public async Task AccountCommandsRejectMissingOrDeletedTargetsWithoutWriteEffects(string operation, bool deleted)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        if (deleted) subject.MarkDeleted(fixture.Clock.UtcNow.AddDays(-1));
        else fixture.Repository.Users.Remove(subject.Id);
        var snapshot = (subject.Status, subject.SystemRole, subject.Email, subject.DeletedAt);

        var result = operation switch
        {
            "activate" => await fixture.Service.ActivateUserAsync(subject.Id),
            "suspend" => await fixture.Service.SuspendUserAsync(subject.Id),
            _ => await fixture.Service.ResetPasswordInviteAsync(subject.Id)
        };

        Assert.False(result.IsSuccess);
        Assert.Equal("User not found.", result.Error);
        Assert.Equal(snapshot, (subject.Status, subject.SystemRole, subject.Email, subject.DeletedAt));
        Assert.Empty(fixture.Repository.Invites);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Requires the persisted administrator to remain available for each account command.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("suspended")]
    [InlineData("archived")]
    [InlineData("deleted")]
    public async Task AccountCommandsRejectAnIneligiblePersistedAdministrator(string state)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        switch (state)
        {
            case "missing": fixture.Repository.Users.Remove(fixture.ActorUser.Id); break;
            case "suspended": fixture.ActorUser.Status = UserStatus.Suspended; break;
            case "archived": fixture.ActorUser.Status = UserStatus.Archived; break;
            default: fixture.ActorUser.MarkDeleted(fixture.Clock.UtcNow); break;
        }
        var snapshot = (subject.Status, subject.SystemRole, subject.DeletedAt);

        Result[] results =
        [
            await fixture.Service.ActivateUserAsync(subject.Id),
            await fixture.Service.SuspendUserAsync(subject.Id),
            await fixture.Service.ArchiveUserAsync(subject.Id),
            await fixture.Service.ResetPasswordInviteAsync(subject.Id)
        ];

        foreach (var result in results)
        {
            Assert.False(result.IsSuccess);
            Assert.Equal("SystemAdmin access is required.", result.Error);
        }
        Assert.Equal(snapshot, (subject.Status, subject.SystemRole, subject.DeletedAt));
        Assert.Empty(fixture.Repository.Invites);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Records the current reset-request audit without claiming an implemented delivery flow.</summary>
    [Fact]
    public async Task PasswordResetRequestPreservesTheAccountAndRecordsTheCurrentDeliveryLimitation()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        var snapshot = (subject.Email, subject.Status, subject.SystemRole, subject.DeletedAt);

        var result = await fixture.Service.ResetPasswordInviteAsync(subject.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(snapshot, (subject.Email, subject.Status, subject.SystemRole, subject.DeletedAt));
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(((Guid?)fixture.ActorUser.Id, "PasswordResetInviteRequested", "User", (Guid?)subject.Id),
            (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId));
        Assert.Contains("reset-token delivery is not implemented yet", audit.Summary ?? string.Empty);
        Assert.Empty(fixture.Repository.Invites);
        Assert.Empty(fixture.Sessions.Revocations);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }
}
