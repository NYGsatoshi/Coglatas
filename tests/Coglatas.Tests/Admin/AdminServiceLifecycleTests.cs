using Coglatas.Application.Admin;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Admin;

public sealed partial class AdminServiceTests
{
    /// <summary>Normalizes both administrative lists with the existing bounded pagination contract.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData(-1, -1, 1, 50)]
    [InlineData(0, 0, 1, 50)]
    [InlineData(3, 201, 3, 200)]
    [InlineData(2, 10, 2, 10)]
    public async Task AdministrativeListsPreserveBoundedPagination(int page, int size, int expectedPage, int expectedSize)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);

        var users = await fixture.Service.ListUsersAsync(page, size);
        var invites = await fixture.Service.ListInvitesAsync(page, size);

        Assert.True(users.IsSuccess);
        Assert.True(invites.IsSuccess);
        Assert.Equal((expectedPage, expectedSize), (users.Value!.Page, users.Value.PageSize));
        Assert.Equal((expectedPage, expectedSize), (invites.Value!.Page, invites.Value.PageSize));
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Requires the persisted administrator to remain present, active and undeleted.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData("missing")]
    [InlineData("suspended")]
    [InlineData("archived")]
    [InlineData("deleted")]
    public async Task IneligiblePersistedAdministratorCannotReadOrUpdateSettings(string state)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        switch (state)
        {
            case "missing": fixture.Repository.Users.Remove(fixture.ActorUser.Id); break;
            case "suspended": fixture.ActorUser.Status = UserStatus.Suspended; break;
            case "archived": fixture.ActorUser.Status = UserStatus.Archived; break;
            case "deleted": fixture.ActorUser.MarkDeleted(fixture.Clock.UtcNow); break;
        }

        var listed = await fixture.Service.ListSettingsAsync();
        var updated = await fixture.Service.UpdateSettingAsync("PilotOption", new UpdateSystemSettingRequest("value", null, null, null));

        Assert.False(listed.IsSuccess);
        Assert.False(updated.IsSuccess);
        Assert.Equal("SystemAdmin access is required.", listed.Error);
        Assert.Equal("SystemAdmin access is required.", updated.Error);
        Assert.Empty(fixture.Repository.Settings);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Rejects invalid setting identities before staging any setting or audit.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSettingKeyDoesNotStageWrites(bool overlong)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var key = overlong ? new string('x', 161) : "  ";

        var result = await fixture.Service.UpdateSettingAsync(key, new UpdateSystemSettingRequest("value", null, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal("Setting key is required and must be 160 characters or fewer.", result.Error);
        Assert.Empty(fixture.Repository.Settings);
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Preserves unspecified fields and masks sensitive values in every returned setting projection.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingPatchRetainsUnspecifiedValuesAndAppliesResponseMask(bool sensitive)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var setting = new SystemSetting
        {
            Key = "PilotOption", Value = "synthetic-value", ValueType = "String",
            Description = "Original description", IsSensitive = sensitive
        };
        fixture.Repository.Settings.Add(setting);

        var updated = await fixture.Service.UpdateSettingAsync("  PilotOption  ", new UpdateSystemSettingRequest(null, " ", null, null));
        var fetched = await fixture.Service.GetSettingAsync(" PilotOption ");
        var listed = await fixture.Service.ListSettingsAsync();

        Assert.True(updated.IsSuccess);
        Assert.True(fetched.IsSuccess);
        Assert.True(listed.IsSuccess);
        var expected = sensitive ? "********" : "synthetic-value";
        Assert.Equal(expected, updated.Value!.Value);
        Assert.Equal(expected, fetched.Value!.Value);
        Assert.Equal(expected, Assert.Single(listed.Value!).Value);
        Assert.Equal(("synthetic-value", "String", "Original description", sensitive),
            (setting.Value, setting.ValueType, setting.Description, setting.IsSensitive));
        Assert.Equal(fixture.Clock.UtcNow, setting.UpdatedAt);
        Assert.Equal(fixture.ActorUser.Id, setting.UpdatedByUserId);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(("SystemSettingChanged", "SystemSetting", (Guid?)setting.Id), (audit.Action, audit.EntityType, audit.EntityId));
        Assert.DoesNotContain("synthetic-value", audit.Summary ?? string.Empty);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    /// <summary>Creates a normalized setting with existing defaults and an explicit persisted actor stamp.</summary>
    [Fact]
    [Trait("Scope", "AdminLifecycleCoverage")]
    public async Task SettingCreationNormalizesValuesAndKeepsExplicitSensitivity()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);

        var result = await fixture.Service.UpdateSettingAsync(" PilotOption ", new UpdateSystemSettingRequest(" value ", " Boolean ", " description ", false));

        Assert.True(result.IsSuccess);
        var setting = Assert.Single(fixture.Repository.Settings);
        Assert.Equal(("PilotOption", "value", "Boolean", "description", false),
            (setting.Key, setting.Value, setting.ValueType, setting.Description, setting.IsSensitive));
        Assert.Equal("value", result.Value!.Value);
        Assert.Equal(fixture.Clock.UtcNow, result.Value.UpdatedAt);
        Assert.Equal(fixture.ActorUser.Id, result.Value.UpdatedByUserId);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Single(fixture.Audit.Entries);
    }

    /// <summary>Rejects invalid profile fields without saving or emitting a success audit.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData("blank-name")]
    [InlineData("long-name")]
    [InlineData("invalid-email")]
    [InlineData("duplicate-email")]
    public async Task InvalidProfilePatchDoesNotSaveOrEmitSuccessAudit(string invalid)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        var request = invalid switch
        {
            "blank-name" => new UpdateAdminUserRequest(" ", null, null),
            "long-name" => new UpdateAdminUserRequest(new string('x', 121), null, null),
            "invalid-email" => new UpdateAdminUserRequest(null, "invalid-address", null),
            _ => new UpdateAdminUserRequest(null, fixture.ActorUser.Email, null)
        };

        var result = await fixture.Service.UpdateUserAsync(subject.Id, request);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(("Subject", "subject@example.test", "SUBJECT@EXAMPLE.TEST", UserStatus.Active),
            (subject.DisplayName, subject.Email, subject.NormalizedEmail, subject.Status));
        AssertNoAdministrativeEffects(fixture);
    }

    /// <summary>Normalizes a valid profile patch and returns the existing role without promoting the subject.</summary>
    [Theory]
    [Trait("Scope", "AdminLifecycleCoverage")]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Suspended)]
    public async Task ValidProfilePatchPreservesRoleAndAuditsOnce(UserStatus status)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.UpdateUserAsync(subject.Id, new UpdateAdminUserRequest(" Updated ", " Updated@example.test ", status), cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Updated", "Updated@example.test", "UPDATED@EXAMPLE.TEST", status, SystemRole.User),
            (subject.DisplayName, subject.Email, subject.NormalizedEmail, subject.Status, subject.SystemRole));
        Assert.Equal(subject.Id, result.Value!.Id);
        Assert.Equal(SystemRole.User, result.Value.SystemRole);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(("AdminUserUpdated", "User", (Guid?)subject.Id), (audit.Action, audit.EntityType, audit.EntityId));
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        if (status == UserStatus.Active) Assert.Empty(fixture.Sessions.Revocations);
        else Assert.Equal((subject.Id, (Guid?)fixture.ActorUser.Id, "AdminUserStatusChanged", (Guid?)null, cancellation.Token),
            Assert.Single(fixture.Sessions.Revocations));
    }

    /// <summary>Uses the existing default setting fields when optional creation values are absent.</summary>
    [Fact]
    [Trait("Scope", "AdminLifecycleCoverage")]
    public async Task SettingCreationWithAbsentFieldsKeepsExistingDefaults()
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);

        var result = await fixture.Service.UpdateSettingAsync("PilotOption", new UpdateSystemSettingRequest(null, null, null, null));

        Assert.True(result.IsSuccess);
        var setting = Assert.Single(fixture.Repository.Settings);
        Assert.Equal((string.Empty, "String", (string?)null, false),
            (setting.Value, setting.ValueType, setting.Description, setting.IsSensitive));
        Assert.Equal(string.Empty, result.Value!.Value);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Single(fixture.Audit.Entries);
    }

    /// <summary>Creates an ordinary synthetic subject using the existing administrative repository double.</summary>
    private static User AddOrdinaryAdministrativeSubject(AdminFixture fixture)
    {
        var subject = new User
        {
            DisplayName = "Subject", Email = "subject@example.test", NormalizedEmail = "SUBJECT@EXAMPLE.TEST",
            Status = UserStatus.Active, SystemRole = SystemRole.User
        };
        fixture.Repository.Users.Add(subject.Id, subject);
        return subject;
    }

    /// <summary>Requires denial and read-only controls to leave the existing observable write effects empty.</summary>
    private static void AssertNoAdministrativeEffects(AdminFixture fixture)
    {
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.Sessions.Revocations);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }
}
