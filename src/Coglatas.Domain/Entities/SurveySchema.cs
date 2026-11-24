using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed record SurveyOption(string OptionKey, string Label);

public sealed record SurveyFieldConstraints(
    int? MinLength = null,
    int? MaxLength = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    decimal? Step = null,
    DateOnly? MinimumDate = null,
    DateOnly? MaximumDate = null,
    int? MinChoices = null,
    int? MaxChoices = null);

public sealed record SurveyField(
    string FieldKey,
    string Label,
    SurveyFieldType Type,
    bool Required,
    IReadOnlyList<SurveyOption> Options,
    SurveyFieldConstraints? Constraints = null)
{
    public bool Accepts(JsonElement value) =>
        Type switch
        {
            SurveyFieldType.Text => value.ValueKind == JsonValueKind.String && AcceptsText(value.GetString()!),
            SurveyFieldType.Number or SurveyFieldType.Scale =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && AcceptsNumber(number),
            SurveyFieldType.Date => value.ValueKind == JsonValueKind.String && AcceptsDate(value.GetString()!),
            SurveyFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SurveyFieldType.SingleChoice => AcceptsOption(value),
            SurveyFieldType.MultipleChoice => value.ValueKind == JsonValueKind.Array && AcceptsChoices(value),
            _ => false
        };

    internal void ValidateDefinition()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FieldKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        ArgumentNullException.ThrowIfNull(Options);
        var isChoice = Type is SurveyFieldType.SingleChoice or SurveyFieldType.MultipleChoice;
        if (!Enum.IsDefined(Type) || isChoice != (Options.Count > 0)
            || Options.Any(option => string.IsNullOrWhiteSpace(option.OptionKey) || string.IsNullOrWhiteSpace(option.Label))
            || Options.Select(option => option.OptionKey).Distinct(StringComparer.Ordinal).Count() != Options.Count)
        {
            throw new ArgumentException("The field type and its stable option keys are invalid.");
        }

        if (Constraints is not { } rule)
        {
            if (Type == SurveyFieldType.Scale)
                throw new ArgumentException("A scale needs published bounds and a step.");
            return;
        }

        var text = rule.MinLength.HasValue || rule.MaxLength.HasValue;
        var number = rule.Minimum.HasValue || rule.Maximum.HasValue || rule.Step.HasValue;
        var date = rule.MinimumDate.HasValue || rule.MaximumDate.HasValue;
        var multiple = rule.MinChoices.HasValue || rule.MaxChoices.HasValue;
        if (text && Type != SurveyFieldType.Text || number && Type is not (SurveyFieldType.Number or SurveyFieldType.Scale)
            || date && Type != SurveyFieldType.Date || multiple && Type != SurveyFieldType.MultipleChoice
            || rule.MinLength < 0 || rule.MaxLength < 0 || rule.MinLength > rule.MaxLength
            || rule.Minimum > rule.Maximum || rule.Step <= 0 || rule.MinimumDate > rule.MaximumDate
            || rule.MinChoices < 0 || rule.MaxChoices <= 0 || rule.MinChoices > rule.MaxChoices
            || rule.MinChoices > Options.Count || rule.MaxChoices > Options.Count
            || Type == SurveyFieldType.Scale && (rule.Minimum is null || rule.Maximum is null || rule.Step is null))
        {
            throw new ArgumentException("The field has invalid or inapplicable constraints.");
        }
    }

    private bool AcceptsText(string value) =>
        (!Required || !string.IsNullOrWhiteSpace(value))
        && !(value.Length < Constraints?.MinLength || value.Length > Constraints?.MaxLength);

    private bool AcceptsNumber(decimal value)
    {
        if (value < Constraints?.Minimum || value > Constraints?.Maximum)
            return false;

        if (Constraints?.Step is not { } step)
            return true;

        var origin = Constraints.Minimum ?? 0;
        var remainder = value % step;
        var originRemainder = origin % step;
        // Compare residues without subtracting the two potentially extreme inputs.
        return remainder == originRemainder
            || remainder < 0 && remainder + step == originRemainder
            || originRemainder < 0 && originRemainder + step == remainder;
    }

    private bool AcceptsDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && !(date < Constraints?.MinimumDate || date > Constraints?.MaximumDate);

    private bool AcceptsOption(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Options.Any(option => option.OptionKey == value.GetString());

    private bool AcceptsChoices(JsonElement value)
    {
        var count = value.GetArrayLength();
        if ((Required && count == 0) || count < Constraints?.MinChoices || count > Constraints?.MaxChoices
            || count > Options.Count)
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateArray().All(item => AcceptsOption(item) && keys.Add(item.GetString()!));
    }
}

