using System.Text.Json;
using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public class FormSubmission : AuditableEntity, ITenantEntity
{
    private readonly List<FormSubmissionValue> values = [];

    protected FormSubmission()
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
    public Guid FormDefinitionId { get; private set; }
    public Guid FormVersionId { get; private set; }
    public Guid? SurveyTargetId { get; private set; }
    public Guid? PublishedSurveySchemaId { get; private set; }
    public Guid? RespondentUserId { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public FormSubmissionStatus Status { get; private set; } = FormSubmissionStatus.Submitted;
    public long VersionNo { get; private set; } = 1;
    public string FieldLockSnapshotJson { get; private set; } = string.Empty;
    public IReadOnlyList<FormSubmissionValue> Values => values.AsReadOnly();

    public static FormSubmission SubmitSurvey(
        FormDefinition definition, SurveyTarget target, PublishedSurveySchema schema,
        IReadOnlyDictionary<string, JsonElement> answers, DateTimeOffset submittedAt)
    {
        if (definition.IsDeleted || definition.Purpose != FormPurpose.Survey
            || target.TenantId != definition.TenantId || schema.TenantId != definition.TenantId
            || target.FormDefinitionId != definition.Id || schema.FormDefinitionId != definition.Id
            || schema.SurveyTargetId != target.Id || target.PublishedSchemaId != schema.Id
            || !target.IsEnabled || submittedAt < schema.PublishedAtUtc)
        {
            throw new ArgumentException("New survey answers must use the target's current published version.");
        }

        if (answers.Count == 0)
            throw new ArgumentException("A survey response needs at least one valid answer.", nameof(answers));

        var fields = schema.Fields.OrderBy(link => link.SortOrder).Select(link =>
            link.FormFieldDefinition ?? throw new ArgumentException("The published fields are required.", nameof(schema)))
            .ToArray();
        var submission = new FormSubmission
        {
            TenantId = definition.TenantId,
            FormDefinitionId = definition.Id,
            FormVersionId = schema.FormVersionId,
            SurveyTargetId = target.Id,
            PublishedSurveySchemaId = schema.Id,
            SubmittedAtUtc = submittedAt.ToUniversalTime(),
            FieldLockSnapshotJson = SurveySchemaSnapshot.WritePart(
                new { mode = "permanent", fieldKeys = fields.Select(field => field.FieldKey).ToArray() })
        };
        submission.AddValues(fields, answers);
        return submission;
    }

    public static FormSubmission RecordOrdinary(
        FormDefinition definition, FormVersion version, Guid? respondentUserId,
        IReadOnlyDictionary<string, JsonElement> answers, string fieldLockSnapshotJson, DateTimeOffset submittedAt)
    {
        if (definition.IsDeleted || definition.Purpose != FormPurpose.Ordinary
            || version.TenantId != definition.TenantId || version.FormDefinitionId != definition.Id
            || respondentUserId == Guid.Empty)
        {
            throw new ArgumentException("An ordinary form response must use its own form version.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fieldLockSnapshotJson);
        using var snapshot = JsonDocument.Parse(fieldLockSnapshotJson);
        if (snapshot.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A field lock snapshot must be an object.", nameof(fieldLockSnapshotJson));

        var submission = new FormSubmission
        {
            TenantId = definition.TenantId,
            FormDefinitionId = definition.Id,
            FormVersionId = version.Id,
            RespondentUserId = respondentUserId,
            SubmittedAtUtc = submittedAt.ToUniversalTime(),
            FieldLockSnapshotJson = fieldLockSnapshotJson
        };
        submission.AddValues(version.Fields, answers);
        return submission;
    }

    public void Void(long expectedVersionNo)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        if (SurveyTargetId is null || Status != FormSubmissionStatus.Submitted)
            throw new InvalidOperationException("Only active survey submissions can be voided.");

        Status = FormSubmissionStatus.Voided;
        VersionNo = nextVersion;
    }

    private void AddValues(IReadOnlyList<FormFieldDefinition> fields, IReadOnlyDictionary<string, JsonElement> answers)
    {
        // Public field identity is case-sensitive independently of the caller's lookup policy.
        var byKey = answers.ToDictionary(answer => answer.Key, answer => answer.Value, StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!byKey.Remove(field.FieldKey, out var value))
            {
                if (field.IsRequired)
                    throw new ArgumentException("A required field has no answer.", nameof(answers));
                continue;
            }

            if (!field.ToSurveyField().Accepts(value))
                throw new ArgumentException("An answer does not match its published field.", nameof(answers));

            values.Add(new FormSubmissionValue(this, field, value));
        }

        if (byKey.Count != 0)
            throw new ArgumentException("An answer refers to an unknown published field.", nameof(answers));
    }
}

public class FormSubmissionValue : AuditableEntity, ITenantEntity
{
    protected FormSubmissionValue()
    {
    }

    internal FormSubmissionValue(FormSubmission submission, FormFieldDefinition field, JsonElement value)
    {
        TenantId = submission.TenantId;
        FormSubmissionId = submission.Id;
        PublishedSurveySchemaId = submission.PublishedSurveySchemaId;
        FormFieldDefinitionId = field.Id;
        ValueJson = value.GetRawText();
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
    public Guid FormSubmissionId { get; private set; }
    public Guid FormFieldDefinitionId { get; private set; }
    public Guid? PublishedSurveySchemaId { get; private set; }
    public string ValueJson { get; private set; } = string.Empty;
}

public sealed class SurveyRespondentIdentity : AuditableEntity, ITenantEntity
{
    private SurveyRespondentIdentity()
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
    public Guid FormSubmissionId { get; private set; }
    public string? EnteredName { get; private set; }
    public Guid? AccountUserId { get; private set; }
    public DateTimeOffset? AccountConsentAtUtc { get; private set; }

    public static SurveyRespondentIdentity? Create(
        FormSubmission submission, SurveyTarget target, EventFeedbackSettings settings, string? enteredName,
        bool accountOptIn, Guid? eligibleAccountUserId, DateTimeOffset consentedAt)
    {
        if (submission.SurveyTargetId != target.Id || submission.TenantId != target.TenantId
            || target.TenantId != settings.TenantId || target.EventId != settings.EventId)
        {
            throw new ArgumentException("Identity policy must belong to the submitted survey's Event.");
        }

        if (settings.NameMode == SurveyNameMode.Anonymous)
        {
            if (enteredName is not null || accountOptIn)
                throw new ArgumentException("Anonymous submissions do not accept a name or account link.");

            return null;
        }

        if (enteredName is not null && string.IsNullOrWhiteSpace(enteredName)
            || settings.NameMode == SurveyNameMode.RequiredName && enteredName is null)
        {
            throw new ArgumentException("The configured name mode requires valid name input.", nameof(enteredName));
        }

        if (accountOptIn && (settings.AccountBinding != SurveyAccountBinding.ExplicitOptIn
            || eligibleAccountUserId is null || eligibleAccountUserId == Guid.Empty))
        {
            throw new ArgumentException("Linking an account requires applicable policy and explicit eligible consent.");
        }

        return enteredName is null && !accountOptIn
            ? null
            : new SurveyRespondentIdentity
            {
                TenantId = submission.TenantId,
                FormSubmissionId = submission.Id,
                EnteredName = enteredName,
                AccountUserId = accountOptIn ? eligibleAccountUserId : null,
                AccountConsentAtUtc = accountOptIn ? consentedAt.ToUniversalTime() : null
            };
    }
}

public sealed class SubmissionIdempotency : AuditableEntity, ITenantEntity
{
    private SubmissionIdempotency()
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
    public Guid SurveyTargetId { get; private set; }
    public Guid FormSubmissionId { get; private set; }
    public string KeyDigest { get; private set; } = string.Empty;
    public string IntentDigest { get; private set; } = string.Empty;
    public string ReceiptJson { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static SubmissionIdempotency Record(
        FormSubmission submission, string keyDigest, string intentDigest, string receiptJson)
    {
        if (submission.SurveyTargetId is not { } targetId || submission.Status != FormSubmissionStatus.Submitted)
            throw new ArgumentException("Only committed survey intent can bind a receipt.", nameof(submission));

        ArgumentException.ThrowIfNullOrWhiteSpace(keyDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptJson);
        return new SubmissionIdempotency
        {
            TenantId = submission.TenantId,
            SurveyTargetId = targetId,
            FormSubmissionId = submission.Id,
            KeyDigest = keyDigest,
            IntentDigest = intentDigest,
            ReceiptJson = receiptJson,
            StatusCode = 201,
            ExpiresAtUtc = submission.SubmittedAtUtc.AddHours(24)
        };
    }

    public bool CanReplayAt(DateTimeOffset now) => now < ExpiresAtUtc;
}

public sealed class SurveyPublicCode : AuditableEntity, ITenantEntity
{
    private SurveyPublicCode()
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
    public Guid? EventId { get; private set; }
    public Guid? SurveyTargetId { get; private set; }
    public string CodeDigest { get; private set; } = string.Empty;
    public string? ProtectedReprintValue { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static SurveyPublicCode ForProgram(EventFeedbackSettings settings, string codeDigest, string? protectedReprintValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeDigest);
        return new SurveyPublicCode
        {
            TenantId = settings.TenantId,
            EventId = settings.EventId,
            CodeDigest = codeDigest,
            ProtectedReprintValue = protectedReprintValue
        };
    }

    public static SurveyPublicCode ForTarget(SurveyTarget target, string codeDigest, string? protectedReprintValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeDigest);
        return new SurveyPublicCode
        {
            TenantId = target.TenantId,
            SurveyTargetId = target.Id,
            CodeDigest = codeDigest,
            ProtectedReprintValue = protectedReprintValue
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAtUtc ??= revokedAt.ToUniversalTime();
        ProtectedReprintValue = null;
    }
}
