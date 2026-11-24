using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class FormDefinition : SoftDeletableEntity, ITenantEntity
{
    private FormDefinition()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid? WorkspaceId { get; private set; }
    public Guid? GroupId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public FormPurpose Purpose { get; private set; }
    public FormStatus Status { get; private set; } = FormStatus.Draft;
    public long VersionNo { get; private set; } = 1;

    public static FormDefinition Create(
        Guid tenantId, Guid createdByUserId, Guid? workspaceId, Guid? groupId, Guid? projectId,
        FormPurpose purpose, string title, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (tenantId == Guid.Empty || createdByUserId == Guid.Empty
            || workspaceId == Guid.Empty || groupId == Guid.Empty || projectId == Guid.Empty
            || new[] { workspaceId, groupId, projectId }.Count(id => id.HasValue) != 1
            || !Enum.IsDefined(purpose))
        {
            throw new ArgumentException("A form needs a tenant, creator, purpose and exactly one owning scope.");
        }

        return new FormDefinition
        {
            TenantId = tenantId,
            CreatedByUserId = createdByUserId,
            WorkspaceId = workspaceId,
            GroupId = groupId,
            ProjectId = projectId,
            Purpose = purpose,
            Title = title,
            Description = description
        };
    }

    public void AdvancePublicationVersion(long expectedVersionNo)
    {
        if (IsDeleted || Status == FormStatus.Archived)
            throw new InvalidOperationException("An archived form cannot publish another version.");

        VersionNo = SurveyVersion.Next(VersionNo, expectedVersionNo);
    }
}

public sealed class FormVersion : AuditableEntity, ITenantEntity
{
    private FormVersion()
    {
    }

    private readonly List<FormFieldDefinition> fields = [];

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid FormDefinitionId { get; private set; }
    public int Number { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; private set; }
    public string? CompatibilityChangeReason { get; private set; }
    public IReadOnlyList<FormFieldDefinition> Fields => fields.AsReadOnly();

    public static FormVersion Publish(
        FormDefinition definition, int number, IReadOnlyList<SurveyField> fields, DateTimeOffset publishedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        var version = new FormVersion
        {
            TenantId = definition.TenantId,
            FormDefinitionId = definition.Id,
            Number = number,
            SnapshotJson = SurveySchemaSnapshot.Write(fields),
            PublishedAtUtc = publishedAt.ToUniversalTime()
        };
        version.fields.AddRange(SurveySchemaSnapshot.ReadPart<SurveyField[]>(version.SnapshotJson).Select((field, order) =>
            FormFieldDefinition.Create(version.TenantId, version.Id, null, field, order)));
        return version;
    }