internal static class SurveySchemaSnapshot
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<SurveyFieldType>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string Write(IReadOnlyList<SurveyField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            field.ValidateDefinition();
            if (!keys.Add(field.FieldKey))
                throw new ArgumentException("A published version cannot repeat a field key.");
        }

        return WritePart(fields);
    }

    internal static T ReadPart<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException("A published snapshot is invalid.");

    internal static string WritePart<T>(T value) => JsonSerializer.Serialize(value, Options);
}

public sealed class PublishedSurveySchema : AuditableEntity, ITenantEntity
{
    private PublishedSurveySchema()
    {
    }

    private readonly List<PublishedSurveySchemaField> fields = [];

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
    public Guid FormDefinitionId { get; private set; }
    public Guid FormVersionId { get; private set; }
    public Guid TargetQuestionSetVersionId { get; private set; }
    public string VersionRef { get; private set; } = string.Empty;
    public string SchemaDigest { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; private set; }
    public IReadOnlyList<PublishedSurveySchemaField> Fields => fields.AsReadOnly();

    public static PublishedSurveySchema Compose(
        SurveyTarget target, FormVersion common, TargetQuestionSetVersion additional,
        string versionRef, DateTimeOffset publishedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionRef);
        if (target.TenantId != common.TenantId || target.TenantId != additional.TenantId
            || target.FormDefinitionId != common.FormDefinitionId || target.Id != additional.SurveyTargetId
            || publishedAt < common.PublishedAtUtc || publishedAt < additional.PublishedAtUtc)
        {
            throw new ArgumentException("Only this target's published version pair can be composed.");
        }

        var orderedFields = common.Fields.OrderBy(field => field.SortOrder)
            .Concat(additional.Fields.OrderBy(field => field.SortOrder)).ToArray();
        var snapshotFields = ReadFields(common.SnapshotJson, additional.SnapshotJson);
        if (!orderedFields.Select(field => field.FieldKey).SequenceEqual(snapshotFields.Select(field => field.FieldKey))
            || orderedFields.Select(field => field.FieldKey).Distinct(StringComparer.Ordinal).Count() != orderedFields.Length)
        {
            throw new ArgumentException("Published field records must match the snapshot with unique composed keys.");
        }

        var schema = new PublishedSurveySchema
        {
            TenantId = target.TenantId,
            SurveyTargetId = target.Id,
            FormDefinitionId = common.FormDefinitionId,
            FormVersionId = common.Id,
            TargetQuestionSetVersionId = additional.Id,
            VersionRef = versionRef,
            SchemaDigest = ComputeDigest(common.SnapshotJson, additional.SnapshotJson),
            PublishedAtUtc = publishedAt.ToUniversalTime()
        };
        schema.fields.AddRange(orderedFields.Select((field, order) => new PublishedSurveySchemaField(schema, field, order)));
        return schema;
    }

    public static IReadOnlyList<SurveyField> ReadFields(string commonSnapshotJson, string additionalSnapshotJson) =>
        Array.AsReadOnly(SurveySchemaSnapshot.ReadPart<SurveyField[]>(commonSnapshotJson)
            .Concat(SurveySchemaSnapshot.ReadPart<SurveyField[]>(additionalSnapshotJson)).ToArray());

    public static string ComputeDigest(string commonSnapshotJson, string additionalSnapshotJson)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("AQ-SCHEMA-v1"u8);
        AppendSnapshot(hash, commonSnapshotJson);
        AppendSnapshot(hash, additionalSnapshotJson);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendSnapshot(IncrementalHash hash, string snapshot)
    {
        var bytes = Encoding.UTF8.GetBytes(snapshot);
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class PublishedSurveySchemaField : AuditableEntity, ITenantEntity
{
    private PublishedSurveySchemaField()
    {
    }

    internal PublishedSurveySchemaField(PublishedSurveySchema schema, FormFieldDefinition field, int order)
    {
        TenantId = schema.TenantId;
        PublishedSurveySchemaId = schema.Id;
        FormFieldDefinitionId = field.Id;
        FormFieldDefinition = field;
        SortOrder = order;
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
    public Guid PublishedSurveySchemaId { get; private set; }
    public Guid FormFieldDefinitionId { get; private set; }
    public int SortOrder { get; private set; }
    public FormFieldDefinition? FormFieldDefinition { get; private set; }
}
