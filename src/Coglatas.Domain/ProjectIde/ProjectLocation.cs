using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Optional UTF-16 buffer coordinates, subordinate to the semantic identity/path.</summary>
public sealed record SourceTextSpan
{
    public int Start { get; }
    public int Length { get; }
    public SourceTextSpan(int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(start));
        Start = start; Length = length;
    }
}

/// <summary>Shared semantic locator; no row number, UI object or storage path is authoritative.</summary>
public sealed class ProjectLocation : IEquatable<ProjectLocation>
{
    public const string Format = "coglatas.project-location/1";
    public DocumentId DocumentId { get; }
    public EntityId? EntityId { get; }
    public RelationId? RelationId { get; }
    public string PropertyPath { get; }
    public SourceTextSpan? TextSpan { get; }
    public SourceRevisionContext Context { get; }
    public SourceJson Data { get; }

    private ProjectLocation(DocumentId documentId, EntityId? entityId, RelationId? relationId,
        string propertyPath, SourceTextSpan? textSpan, SourceRevisionContext context, SourceJson data)
    {
        DocumentId = documentId; EntityId = entityId; RelationId = relationId;
        PropertyPath = propertyPath; TextSpan = textSpan; Context = context; Data = data;
    }

    public static ProjectLocation Create(DocumentId documentId, SourceRevisionContext context, string propertyPath = "",
        EntityId? entityId = null, RelationId? relationId = null, SourceTextSpan? textSpan = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var fields = new Dictionary<string, object?>
        {
            ["format"] = Format, ["schemaId"] = "coglatas.project-location", ["schemaVersion"] = 1,
            ["canonicalEncodingVersion"] = 1, ["documentId"] = documentId.ToString(),
            ["context"] = context.Data.Value, ["propertyPath"] = propertyPath
        };
        if (entityId is not null) fields["entityId"] = entityId.Value.ToString();
        if (relationId is not null) fields["relationId"] = relationId.Value.ToString();
        if (textSpan is not null) fields["textSpan"] = new { start = textSpan.Start, length = textSpan.Length };
        return Parse(SourceJson.FromObject(fields));
    }

    public static ProjectLocation Parse(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var root = data.Value;
        if (SourceFields.String(root, "format") != Format || SourceFields.String(root, "schemaId") != "coglatas.project-location" ||
            SourceFields.PositiveInteger(root, "schemaVersion") != 1 || SourceFields.PositiveInteger(root, "canonicalEncodingVersion") != 1)
            throw new UnsupportedSourceException("Unsupported ProjectLocation version.");
        var document = SourceFields.Identity<DocumentId>(root, "documentId");
        EntityId? entity = OptionalId<EntityId>(root, "entityId");
        RelationId? relation = OptionalId<RelationId>(root, "relationId");
        var path = SourceFields.String(root, "propertyPath");
        ValidatePath(path);
        SourceTextSpan? span = null;
        if (root.TryGetProperty("textSpan", out var text) && text.ValueKind != JsonValueKind.Null)
        {
            var start = SourceFields.Required(text, "start", JsonValueKind.Number);
            var length = SourceFields.Required(text, "length", JsonValueKind.Number);
            if (!start.TryGetInt32(out var startValue) || !length.TryGetInt32(out var lengthValue)) throw new FormatException("Invalid text span integer.");
            span = new(startValue, lengthValue);
        }
        return new(document, entity, relation, path, span,
            SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(root, "context"), data.Limits)), data);
    }

    private static T? OptionalId<T>(JsonElement root, string field) where T : struct, IProjectIdentity<T>
    {
        if (!root.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return SourceFields.Identity<T>(root, field);
    }

    private static void ValidatePath(string path)
    {
        if (path.Length > 0 && path[0] != '/') throw new FormatException("Property path must be an RFC 6901 JSON pointer.");
        for (var i = 0; i < path.Length; i++)
            if (path[i] == '~' && (i + 1 >= path.Length || path[++i] is not ('0' or '1')))
                throw new FormatException("Invalid JSON pointer escape.");
    }

    public byte[] ToCanonicalBytes() => Data.ToCanonicalBytes();
    public bool Equals(ProjectLocation? other) => other is not null && Data.CanonicalText == other.Data.CanonicalText;
    public override bool Equals(object? obj) => obj is ProjectLocation other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Data.CanonicalText);
}
