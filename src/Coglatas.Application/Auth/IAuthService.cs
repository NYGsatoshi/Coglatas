using Coglatas.Application.Common;

namespace Coglatas.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<InviteValidationResponse>> ValidateInviteAsync(string token, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> RegisterByInviteAsync(RegisterByInviteRequest request, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
