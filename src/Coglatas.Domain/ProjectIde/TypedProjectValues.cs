using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum ValueState { Known, Null, Unknown }
public enum ValueDecodeStatus { Decoded, Unsupported, Invalid }

/// <summary>Source type descriptor. Unsupported kinds/members retain their complete original representation.</summary>
public sealed class TypeReference
{
    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal)
    {
        "coglatas.boolean", "coglatas.text", "coglatas.decimal", "coglatas.rational", "coglatas.enum",
        "coglatas.quantity", "coglatas.elapsed-duration", "coglatas.working-duration", "coglatas.calendar-period",
        "coglatas.instant", "coglatas.local-date", "coglatas.local-date-time", "coglatas.zoned-local-date-time",
        "coglatas.time-interval"
    };
    public string Kind { get; }
    public int SchemaVersion { get; }
    public bool Nullable { get; }
    public bool AcceptsUnknown { get; }
    public bool IsSupported { get; }
    public string? UnitId { get; }
    public Dimension? Dimension { get; }
    public SourceJson Data { get; }

    public TypeReference(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        Kind = SourceFields.Token(data.Value, "kind");
        SchemaVersion = SourceFields.PositiveInteger(data.Value, "schemaVersion");
        if (SchemaVersion != 1 || !Scalars.Contains(Kind)) return;
        Nullable = ValueFields.Boolean(data.Value, "nullable");
        AcceptsUnknown = ValueFields.Boolean(data.Value, "acceptsUnknown");
        var fields = new List<string> { "kind", "schemaVersion", "nullable", "acceptsUnknown" };
        if (Kind == "coglatas.quantity")
        {
            UnitId = SourceFields.Token(data.Value, "unitId");
            Dimension = new(SourceJson.FromElement(SourceFields.Object(data.Value, "dimension"), data.Limits));
            fields.AddRange(["unitId", "dimension"]);
        }
        if (Kind == "coglatas.enum")
        {
            var tokens = SourceFields.Array(data.Value, "tokens").EnumerateArray().ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in tokens)
                if (token.ValueKind != JsonValueKind.String || !ValueFields.IsToken(token.GetString()!) || !names.Add(token.GetString()!))
                    throw new FormatException("Enum tokens must be unique stable namespaced symbols.");
            if (names.Count == 0) throw new FormatException("Enum type requires declared tokens.");
            fields.Add("tokens");
        }
        IsSupported = !ValueFields.HasUnknown(data.Value, fields.ToArray());
    }
}

public sealed class TypedProjectValue
{
    public TypeReference Type { get; }
    public ValueState State { get; }
    /// <summary>Only immutable supported built-ins. Null/Unknown never obtain a fabricated known value.</summary>
    public object? KnownValue { get; }
    public string? UnknownReason { get; }
    public SourceJson? UnknownBasis { get; }
    public SourceJson Data { get; }

    internal TypedProjectValue(TypeReference type, ValueState state, object? value, string? reason, SourceJson? basis, SourceJson data)
    {
        Type = type; State = state; KnownValue = value; UnknownReason = reason; UnknownBasis = basis; Data = data;
    }
}

/// <summary>Value decoding is not compilation, reference binding, evidence eligibility or execution success.</summary>
public sealed class ValueDecodeResult
{
    public ValueDecodeStatus Status { get; }
    public TypedProjectValue? Value { get; }
    public SourceJson Data { get; }
    internal ValueDecodeResult(ValueDecodeStatus status, TypedProjectValue? value, SourceJson data)
    { Status = status; Value = value; Data = data; }
}

public static class TypedProjectValueCodec
{
    public static ValueDecodeResult Decode(SourceJson data, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        limits ??= new();
        limits.Validate();
        try
        {
            var type = new TypeReference(SourceJson.FromElement(SourceFields.Object(data.Value, "type"), data.Limits));
            if (!type.IsSupported) return new(ValueDecodeStatus.Unsupported, null, data);
            var token = SourceFields.Token(data.Value, "state");
            var fields = new List<string> { "type", "state" };
            TypedProjectValue value;
            switch (token)
            {
                case "coglatas.known":
                    var known = ReadKnown(type, ValueFields.Required(data.Value, "value"), data.Limits, limits);
                    value = new(type, ValueState.Known, known, null, null, data);
                    fields.Add("value");
                    Reject(data.Value, "reason", "basis");
                    break;
                case "coglatas.null":
                    if (!type.Nullable) throw new FormatException("Null is forbidden by this type.");
                    Reject(data.Value, "value", "reason", "basis");
                    value = new(type, ValueState.Null, null, null, null, data);
                    break;
                case "coglatas.unknown":
                    if (!type.AcceptsUnknown) throw new FormatException("Unknown is forbidden by this type.");
                    Reject(data.Value, "value");
                    var reason = SourceFields.Token(data.Value, "reason");
                    var basis = SourceJson.FromElement(SourceFields.Object(data.Value, "basis"), data.Limits);
                    value = new(type, ValueState.Unknown, null, reason, basis, data);
                    fields.AddRange(["reason", "basis"]);
                    break;
                default: return new(ValueDecodeStatus.Unsupported, null, data);
            }
            return ValueFields.HasUnknown(data.Value, fields.ToArray())
                ? new(ValueDecodeStatus.Unsupported, null, data)
                : new(ValueDecodeStatus.Decoded, value, data);
        }
        catch (UnsupportedSourceException) { return new(ValueDecodeStatus.Unsupported, null, data); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or DivideByZeroException)
        { return new(ValueDecodeStatus.Invalid, null, data); }
    }

