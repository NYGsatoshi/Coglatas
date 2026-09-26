using Coglatas.Domain.Enums;

namespace Coglatas.Application.Auth;

public sealed record CurrentUserResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    SystemRole SystemRole,
    UserStatus Status,
    IReadOnlyList<string> Capabilities,
    AuthWorkspaceSummary? CurrentWorkspace,
    IReadOnlyList<AuthWorkspaceSummary> Workspaces);

public sealed record AuthWorkspaceSummary(
    Guid Id,
    string Name,
    string? Description,
    WorkspaceStatus Status);
