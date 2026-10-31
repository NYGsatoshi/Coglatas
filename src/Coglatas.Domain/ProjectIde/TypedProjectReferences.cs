using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

[JsonConverter(typeof(ProjectIdentityJsonConverter<ExternalSnapshotId>))]
public readonly record struct ExternalSnapshotId : IProjectIdentity<ExternalSnapshotId>
{
    public Guid Value { get; }
    public ExternalSnapshotId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ExternalSnapshotId New() => new(Guid.NewGuid());
    public static ExternalSnapshotId Parse(string text) => new(ProjectIdentity.Parse(text));
    public override string ToString() => ProjectIdentity.Format(Value);
}

public enum ReferenceContextStatus { MatchesPinnedContext, ScopeMismatch, BranchMismatch, RevisionMismatch }

/// <summary>A Source-bound identity contract. No constructor or context check resolves or authorizes a target.</summary>
public sealed class TypedProjectReference
{
    public string Kind { get; }
    public string ExpectedKind { get; }
    public TenantId TenantId { get; }
    public ProjectId ProjectId { get; }
    public EntityId? EntityId { get; }
    public DocumentId? DocumentId { get; }
    public ResourceId? ResourceId { get; }
    public ExternalSnapshotId? SnapshotId { get; }
    public SourceRevisionContext Context { get; }
    public ProjectLocation Origin { get; }
    public string ManifestVersion { get; }
    public ContentDigest ManifestDigest { get; }
    public SourceJson Data { get; }

    public TypedProjectReference(SourceJson data, string kind, string expectedKind)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!ValueFields.IsToken(expectedKind)) throw new FormatException("Expected a namespaced target kind.");
        Kind = kind; ExpectedKind = expectedKind; Data = data;
        TenantId = SourceFields.Identity<TenantId>(data.Value, "tenantId");
        ProjectId = SourceFields.Identity<ProjectId>(data.Value, "projectId");
        Context = SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "context"), data.Limits));
        PlanEntityReference.RequireKnownContext(Context);
        if (Context.Branch.TenantId != TenantId || Context.Branch.ProjectId != ProjectId)
            throw new FormatException("Reference context scope mismatch.");
        Origin = ProjectLocation.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "origin"), data.Limits));
        PlanEntityReference.RequireKnownContext(Origin.Context);
        ProjectValueShapes.RequireFields(Origin.Data.Value, "format", "schemaId", "schemaVersion", "canonicalEncodingVersion",
            "documentId", "context", "propertyPath", "entityId", "relationId", "textSpan");
        if (Origin.TextSpan is not null) ProjectValueShapes.RequireFields(Origin.Data.Value.GetProperty("textSpan"), "start", "length");
        if (Origin.Context.Branch.TenantId != TenantId) throw new FormatException("Cross-Tenant reference origin.");
        var manifest = SourceFields.Object(data.Value, "manifest");
        ProjectValueShapes.RequireFields(manifest, "version", "digest");
        ManifestVersion = SourceFields.String(manifest, "version");
        if (string.IsNullOrWhiteSpace(ManifestVersion)) throw new FormatException("A fixed manifest version is required.");
        ManifestDigest = ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(manifest, "digest"), data.Limits));
        ProjectValueShapes.RequireFields(ManifestDigest.Data.Value, "algorithm", "value");
        var idField = kind switch
        {
            "coglatas.entity-reference" => "entityId", "coglatas.document-reference" => "documentId",
            "coglatas.resource-reference" => "resourceId", "coglatas.external-snapshot-reference" => "snapshotId",
            _ => throw new UnsupportedSourceException("Unsupported reference kind.")
        };
        switch (idField)
        {
            case "entityId": EntityId = SourceFields.Identity<EntityId>(data.Value, idField); break;
            case "documentId": DocumentId = SourceFields.Identity<DocumentId>(data.Value, idField); break;
            case "resourceId": ResourceId = SourceFields.Identity<ResourceId>(data.Value, idField); break;
            case "snapshotId": SnapshotId = SourceFields.Identity<ExternalSnapshotId>(data.Value, idField); break;
        }
        ProjectValueShapes.RequireFields(data.Value, "tenantId", "projectId", "context", "origin", "manifest", idField);
    }

    public ReferenceContextStatus ValidateTargetContext(SourceRevisionContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Branch.TenantId != TenantId || target.Branch.ProjectId != ProjectId) return ReferenceContextStatus.ScopeMismatch;
        if (target.Branch != Context.Branch) return ReferenceContextStatus.BranchMismatch;
        return Context.Equals(target) ? ReferenceContextStatus.MatchesPinnedContext : ReferenceContextStatus.RevisionMismatch;
    }

    public void RequireManifest(string version, ContentDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (version != ManifestVersion || !ManifestDigest.Data.Equals(digest.Data))
            throw new FormatException("Reference manifest version/digest mismatch.");
    }
}
