using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.StudentRecords;

public sealed record StudentRecordSchoolAccessContext(
    SchoolRole Role,
    bool IsSelf = false,
    bool HasGuardianRelationship = false,
    bool HasTeacherScope = false,
    bool HasHomeroomScope = false,
    bool HasGradeScope = false,
    bool HasStudentAdminScope = false,
    bool HasSchoolAdminScope = false);

public sealed record StudentRecordRestrictedAccess(
    bool IsAuthorizedForRecord,
    SchoolRole? Role,
    IReadOnlyCollection<string> AllowedFields,
    string Reason);

public interface IStudentRecordSchoolAccessContextProvider
{
    Task<StudentRecordSchoolAccessContext?> GetAccessContextAsync(
        Guid userId,
        StudentRecord record,
        CancellationToken cancellationToken = default);
}

public static class StudentRecordFieldAccessPolicy
{
    private static readonly IReadOnlyDictionary<SchoolRole, HashSet<string>> AllowedFieldsByRole =
        new Dictionary<SchoolRole, HashSet<string>>
        {
            [SchoolRole.Student] = Set(StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.Guardian] = Set(StudentRecordDataPolicy.GuardianContact, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.Teacher] = Set(StudentRecordDataPolicy.Grades, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.HomeroomTeacher] = Set(StudentRecordDataPolicy.HealthNotes, StudentRecordDataPolicy.Grades, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.GradeTeacher] = Set(StudentRecordDataPolicy.Grades, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.StudentAdmin] = Set(StudentRecordDataPolicy.GuardianContact, StudentRecordDataPolicy.Grades, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.SchoolAdmin] = Set(StudentRecordDataPolicy.HealthNotes, StudentRecordDataPolicy.GuardianContact, StudentRecordDataPolicy.Grades, StudentRecordDataPolicy.AttendanceStatus),
            [SchoolRole.ExternalGuest] = Set()
        };

    public static StudentRecordRestrictedAccess Authorize(
        StudentRecordSchoolAccessContext? context,
        IReadOnlyCollection<string> requestedFields)
    {
        if (context is null)
        {
            return Deny(null, "missing-school-role");
        }

        if (!HasRequiredBoundary(context))
        {
            return Deny(context.Role, "missing-relationship-or-scope");
        }

        var requestedRestrictedFields = requestedFields
            .Where(StudentRecordDataPolicy.IsKnownRestrictedField)
            .Select(StudentRecordDataPolicy.CanonicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!AllowedFieldsByRole.TryGetValue(context.Role, out var policy))
        {
            return new StudentRecordRestrictedAccess(true, context.Role, [], "missing-field-access-policy");
        }

        var allowedFields = requestedRestrictedFields
            .Where(policy.Contains)
            .ToArray();

        return new StudentRecordRestrictedAccess(true, context.Role, allowedFields, "allowed-by-field-access-policy");
    }

    private static bool HasRequiredBoundary(StudentRecordSchoolAccessContext context)
    {
        return context.Role switch
        {
            SchoolRole.Student => context.IsSelf,
            SchoolRole.Guardian => context.HasGuardianRelationship,
            SchoolRole.Teacher => context.HasTeacherScope,
            SchoolRole.HomeroomTeacher => context.HasHomeroomScope,
            SchoolRole.GradeTeacher => context.HasGradeScope,
            SchoolRole.StudentAdmin => context.HasStudentAdminScope,
            SchoolRole.SchoolAdmin => context.HasSchoolAdminScope,
            SchoolRole.ExternalGuest => false,
            _ => false
        };
    }

    private static StudentRecordRestrictedAccess Deny(SchoolRole? role, string reason)
    {
        return new StudentRecordRestrictedAccess(false, role, [], reason);
    }

    private static HashSet<string> Set(params string[] fields)
    {
        return new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
    }
}
