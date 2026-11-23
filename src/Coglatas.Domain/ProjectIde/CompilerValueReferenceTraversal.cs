using System.Globalization;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Visits decoded value trees only. Null, Unknown basis and absent optionals are opaque leaves.</summary>
internal static class CompilerValueReferenceTraversal
{
    internal static IEnumerable<(TypedProjectValue Value, string Path)> Values(TypedProjectValue value, string path)
    {
        yield return (value, path);
        if (value.State != ValueState.Known) yield break;
        switch (value.KnownValue)
        {
            case TypedProjectValue child:
                foreach (var item in Values(child, path + "/value")) yield return item;
                break;
            case OptionalProjectValue { Present: true, Value: { } child }:
                foreach (var item in Values(child, path + "/value/value")) yield return item;
                break;
            case ProjectStructureValue structure:
                foreach (var field in structure.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
                    foreach (var item in Values(field.Value, path + "/value/" + Escape(field.Key))) yield return item;
                break;
            case ProjectCollectionValue collection:
                for (var index = 0; index < collection.Items.Count; index++)
                {
                    var child = collection.Items[index];
                    var identity = value.Type.Kind == "coglatas.set" ? Identity(child) : index.ToString(CultureInfo.InvariantCulture);
                    foreach (var item in Values(child, path + "/value/" + identity)) yield return item;
                }
                foreach (var entry in collection.Entries)
                    foreach (var item in Values(entry.Value, path + "/value/" + Identity(entry.Key) + "/value")) yield return item;
                break;
        }
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string Identity(TypedProjectValue value) => ContentDigest.Compute("coglatas.canonical-value/1", value.Data.ToCanonicalBytes()).Value;
}
