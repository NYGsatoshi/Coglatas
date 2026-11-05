using System.Globalization;

namespace Coglatas.Domain.ProjectIde.VersionedArtifacts;

/// <summary>Full content identity. It is never an EntityId, authorization grant or producer proof.</summary>
public sealed record ArtifactIdentity
{
    public string ArtifactType { get; }
    public int SchemaVersion { get; }
    public int CanonicalEncodingVersion { get; }
    public string FullDigest { get; }

    public ArtifactIdentity(string artifactType, int schemaVersion, int canonicalEncodingVersion, string fullDigest)
    {
        if (string.IsNullOrEmpty(artifactType) || artifactType.Length > 256 ||
            artifactType.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-'))
            throw new FormatException("Invalid artifact type.");
        if (schemaVersion < 1 || canonicalEncodingVersion < 1) throw new FormatException("Invalid artifact version.");
        if (fullDigest.Length != 64 || fullDigest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new FormatException("Artifact identity requires full lowercase SHA-256.");
        ArtifactType = artifactType; SchemaVersion = schemaVersion;
        CanonicalEncodingVersion = canonicalEncodingVersion; FullDigest = fullDigest;
    }

    public SourceJson Data => SourceJson.FromObject(new
    {
        artifactType = ArtifactType, schemaVersion = SchemaVersion,
        canonicalEncodingVersion = CanonicalEncodingVersion, digest = FullDigest
    });

    public override string ToString() => ArtifactType + "/" + SchemaVersion.ToString(CultureInfo.InvariantCulture) +
        "/canonical/" + CanonicalEncodingVersion.ToString(CultureInfo.InvariantCulture) + "/sha-256/" + FullDigest;
}

/// <summary>Experimental allocations only. Formal registry assignments require separate specification approval.</summary>
public static class PrototypeArtifactRegistry
{
    public const string DependencyManifest = "coglatas.prototype.dependency-manifest";
    public const string CompilerInputAssociation = "coglatas.prototype.compiler-input-association";
    public const string MaterializationSet = "coglatas.prototype.materialization-set";
    public const string MaterializationReceipt = "coglatas.prototype.materialization-receipt";
    public static IReadOnlyList<string> ArtifactTypes { get; } = Array.AsReadOnly(new[]
    {
        DependencyManifest, CompilerInputAssociation, MaterializationSet, MaterializationReceipt
    });

    public static string Domain(string artifactType, int schemaVersion, int canonicalEncodingVersion)
    {
        if (!ArtifactTypes.Contains(artifactType, StringComparer.Ordinal) || schemaVersion != 1 || canonicalEncodingVersion != 1)
            throw new UnsupportedSourceException("Unsupported prototype artifact schema or encoding.");
        return artifactType + "/1/canonical/1";
    }
}

/// <summary>Source content and exact Branch/Proposal/Scenario context remain separate from artifact identity.</summary>
public sealed class ArtifactSourcePin
{
    public SourceRevisionContext Context { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }

    internal ArtifactSourcePin(SourceJson data)
    {
        Data = data;
        if (SourceFields.String(data.Value, "schemaId") != ProjectSource.SchemaId ||
            SourceFields.PositiveInteger(data.Value, "schemaVersion") != 1 ||
            SourceFields.PositiveInteger(data.Value, "canonicalEncodingVersion") != 1)
            throw new UnsupportedSourceException("Unsupported Source pin version.");
        Context = SourceRevisionContext.Parse(RelationFields.Child(data, "context"));
        PlanEntityReference.RequireKnownContext(Context);
        Digest = ContentDigest.Parse(RelationFields.Child(data, "digest"));
    }

    public static ArtifactSourcePin FromSource(ProjectSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(SourceJson.FromObject(new
        {
            schemaId = ProjectSource.SchemaId, schemaVersion = 1, canonicalEncodingVersion = 1,
            context = source.Context.Data.Value, digest = source.Digest.Data.Value
        }));
    }
}
