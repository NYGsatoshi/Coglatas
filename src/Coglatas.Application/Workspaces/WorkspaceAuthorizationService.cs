using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Workspaces;

public sealed class WorkspaceAuthorizationService(
    IUserRepository users,
    IWorkspaceRepository workspaces,
    ITenantAuthorizationService? tenants = null,
    ICapabilityGrantEvaluator? capabilityGrants = null) : IWorkspaceAuthorizationService
{
    public async Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(userId, workspaceId, cancellationToken);
        var workspace = access.Workspace;
        var member = access.Member;
        if (workspace is null || workspace.DeletedAt.HasValue || workspace.Status == WorkspaceStatus.Deleted)
        {
            return false;
        }

        if (workspace.Status == WorkspaceStatus.Archived)
        {
            // Archived Workspaces are an authorized historical projection.
            // A platform role alone never grants archived access.
            return member is { Status: MembershipStatus.Active };
        }

        if (workspace.Status != WorkspaceStatus.Active)
        {
            return false;
        }

        if (await IsSystemAdmin(userId, cancellationToken))
        {
            return true;
        }

        return member is { Status: MembershipStatus.Active };
    }

    public async Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(userId, workspaceId, cancellationToken);
        var workspace = access.Workspace;
        if (workspace is null ||
            workspace.DeletedAt.HasValue ||
            workspace.Status != WorkspaceStatus.Active)
        {
            return false;
        }

        if (await IsSystemAdmin(userId, cancellationToken))
        {
            return true;
        }

        return access.Member is { Status: MembershipStatus.Active } member && member.Role.CanManage();
    }

    public async Task<bool> CanGovernWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(userId, workspaceId, cancellationToken);
        var workspace = access.Workspace;
        var member = access.Member;
        if (workspace is null || workspace.DeletedAt.HasValue || workspace.Status == WorkspaceStatus.Deleted)
        {
            return false;
        }

        if (workspace.Status == WorkspaceStatus.Archived)
        {
            return member is { Status: MembershipStatus.Active } && member.Role.CanManage();
        }

        if (workspace.Status != WorkspaceStatus.Active)
        {
            return false;
        }

        return await IsSystemAdmin(userId, cancellationToken) ||
               member is { Status: MembershipStatus.Active } && member.Role.CanManage();
    }

    public async Task<bool> CanRestoreWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(userId, workspaceId, cancellationToken);
        var workspace = access.Workspace;
        if (workspace is null ||
            workspace.DeletedAt.HasValue ||
            workspace.Status != WorkspaceStatus.Archived)
        {
            return false;
        }

        return access.Member is
        {
            Status: MembershipStatus.Active,
            Role: WorkspaceRole.Owner
        };
    }

    public async Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(userId, workspaceId, cancellationToken);
        var workspace = access.Workspace;
        if (workspace is null ||
            workspace.DeletedAt.HasValue ||
            workspace.Status != WorkspaceStatus.Active)
        {
            return false;
        }

        if (await IsSystemAdmin(userId, cancellationToken))
        {
            return true;
        }

        return access.Member is { Status: MembershipStatus.Active } member && member.Role.CanContribute();
    }

    public async Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (tenantId == Guid.Empty ||
            tenants is null ||
            user is not { Status: UserStatus.Active, DeletedAt: null })
        {
            return false;
        }

        if (await tenants.CanManageTenantAsync(userId, tenantId, cancellationToken))
        {
            return true;
        }

        return capabilityGrants is not null &&
               await capabilityGrants.HasActiveGrantAsync(
                   userId,
                   tenantId,
                   CapabilityKeys.WorkspaceCreate,
                   CapabilityScopeType.Tenant,
                   tenantId,
                   cancellationToken);
    }

    private async Task<WorkspaceAccess> ResolveAccessAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        // Authorization needs membership and the current parent Workspace state
        // from one command. Keep this separate from the generic membership read:
        // callers such as the Gantt projection must not acquire a tracked
        // Workspace as a side effect and suppress later authoritative reads.
        var member = await workspaces.GetMemberWithWorkspaceAsync(workspaceId, userId, cancellationToken);
        var workspace = member?.Workspace ?? await workspaces.GetByIdAsync(workspaceId, cancellationToken);
        return new WorkspaceAccess(workspace, member);
    }

    private async Task<bool> IsSystemAdmin(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { SystemRole: SystemRole.SystemAdmin, Status: UserStatus.Active, DeletedAt: null };
    }

    private sealed record WorkspaceAccess(Workspace? Workspace, WorkspaceMember? Member);
}
