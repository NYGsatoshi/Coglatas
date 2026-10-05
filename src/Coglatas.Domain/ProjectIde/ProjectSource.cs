using System.Collections.ObjectModel;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Only the common typed root header; kind-specific semantic types belong to Slice 2.</summary>
public sealed class SourceRoot
{
    public EntityId EntityId { get; }
    public string Kind { get; }
    public int SchemaVersion { get; }
    public SourceJson Payload { get; }
    public SourceJson Data { get; }
    internal SourceRoot(SourceJson data)
    {
        Data = data;
        EntityId = SourceFields.Identity<EntityId>(data.Value, "entityId");
        Kind = SourceFields.Token(data.Value, "kind");
        SchemaVersion = SourceFields.PositiveInteger(data.Value, "schemaVersion");
        Payload = SourceJson.FromElement(SourceFields.Object(data.Value, "payload"), data.Limits);
    }
}

public sealed class SourceDocument
{
    public const string SchemaId = "coglatas.source-document";
    public const string DigestDomain = "coglatas.source-document/1";
    public DocumentId DocumentId { get; }
    public string LogicalName { get; }
    public SourceRoot Root { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }

    private SourceDocument(SourceJson data)
    {
        Data = data;
        DocumentId = SourceFields.Identity<DocumentId>(data.Value, "documentId");
        LogicalName = SourceFields.String(data.Value, "logicalName");
        if (SourceFields.String(data.Value, "schemaId") != SchemaId || SourceFields.PositiveInteger(data.Value, "schemaVersion") != 1)
            throw new UnsupportedSourceException("Unsupported document schema.");
        Root = new(SourceJson.FromElement(SourceFields.Object(data.Value, "root"), data.Limits));
        Digest = ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "digest"), data.Limits));
        var computed = ComputeDigest(data);
        if (computed.Value != Digest.Value) throw new FormatException("Document digest does not match canonical document payload.");
    }

    public static SourceDocument Create(DocumentId documentId, string logicalName, SourceJson root, SourceJson? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var fields = new Dictionary<string, object?>
        {
            ["documentId"] = documentId.ToString(), ["logicalName"] = logicalName,
            ["schemaId"] = SchemaId, ["schemaVersion"] = 1, ["root"] = root.Value
        };
        if (extensions is not null) fields["extensions"] = extensions.Value;
        var body = SourceJson.FromObject(fields);
        fields["digest"] = ComputeDigest(body).Data.Value;
        return Parse(SourceJson.FromObject(fields));
    }

    public static SourceDocument Parse(SourceJson data) => new(data);
    private static ContentDigest ComputeDigest(SourceJson data) =>
        ContentDigest.Compute(DigestDomain, SourceJson.FromObject(data.Value.EnumerateObject()
            .Where(member => member.Name != "digest").ToDictionary(member => member.Name, member => member.Value), data.Limits).ToCanonicalBytes());
}

public sealed class SourceManifest
{
    public SourceJson Data { get; }
    public IReadOnlyList<DocumentId> DocumentIds { get; }

    internal SourceManifest(SourceJson data, IReadOnlyList<SourceDocument> documents)
    {
        Data = data;
        var entries = SourceFields.Array(data.Value, "documents").EnumerateArray().ToArray();
        var byId = documents.ToDictionary(document => document.DocumentId);
        var seen = new HashSet<DocumentId>();
        foreach (var entry in entries)
        {
            var id = SourceFields.Identity<DocumentId>(entry, "documentId");
            if (!seen.Add(id)) throw new FormatException("Duplicate manifest DocumentId.");
            if (!byId.TryGetValue(id, out var document)) throw new FormatException("Manifest refers to an absent document.");
            var digest = ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(entry, "digest"), data.Limits));
            if (digest.Value != document.Digest.Value || SourceFields.String(entry, "schemaId") != SourceDocument.SchemaId ||
                SourceFields.PositiveInteger(entry, "schemaVersion") != 1)
                throw new FormatException("Manifest document identity/schema/digest mismatch.");
        }
        if (seen.Count != documents.Count) throw new FormatException("Manifest does not cover every document.");
        DocumentIds = new ReadOnlyCollection<DocumentId>(seen.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray());
    }

    internal static object CreateEntries(IEnumerable<SourceDocument> documents) => documents.Select(document => new
    {
        documentId = document.DocumentId.ToString(), schemaId = SourceDocument.SchemaId,
        schemaVersion = 1, digest = document.Digest.Data.Value
    }).ToArray();
}

