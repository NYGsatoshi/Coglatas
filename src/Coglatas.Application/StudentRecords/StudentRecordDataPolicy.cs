using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.StudentRecords;

public static class StudentRecordDataPolicy
{
    public const string PublicDisplayName = "publicDisplayName";
    public const string HomeroomLabel = "homeroomLabel";
    public const string HealthNotes = "healthNotes";
    public const string GuardianContact = "guardianContact";
    public const string Grades = "grades";
    public const string AttendanceStatus = "attendanceStatus";

    private static readonly HashSet<string> PublicFields = new(StringComparer.OrdinalIgnoreCase)
    {
        PublicDisplayName,
        HomeroomLabel
    };

    private static readonly HashSet<string> RestrictedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        HealthNotes,
        GuardianContact,
        Grades,
        AttendanceStatus
    };

    public static DataClassification? Classify(string fieldName)
    {
        if (PublicFields.Contains(fieldName))
        {
            return DataClassification.Public;
        }

        if (RestrictedFields.Contains(fieldName))
        {
            return DataClassification.StudentRecordRestricted;
        }

        return DataClassification.UnknownSensitive;
    }

    public static bool IsKnownRestrictedField(string fieldName)
    {
        return Classify(fieldName) == DataClassification.StudentRecordRestricted;
    }

    public static IReadOnlyList<string> KnownRestrictedFields => RestrictedFields.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static StudentRecordPublicResponse ToPublicResponse(StudentRecord record)
    {
        return new StudentRecordPublicResponse(
            record.Id,
            record.WorkspaceId,
            PublicFields.Contains(PublicDisplayName) ? record.PublicDisplayName : null,
            PublicFields.Contains(HomeroomLabel) ? record.HomeroomLabel : null);
    }

    public static IReadOnlyDictionary<string, string?> ProjectRestrictedFields(StudentRecord record, IEnumerable<string> requestedFields)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in requestedFields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsKnownRestrictedField(field))
            {
                continue;
            }

            values[CanonicalName(field)] = field switch
            {
                var name when name.Equals(HealthNotes, StringComparison.OrdinalIgnoreCase) => record.HealthNotes,
                var name when name.Equals(GuardianContact, StringComparison.OrdinalIgnoreCase) => record.GuardianContact,
                var name when name.Equals(Grades, StringComparison.OrdinalIgnoreCase) => record.Grades,
                var name when name.Equals(AttendanceStatus, StringComparison.OrdinalIgnoreCase) => record.AttendanceStatus?.ToString(),
                _ => null
            };
        }

        return values;
    }

    public static string CanonicalName(string field)
    {
        if (field.Equals(HealthNotes, StringComparison.OrdinalIgnoreCase))
        {
            return HealthNotes;
        }

        if (field.Equals(GuardianContact, StringComparison.OrdinalIgnoreCase))
        {
            return GuardianContact;
        }

        if (field.Equals(Grades, StringComparison.OrdinalIgnoreCase))
        {
            return Grades;
        }

        return AttendanceStatus;
    }
}