    public static byte[] Encode(ValueDecodeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Data.ToCanonicalBytes();
    }

    private static object ReadKnown(TypeReference type, JsonElement value, SourceCodecLimits sourceLimits, ExactNumberLimits numericLimits)
    {
        switch (type.Kind)
        {
            case "coglatas.boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException("Expected boolean.");
                return value.GetBoolean();
            case "coglatas.text": return ValueFields.String(value);
            case "coglatas.decimal":
                var text = ValueFields.String(value);
                var number = ExactDecimal.Parse(text, numericLimits);
                if (text != number.CanonicalText) throw new FormatException("Expected canonical decimal spelling.");
                return number;
            case "coglatas.rational":
                RequireFields(value, "numerator", "denominator");
                var numerator = SourceFields.String(value, "numerator");
                var denominator = SourceFields.String(value, "denominator");
                var rational = ExactRational.Parse(numerator, denominator, numericLimits);
                if (rational.ToString() != numerator + "/" + denominator) throw new FormatException("Expected reduced rational with positive denominator.");
                return rational;
            case "coglatas.enum":
                var enumToken = ValueFields.String(value);
                if (!ValueFields.IsToken(enumToken)) throw new FormatException("Expected namespaced enum token.");
                if (!type.Data.Value.GetProperty("tokens").EnumerateArray().Any(item => item.GetString() == enumToken))
                    throw new UnsupportedSourceException("Unknown enum value is preserved without fallback.");
                return enumToken;
            case "coglatas.quantity":
                RequireFields(value, "amount", "unitId", "dimension");
                var quantity = new Quantity(SourceJson.FromElement(value, sourceLimits), numericLimits);
                quantity.RequireCompatible(type.UnitId!, type.Dimension!);
                return quantity;
            case "coglatas.elapsed-duration":
                return ElapsedDuration.Parse(ValueFields.String(value));
            case "coglatas.working-duration":
                RequireFields(value, "ticks", "calendar");
                return new WorkingDuration(TimeValueFields.Ticks(SourceFields.String(value, "ticks")), ReadCalendar(value, sourceLimits));
            case "coglatas.calendar-period":
                RequireFields(value, "years", "months", "days");
                return new CalendarPeriod(ValueFields.Integer(value, "years"), ValueFields.Integer(value, "months"), ValueFields.Integer(value, "days"));
            case "coglatas.instant": return ProjectInstant.Parse(ValueFields.String(value));
            case "coglatas.local-date-time": return new ProjectLocalDateTime(ValueFields.String(value));
            case "coglatas.zoned-local-date-time": return new ZonedCivilTime(SourceJson.FromElement(value, sourceLimits));
            case "coglatas.time-interval": return new ProjectTimeInterval(SourceJson.FromElement(value, sourceLimits));
            case "coglatas.local-date":
                RequireFields(value, "date", "calendar");
                return new ProjectLocalDate(SourceFields.String(value, "date"), ReadCalendar(value, sourceLimits));
            default: throw new UnsupportedSourceException("Unsupported value type.");
        }
    }

    private static CalendarReference ReadCalendar(JsonElement value, SourceCodecLimits limits)
    {
        var calendar = new CalendarReference(SourceJson.FromElement(SourceFields.Object(value, "calendar"), limits));
        RequireFields(calendar.Data.Value, "calendarId", "version", "timeZoneId", "rulesDigest");
        RequireFields(calendar.RulesDigest.Data.Value, "algorithm", "value");
        // Resolving an IANA identifier or rules digest belongs to the pinned CompilerInput, never the OS database.
        return calendar;
    }

    private static void RequireFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected structured value.");
        if (ValueFields.HasUnknown(value, fields)) throw new UnsupportedSourceException("Unknown value members require explicit compatibility.");
    }

    private static void Reject(JsonElement value, params string[] names)
    {
        if (names.Any(name => value.TryGetProperty(name, out _))) throw new FormatException("Conflicting value-state payloads.");
    }
}

internal static class ValueFields
{
    internal static bool HasUnknown(JsonElement value, params string[] names) =>
        value.EnumerateObject().Any(member => !names.Contains(member.Name, StringComparer.Ordinal));
    internal static bool IsToken(string text) => text.Contains('.') && !text.Any(char.IsWhiteSpace);
    internal static JsonElement Required(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : throw new FormatException("Required value field is absent.");
    internal static string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException("Expected string value.");
    internal static bool Boolean(JsonElement value, string name) => Required(value, name).ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new FormatException("Expected explicit boolean flag.")
    };
    internal static int Integer(JsonElement value, string name)
    {
        var field = Required(value, name);
        return field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var number) ? number : throw new FormatException("Expected checked 32-bit integer.");
    }
}