/// <summary>Canonical plan representation; neither IR nor a writable database/transport DTO.</summary>
public sealed class ProjectSource
{
    public const string Format = "coglatas.project-source/1";
    public const string SchemaId = "coglatas.project-source";
    public const int SchemaVersion = 1;
    public const int CanonicalEncodingVersion = 1;
    public TenantId TenantId { get; }
    public ProjectId ProjectId { get; }
    public SourceRevisionContext Context { get; }
    public SourceManifest Manifest { get; }
    public IReadOnlyList<SourceDocument> Documents { get; }
    public SourceJson Declarations { get; }
    public SourceJson Relations { get; }
    public SourceJson ExtensionDeclarations { get; }
    public SourceJson PackageLock { get; }
    public SourceJson SourceReferences { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal ProjectSource(SourceJson data)
    {
        Data = data;
        TenantId = SourceFields.Identity<TenantId>(data.Value, "tenantId");
        ProjectId = SourceFields.Identity<ProjectId>(data.Value, "projectId");
        Context = SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "context"), data.Limits));
        if (Context.Branch.TenantId != TenantId || Context.Branch.ProjectId != ProjectId)
            throw new FormatException("Source context has a different Tenant/Project scope.");
        var documents = SourceFields.Array(data.Value, "documents").EnumerateArray()
            .Select(item => SourceDocument.Parse(SourceJson.FromElement(item, data.Limits))).ToArray();
        if (documents.Length == 0 || !documents.Any(document => document.Root.Kind == "coglatas.project"))
            throw new FormatException("Source requires a Project root document.");
        if (documents.Select(document => document.DocumentId).Distinct().Count() != documents.Length)
            throw new FormatException("Duplicate DocumentId.");
        Documents = new ReadOnlyCollection<SourceDocument>(documents);
        Manifest = new(SourceJson.FromElement(SourceFields.Object(data.Value, "manifest"), data.Limits), Documents);
        Declarations = SourceJson.FromElement(SourceFields.Array(data.Value, "declarations"), data.Limits);
        Relations = SourceJson.FromElement(SourceFields.Array(data.Value, "relations"), data.Limits);
        ExtensionDeclarations = SourceJson.FromElement(SourceFields.Array(data.Value, "extensionDeclarations"), data.Limits);
        PackageLock = SourceJson.FromElement(SourceFields.Array(data.Value, "packageLock"), data.Limits);
        SourceReferences = SourceJson.FromElement(SourceFields.Array(data.Value, "sourceReferences"), data.Limits);
        ValidateIdentities(documents);
        Digest = ContentDigest.Compute(Format, data.ToCanonicalBytes());
    }

    private void ValidateIdentities(SourceDocument[] documents)
    {
        var documentIds = documents.Select(document => document.DocumentId).ToHashSet();
        var entities = new HashSet<EntityId>();
        foreach (var document in documents)
            if (!entities.Add(document.Root.EntityId)) throw new FormatException("Duplicate root EntityId.");
        foreach (var declaration in Declarations.Value.EnumerateArray())
        {
            if (!entities.Add(SourceFields.Identity<EntityId>(declaration, "entityId"))) throw new FormatException("Duplicate EntityId.");
            ValidateHeader(declaration, documentIds);
        }
        var relations = new HashSet<RelationId>();
        foreach (var relation in Relations.Value.EnumerateArray())
        {
            if (!relations.Add(SourceFields.Identity<RelationId>(relation, "relationId"))) throw new FormatException("Duplicate RelationId.");
            ValidateHeader(relation, documentIds);
            // Endpoint scope/type resolution and descriptor semantics belong to Slices 3/5.
            _ = SourceFields.Object(relation, "source");
            _ = SourceFields.Object(relation, "target");
        }
    }

    private static void ValidateHeader(JsonElement value, HashSet<DocumentId> documentIds)
    {
        _ = SourceFields.Token(value, "kind");
        _ = SourceFields.PositiveInteger(value, "schemaVersion");
        _ = SourceFields.Object(value, "payload");
        if (!documentIds.Contains(SourceFields.Identity<DocumentId>(value, "documentId")))
            throw new FormatException("Declaration/relation belongs to an absent document.");
    }

    public static ProjectSource Create(SourceRevisionContext context, IEnumerable<SourceDocument> documents,
        SourceJson? declarations = null, SourceJson? relations = null, SourceJson? extensionDeclarations = null,
        SourceJson? packageLock = null, SourceJson? sourceReferences = null, SourceJson? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(documents);
        var frozenDocuments = documents.ToArray();
        var empty = SourceJson.Parse("[]");
        var fields = new Dictionary<string, object?>
        {
            ["format"] = Format, ["schemaId"] = SchemaId, ["schemaVersion"] = SchemaVersion,
            ["canonicalEncodingVersion"] = CanonicalEncodingVersion,
            ["tenantId"] = context.Branch.TenantId.ToString(), ["projectId"] = context.Branch.ProjectId.ToString(),
            ["context"] = context.Data.Value, ["documents"] = frozenDocuments.Select(document => document.Data.Value).ToArray(),
            ["manifest"] = new { documents = SourceManifest.CreateEntries(frozenDocuments) },
            ["declarations"] = (declarations ?? empty).Value, ["relations"] = (relations ?? empty).Value,
            ["extensionDeclarations"] = (extensionDeclarations ?? empty).Value, ["packageLock"] = (packageLock ?? empty).Value,
            ["sourceReferences"] = (sourceReferences ?? empty).Value
        };
        if (extensions is not null) fields["extensions"] = extensions.Value;
        return ProjectSourceCodec.Decode(SourceJson.FromObject(fields).ToCanonicalBytes()).RequireSource();
    }
}
