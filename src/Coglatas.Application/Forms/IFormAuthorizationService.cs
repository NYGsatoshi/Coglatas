using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms;

public interface IFormAuthorizationService
{
    Task<bool> CanCreateForm(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default);
    Task<bool> CanViewForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default);
    Task<bool> CanManageForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default);
    Task<bool> CanAccessScope(Guid userId, InternalForm form, CancellationToken cancellationToken = default);
}
