using System.Text.Json;
using Coglatas.Application.Admin;
using Coglatas.Application.Common;
using Coglatas.Domain.Enums;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Tests.Admin;

public sealed class AdminControllerTests
{
    [Fact]
    public async Task CreateInviteReturnsInviteUrlWithoutSeparateRawToken()
    {
        var controller = new AdminController(new FakeAdminService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Scheme = "https";
        controller.Request.Host = new HostString("portal.example.com");

        var result = await controller.CreateInvite(
            new CreateInviteRequest(Guid.NewGuid(), "new-user@example.com", WorkspaceRole.Member, null),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"inviteUrl\":\"https://portal.example.com/app/register/invite?token=raw-token\"", json);
        Assert.DoesNotContain("inviteToken", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAdminService : IAdminService
    {
        public Task<Result> RestartTaskDeadlineDigestAsync(Guid jobId, RestartTaskDeadlineDigestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<AdminInviteResponse>> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<AdminInviteResponse>.Success(new AdminInviteResponse(
                Guid.NewGuid(),
                request.WorkspaceId,
                request.Email,
                request.Role,
                new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero),
                null,
                null,
                Guid.NewGuid(),
                new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
                "raw-token")));
        }

        public Task<Result<PagedResponse<AdminUserListItemResponse>>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<AdminUserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<AdminUserDetailResponse>> UpdateUserAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> SuspendUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ActivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ResetPasswordInviteAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ArchiveUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<AdminUserDetailResponse>> ChangeSystemRoleAsync(Guid userId, ChangeSystemRoleRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<PagedResponse<AdminInviteResponse>>> ListInvitesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<IReadOnlyList<AdminInviteResponse>>> BulkCreateInvitesAsync(BulkCreateInviteRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> RevokeInviteAsync(Guid inviteId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<IReadOnlyList<SystemSettingResponse>>> ListSettingsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<SystemSettingResponse>> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<SystemSettingResponse>> UpdateSettingAsync(string key, UpdateSystemSettingRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ArchiveGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ArchiveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> ArchiveChannelAsync(Guid channelId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<AdminDashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