    public static FormVersion PublishCorrection(
        FormDefinition definition, FormVersion previous, int number, IReadOnlyList<SurveyField> fields,
        DateTimeOffset publishedAt, string changeReason)
    {
        if (definition.Purpose != FormPurpose.Survey || previous.TenantId != definition.TenantId ||
            previous.FormDefinitionId != definition.Id || previous.Number != number - 1 ||
            previous.PublishedAtUtc > publishedAt)
        {
            throw new ArgumentException("A common correction must directly follow a version of its own Survey form.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(changeReason);
        var version = Publish(definition, number, fields, publishedAt);
        FormFieldDefinition.LinkCorrections(previous.Fields, version.fields);
        version.CompatibilityChangeReason = changeReason.Trim();
        return version;
    }
}

public sealed class TargetQuestionSetVersion : AuditableEntity, ITenantEntity
{
    private TargetQuestionSetVersion()
    {
    }

    private readonly List<FormFieldDefinition> fields = [];

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid SurveyTargetId { get; private set; }
    public int Number { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; private set; }
    public string? CompatibilityChangeReason { get; private set; }
    public IReadOnlyList<FormFieldDefinition> Fields => fields.AsReadOnly();

    public static TargetQuestionSetVersion Publish(
        SurveyTarget target, int number, IReadOnlyList<SurveyField> fields, DateTimeOffset publishedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        var version = new TargetQuestionSetVersion
        {
            TenantId = target.TenantId,
            SurveyTargetId = target.Id,
            Number = number,
            SnapshotJson = SurveySchemaSnapshot.Write(fields),
            PublishedAtUtc = publishedAt.ToUniversalTime()
        };
        version.fields.AddRange(SurveySchemaSnapshot.ReadPart<SurveyField[]>(version.SnapshotJson).Select((field, order) =>
            FormFieldDefinition.Create(version.TenantId, null, version.Id, field, order)));
        return version;
    }

    public static TargetQuestionSetVersion PublishCorrection(
        SurveyTarget target, TargetQuestionSetVersion previous, int number, IReadOnlyList<SurveyField> fields,
        DateTimeOffset publishedAt, string changeReason)
    {
        if (previous.TenantId != target.TenantId || previous.SurveyTargetId != target.Id ||
            previous.Number >= number || previous.PublishedAtUtc > publishedAt)
        {
            throw new ArgumentException("A correction must follow a version of the same Survey target.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(changeReason);
        var version = Publish(target, number, fields, publishedAt);
        FormFieldDefinition.LinkCorrections(previous.Fields, version.fields);
        version.CompatibilityChangeReason = changeReason.Trim();
        return version;
    }
}

public sealed class FormFieldDefinition : AuditableEntity, ITenantEntity
{
    private FormFieldDefinition()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid? FormVersionId { get; private set; }
    public Guid? TargetQuestionSetVersionId { get; private set; }
    public string FieldKey { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public SurveyFieldType FieldType { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsRequired { get; private set; }
    public string OptionsJson { get; private set; } = string.Empty;
    public string? ConstraintsJson { get; private set; }
    public Guid? CompatiblePreviousFieldId { get; private set; }
    public Guid? CompatibilityRootFieldId { get; private set; }

    public bool IsCompatibleCorrectionOf(SurveyField field) =>
        HasSameAnswerContract(ToSurveyField(), field);

    internal static FormFieldDefinition Create(
        Guid tenantId, Guid? commonVersionId, Guid? additionalVersionId, SurveyField field, int order) =>
        new()
        {
            TenantId = tenantId,
            FormVersionId = commonVersionId,
            TargetQuestionSetVersionId = additionalVersionId,
            FieldKey = field.FieldKey,
            Label = field.Label,
            FieldType = field.Type,
            SortOrder = order,
            IsRequired = field.Required,
            OptionsJson = SurveySchemaSnapshot.WritePart(field.Options),
            ConstraintsJson = field.Constraints is null ? null : SurveySchemaSnapshot.WritePart(field.Constraints)
        };

    internal static void LinkCorrections(IReadOnlyList<FormFieldDefinition> previous, IReadOnlyList<FormFieldDefinition> next)
    {
        if (previous.Count != next.Count)
        {
            throw new ArgumentException("A correction must keep the published field set.");
        }

        var previousByOrder = previous.OrderBy(field => field.SortOrder).ToArray();
        for (var index = 0; index < next.Count; index++)
        {
            var prior = previousByOrder[index];
            var field = next[index];
            if (!prior.IsCompatibleCorrectionOf(field.ToSurveyField()))
            {
                throw new ArgumentException("A correction must preserve the published answer contract.");
            }
            field.CompatiblePreviousFieldId = prior.Id;
            field.CompatibilityRootFieldId = prior.CompatibilityRootFieldId ?? prior.Id;
        }
    }

    private static bool HasSameAnswerContract(SurveyField previous, SurveyField next) =>
        string.Equals(previous.FieldKey, next.FieldKey, StringComparison.Ordinal) &&
        previous.Type == next.Type && previous.Required == next.Required && previous.Constraints == next.Constraints &&
        previous.Options.Select(option => option.OptionKey).SequenceEqual(
            next.Options.Select(option => option.OptionKey), StringComparer.Ordinal);

    public SurveyField ToSurveyField() =>
        new(FieldKey, Label, FieldType, IsRequired,
            SurveySchemaSnapshot.ReadPart<SurveyOption[]>(OptionsJson),
            ConstraintsJson is null ? null : SurveySchemaSnapshot.ReadPart<SurveyFieldConstraints>(ConstraintsJson));
}
