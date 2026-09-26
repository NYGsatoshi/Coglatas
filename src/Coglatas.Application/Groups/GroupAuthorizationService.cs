using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Groups;

public sealed class GroupAuthorizationService(
    IGroupRepository groups,
    IWorkspaceRepository workspaces,
    IWorkspaceAuthorizationService workspaceAuthorization) : IGroupAuthorizationService
{
    public async Task<bool> CanViewGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
    {
        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        return group is not null &&
            group.Status == GroupStatus.Active &&
            await workspaceAuthorization.CanViewWorkspace(userId, group.WorkspaceId, cancellationToken);
    }

    public async Task<bool> CanManageGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
    {
        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            return false;
        }

        if (await workspaceAuthorization.CanManageWorkspace(userId, group.WorkspaceId, cancellationToken))
        {
            return true;
        }

        var member = await groups.GetMemberAsync(groupId, userId, cancellationToken);
        return member?.Role.CanManage() == true;
    }

    public async Task<bool> CanCreateGroup(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (await workspaceAuthorization.CanManageWorkspace(userId, workspaceId, cancellationToken))
        {
            return true;
        }

        var member = await workspaces.GetMemberAsync(workspaceId, userId, cancellationToken);
        return member is { Status: MembershipStatus.Active, Role: WorkspaceRole.Adviser };
    }
}
