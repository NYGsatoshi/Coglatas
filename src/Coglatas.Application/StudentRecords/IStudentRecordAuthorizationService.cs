using Coglatas.Domain.Entities;

namespace Coglatas.Application.StudentRecords;

public interface IStudentRecordAuthorizationService
{
    Task<bool> CanViewPublicStudentRecordAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<StudentRecordRestrictedAccess> AuthorizeRestrictedStudentRecordAsync(
        Guid userId,
        StudentRecord record,
        IReadOnlyCollection<string> requestedFields,
        CancellationToken cancellationToken = default);
}
