using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.StudentRecords;

public sealed class StudentRecordAuthorizationService(
    IWorkspaceRepository workspaces,
    IStudentRecordSchoolAccessContextProvider schoolAccessContext) : IStudentRecordAuthorizationService
{
    public async Task<bool> CanViewPublicStudentRecordAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var member = await workspaces.GetMemberAsync(workspaceId, userId, cancellationToken);
        return member is { Status: MembershipStatus.Active };
    }

    public async Task<StudentRecordRestrictedAccess> AuthorizeRestrictedStudentRecordAsync(
        Guid userId,
        StudentRecord record,
        IReadOnlyCollection<string> requestedFields,
        CancellationToken cancellationToken = default)
    {
        var context = await schoolAccessContext.GetAccessContextAsync(userId, record, cancellationToken);
        return StudentRecordFieldAccessPolicy.Authorize(context, requestedFields);
    }
}

public sealed class WorkspaceSchoolAccessContextProvider(IWorkspaceRepository workspaces) : IStudentRecordSchoolAccessContextProvider
{
    public async Task<StudentRecordSchoolAccessContext?> GetAccessContextAsync(
        Guid userId,
        StudentRecord record,
        CancellationToken cancellationToken = default)
    {
        var member = await workspaces.GetMemberAsync(record.WorkspaceId, userId, cancellationToken);
        if (member is not { Status: MembershipStatus.Active })
        {
            return null;
        }

        return member.Role switch
        {
            WorkspaceRole.Owner or WorkspaceRole.Admin => new StudentRecordSchoolAccessContext(
                SchoolRole.SchoolAdmin,
                HasSchoolAdminScope: true),
            WorkspaceRole.Adviser => new StudentRecordSchoolAccessContext(
                SchoolRole.Teacher,
                HasTeacherScope: true),
            _ => new StudentRecordSchoolAccessContext(SchoolRole.ExternalGuest)
        };
    }
}
