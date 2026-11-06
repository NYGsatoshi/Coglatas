using Coglatas.Application.Admin;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Admin;

public sealed partial class AdminServiceTests
{
    /// <summary>Rejects an invalid combined patch before changing the repository-owned user object.</summary>
    [Theory]
    [Trait("Scope", "AdminProfileValidationOrder")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedEmailDoesNotStageAnOtherwiseValidNameOrStatus(bool duplicateEmail)
    {
        var fixture = AdminFixture.Create(SystemRole.SystemAdmin);
        var subject = AddOrdinaryAdministrativeSubject(fixture);
        var email = duplicateEmail ? fixture.ActorUser.Email : "invalid-address";

        var result = await fixture.Service.UpdateUserAsync(subject.Id,
            new UpdateAdminUserRequest("Changed name", email, UserStatus.Suspended));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(duplicateEmail ? "A user with this email already exists." : "A valid email address is required.", result.Error);
        Assert.Equal(("Subject", "subject@example.test", "SUBJECT@EXAMPLE.TEST", UserStatus.Active, SystemRole.User),
            (subject.DisplayName, subject.Email, subject.NormalizedEmail, subject.Status, subject.SystemRole));
        AssertNoAdministrativeEffects(fixture);
    }
}
