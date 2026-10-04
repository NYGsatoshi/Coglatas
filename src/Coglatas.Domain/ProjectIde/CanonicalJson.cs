using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Processing bounds, not a product/device acceptance claim. Rejected buffers remain recoverable.</summary>
public sealed record SourceCodecLimits(int MaximumBytes = 4_194_304, int MaximumDepth = 64, int MaximumNumberCharacters = 100_000)
{
    internal void Validate()
    {
        if (MaximumBytes <= 0 || MaximumDepth is < 1 or > 256 || MaximumNumberCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(SourceCodecLimits));
    }
}

/// <summary>Immutable JSON value retaining unknown object boundaries, exact decimals and ordered arrays.</summary>
public sealed class SourceJson : IEquatable<SourceJson>
{
    private readonly JsonElement value;
    private readonly string canonical;
    internal SourceCodecLimits Limits { get; }
    public JsonElement Value => value;
    public string CanonicalText => canonical;

    private SourceJson(string canonical, SourceCodecLimits limits)
    {
        using var document = JsonDocument.Parse(canonical, new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
        this.value = document.RootElement.Clone();
        this.canonical = canonical;
        Limits = limits;
    }

    public static SourceJson Parse(ReadOnlySpan<byte> utf8, SourceCodecLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        if (utf8.Length > limits.MaximumBytes) throw new FormatException("JSON exceeds the byte processing bound.");
        try
        {
            // Throw on malformed UTF-8 rather than allowing replacement-character recovery.
            _ = new UTF8Encoding(false, true).GetString(utf8);
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
            return FromElement(document.RootElement, limits);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            throw new FormatException("Invalid JSON or Unicode scalar data.", error);
        }
    }

    public static SourceJson Parse(string json, SourceCodecLimits? limits = null)
    {
        CanonicalJson.ValidateUnicode(json);
        return Parse(Encoding.UTF8.GetBytes(json), limits);
    }

    internal static SourceJson FromElement(JsonElement value, SourceCodecLimits? limits = null)
    {
        limits ??= new();
        return new(CanonicalJson.Write(value, limits), limits);
    }

    internal static SourceJson FromObject(object value, SourceCodecLimits? limits = null) => FromElement(
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions { MaxDepth = (limits ?? new()).MaximumDepth }), limits);
    public byte[] ToCanonicalBytes() => Encoding.UTF8.GetBytes(canonical);
    public bool Equals(SourceJson? other) => other is not null && canonical == other.canonical;
    public override bool Equals(object? obj) => obj is SourceJson other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(canonical);
}

internal static class CanonicalJson
{
    internal static string Write(JsonElement value, SourceCodecLimits limits)
    {
        limits.Validate();
        var output = new StringBuilder();
        Append(value, output, limits, 0);
        if (Encoding.UTF8.GetByteCount(output.ToString()) > limits.MaximumBytes)
            throw new FormatException("Canonical JSON exceeds the byte processing bound.");
        return output.ToString();
    }

    private static void Append(JsonElement value, StringBuilder output, SourceCodecLimits limits, int depth)
    {
        if (depth > limits.MaximumDepth) throw new FormatException("JSON exceeds the depth processing bound.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var members = value.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in members)
                {
                    ValidateUnicode(member.Name);
                    if (!names.Add(member.Name)) throw new FormatException("Duplicate JSON object member.");
                }
                Array.Sort(members, (left, right) => ScalarCompare(left.Name, right.Name));
                output.Append('{');
                for (var i = 0; i < members.Length; i++)
                {
                    if (i > 0) output.Append(',');
                    AppendString(members[i].Name, output);
                    output.Append(':');
                    Append(members[i].Value, output, limits, depth + 1);
                }
                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) output.Append(',');
                    first = false;
                    Append(item, output, limits, depth + 1);
                }
                output.Append(']');
                break;
            case JsonValueKind.String:
                AppendString(value.GetString()!, output);
                break;
            case JsonValueKind.Number:
                output.Append(NormalizeNumber(value.GetRawText(), limits.MaximumNumberCharacters));
                break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new FormatException("Undefined JSON value.");
        }
    }

    internal static int ScalarCompare(string left, string right)
    {
        var a = left.EnumerateRunes().GetEnumerator();
        var b = right.EnumerateRunes().GetEnumerator();
        while (true)
        {
            var hasA = a.MoveNext();
            var hasB = b.MoveNext();
            if (!hasA || !hasB) return hasA.CompareTo(hasB);
            var comparison = a.Current.Value.CompareTo(b.Current.Value);
            if (comparison != 0) return comparison;
        }
    }

    internal static void ValidateUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[++i]))
                throw new FormatException("Invalid Unicode scalar.");
        }
    }

    private static void AppendString(string text, StringBuilder output)
    {
        ValidateUnicode(text);
        output.Append('"');
        foreach (var character in text)
        {
            if (character is '"' or '\\') output.Append('\\').Append(character);
            else if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else output.Append(character);
        }
        output.Append('"');
    }

    // JSON grammar has already been checked. Work only on digits; never pass through double/decimal.
    private static string NormalizeNumber(string token, int maximumCharacters)
    {
        if (token.Length > maximumCharacters) throw new FormatException("Number exceeds processing bound.");
        var negative = token[0] == '-';
        var start = negative ? 1 : 0;
        var exponentIndex = token.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? token[start..] : token[start..exponentIndex];
        var exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(token[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
            throw new FormatException("Number expansion exceeds processing bound.");
        var dot = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var decimalPosition = (long)(dot < 0 ? digits.Length : dot) + exponent;
        var leading = 0;
        while (leading < digits.Length && digits[leading] == '0') leading++;
        if (leading == digits.Length) return "0";
        digits = digits[leading..].TrimEnd('0');
        decimalPosition -= leading;
        var expandedLength = decimalPosition <= 0 ? 2 - decimalPosition + digits.Length : Math.Max(decimalPosition, digits.Length + 1L);
        if (expandedLength + (negative ? 1 : 0) > maximumCharacters)
            throw new FormatException("Number expansion exceeds processing bound.");
        string result;
        if (decimalPosition <= 0) result = "0." + new string('0', checked((int)-decimalPosition)) + digits;
        else if (decimalPosition >= digits.Length) result = digits + new string('0', checked((int)decimalPosition - digits.Length));
        else result = digits.Insert(checked((int)decimalPosition), ".");
        return negative ? "-" + result : result;
    }
}
