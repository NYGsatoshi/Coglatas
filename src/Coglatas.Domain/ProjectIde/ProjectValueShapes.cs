using System.Collections.ObjectModel;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Provisional deterministic processing bounds, not production capacity guarantees.</summary>
public sealed record ProjectValueLimits(int MaximumDepth = 16, int MaximumElements = 1024, int MaximumNodes = 8192)
{
    internal void Validate()
    {
        if (MaximumDepth is < 1 or > 64 || MaximumElements is < 1 or > 100_000 || MaximumNodes is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(ProjectValueLimits));
    }
}

internal sealed class ProjectValueBudget(ProjectValueLimits limits)
{
    private int _nodes;
    internal ProjectValueLimits Limits { get; } = limits;
    internal void Visit(int depth)
    {
        if (depth > Limits.MaximumDepth || ++_nodes > Limits.MaximumNodes)
            throw new FormatException("Type/value processing budget exceeded.");
    }
    internal void Count(int count)
    {
        if (count > Limits.MaximumElements) throw new FormatException("Collection/field bound exceeded.");
    }
}

public sealed record StructureField(string Name, TypeReference Type, bool Required);
public sealed record ProjectMapEntry(TypedProjectValue Key, TypedProjectValue Value);
public sealed record OptionalProjectValue(bool Present, TypedProjectValue? Value);

/// <summary>Frozen values. Set/map identity is explicitly canonical tagged Source value identity.</summary>
public sealed class ProjectCollectionValue
{
    public IReadOnlyList<TypedProjectValue> Items { get; }
    public IReadOnlyList<ProjectMapEntry> Entries { get; }
    internal ProjectCollectionValue(IEnumerable<TypedProjectValue> items, IEnumerable<ProjectMapEntry> entries)
    {
        Items = new ReadOnlyCollection<TypedProjectValue>(items.ToArray());
        Entries = new ReadOnlyCollection<ProjectMapEntry>(entries.ToArray());
    }
}

public sealed class ProjectStructureValue
{
    public IReadOnlyDictionary<string, TypedProjectValue> Fields { get; }
    internal ProjectStructureValue(IDictionary<string, TypedProjectValue> fields) =>
        Fields = new ReadOnlyDictionary<string, TypedProjectValue>(new Dictionary<string, TypedProjectValue>(fields, StringComparer.Ordinal));
}

internal static class ProjectValueShapes
{
    internal static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "coglatas.list", "coglatas.sequence", "coglatas.set", "coglatas.map", "coglatas.optional",
        "coglatas.nullable", "coglatas.structure", "coglatas.named-structure",
        "coglatas.entity-reference", "coglatas.document-reference", "coglatas.resource-reference", "coglatas.external-snapshot-reference"
    };

    internal static object Read(TypeReference type, JsonElement value, SourceCodecLimits sourceLimits,
        ExactNumberLimits numericLimits, ProjectValueBudget budget, int depth)
    {
        SourceJson Json(JsonElement element) => SourceJson.FromElement(element, sourceLimits);
        TypedProjectValue Child(JsonElement element, TypeReference expected)
        {
            var decoded = TypedProjectValueCodec.DecodeCore(Json(element), numericLimits, budget, depth + 1);
            if (decoded.Status == ValueDecodeStatus.Invalid) throw new FormatException("Invalid nested value.");
            if (decoded.Status != ValueDecodeStatus.Decoded) throw new UnsupportedSourceException("Unsupported nested value is retained.");
            if (!decoded.Value!.Type.Data.Equals(expected.Data)) throw new FormatException("Nested value type mismatch.");
            return decoded.Value;
        }
        if (type.ExpectedKind is not null) return new TypedProjectReference(Json(value), type.Kind, type.ExpectedKind);
        if (type.Kind == "coglatas.nullable") return Child(value, type.ElementType!);
        if (type.Kind == "coglatas.optional")
        {
            var present = ValueFields.Boolean(value, "present");
            if (!present && value.TryGetProperty("value", out _)) throw new FormatException("Absent optional cannot carry a value.");
            RequireFields(value, present ? ["present", "value"] : ["present"]);
            return new OptionalProjectValue(present, present ? Child(ValueFields.Required(value, "value"), type.ElementType!) : null);
        }
        if (type.Kind == "coglatas.structure")
        {
            if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected structure object.");
            budget.Count(value.GetPropertyCount());
            var result = new Dictionary<string, TypedProjectValue>(StringComparer.Ordinal);
            var names = type.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
            if (value.EnumerateObject().Any(field => !names.Contains(field.Name)))
                throw new UnsupportedSourceException("Undeclared fields remain opaque.");
            foreach (var field in type.Fields)
            {
                if (value.TryGetProperty(field.Name, out var item)) result.Add(field.Name, Child(item, field.Type));
                else if (field.Required) throw new FormatException("Required structure field is absent.");
            }
            return new ProjectStructureValue(result);
        }
        if (value.ValueKind != JsonValueKind.Array) throw new FormatException("Expected collection array.");
        budget.Count(value.GetArrayLength());
        var items = new List<TypedProjectValue>();
        var entries = new List<ProjectMapEntry>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        string? previousIdentity = null;
        foreach (var element in value.EnumerateArray())
        {
            if (type.Kind == "coglatas.map")
            {
                RequireFields(element, "key", "value");
                var key = Child(ValueFields.Required(element, "key"), type.KeyType!);
                if (key.State != ValueState.Known) throw new UnsupportedSourceException("Map key identity requires a known value.");
                if (!identities.Add(key.Data.CanonicalText)) throw new FormatException("Duplicate map key.");
                RequireOrder(key.Data.CanonicalText);
                entries.Add(new(key, Child(ValueFields.Required(element, "value"), type.ValueType!)));
            }
            else
            {
                var item = Child(element, type.ElementType!);
                if (type.Kind == "coglatas.set" && !identities.Add(item.Data.CanonicalText)) throw new FormatException("Duplicate set value identity.");
                if (type.Kind == "coglatas.set") RequireOrder(item.Data.CanonicalText);
                items.Add(item);
            }
        }
        return new ProjectCollectionValue(items, entries);

        void RequireOrder(string identity)
        {
            if (previousIdentity is not null && CanonicalJson.ScalarCompare(previousIdentity, identity) >= 0)
                throw new FormatException("Canonical set/map entries must be ordered by tagged Source identity.");
            previousIdentity = identity;
        }
    }

    internal static void RequireFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected object.");
        if (ValueFields.HasUnknown(value, fields)) throw new UnsupportedSourceException("Unknown members are retained without interpretation.");
    }
}
