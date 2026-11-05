namespace Coglatas.Domain.ProjectIde.Artifacts;

/// <summary>Explicit assembly from frozen values; does not resolve a branch head or perform I/O.</summary>
public static class ArtifactAssembly
{
    public static PrototypeArtifact Associate(CompilerInputManifest input, PrototypeArtifact manifest)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(manifest);
        RequireType(manifest, PrototypeArtifactRegistry.DependencyManifest);
        var source = ArtifactSourcePin.FromSource(input.Source);
        if (!source.Data.Equals(manifest.Source!.Data)) throw new FormatException("Manifest does not bind the exact Source.");
        return PrototypeArtifact.Create(PrototypeArtifactRegistry.CompilerInputAssociation, source, SourceJson.FromObject(new
        {
            dependencyManifest = manifest.Identity.Data.Value,
            compilerConfiguration = input.Settings.Value,
            legacyInputDigest = input.InputDigest.Data.Value
        }));
    }

    internal static void RequireType(PrototypeArtifact artifact, string type)
    {
        artifact.RequireSupported();
        if (artifact.Identity.ArtifactType != type) throw new FormatException("Artifact role mismatch.");
    }

    /// <summary>Checks declared versus actual bindings. Complete syntax is not proof that bodies were acquired.</summary>
    public static bool MaterializationComplete(PrototypeArtifact association, PrototypeArtifact manifest, PrototypeArtifact actual)
    {
        RequireType(association, PrototypeArtifactRegistry.CompilerInputAssociation);
        RequireType(manifest, PrototypeArtifactRegistry.DependencyManifest);
        RequireType(actual, PrototypeArtifactRegistry.MaterializationSet);
        RequireSameSource(association, manifest, actual);
        RequirePin(association, "dependencyManifest", manifest.Identity);
        RequirePin(actual, "dependencyManifest", manifest.Identity);
        var declared = SourceFields.Array(manifest.Data.Value.GetProperty("payload"), "entries").EnumerateArray().ToArray();
        var observed = SourceFields.Array(actual.Data.Value.GetProperty("payload"), "entries").EnumerateArray().ToArray();
        if (declared.Length != observed.Length) return false;
        var complete = SourceFields.String(actual.Data.Value.GetProperty("payload"), "enumeration") == "completed";
        for (var index = 0; index < declared.Length; index++)
        {
            if (SourceFields.String(declared[index], "entryKey") != SourceFields.String(observed[index], "entryKey") ||
                !SourceJson.FromElement(declared[index].GetProperty("expected")).Equals(SourceJson.FromElement(observed[index].GetProperty("expected"))))
                throw new FormatException("Declared and actual materialization bindings disagree.");
            complete &= SourceFields.String(observed[index], "state") == "present" &&
                SourceFields.String(declared[index], "coverageClaim") == "complete";
        }
        return complete;
    }

    public static bool BindReceipt(PrototypeArtifact receipt, PrototypeArtifact association, PrototypeArtifact manifest, PrototypeArtifact actual)
    {
        RequireType(receipt, PrototypeArtifactRegistry.MaterializationReceipt);
        RequireSameSource(receipt, association, manifest, actual);
        RequirePin(receipt, "compilerInput", association.Identity);
        RequirePin(receipt, "actualSet", actual.Identity);
        return MaterializationComplete(association, manifest, actual);
    }

    internal static void RequireSameSource(params PrototypeArtifact[] artifacts)
    {
        foreach (var artifact in artifacts)
            if (artifact.Source is null || !artifact.Source.Data.Equals(artifacts[0].Source!.Data))
                throw new FormatException("Artifact Source scope mismatch.");
    }

    internal static void RequirePin(PrototypeArtifact owner, string field, ArtifactIdentity expected)
    {
        var value = owner.Data.Value.GetProperty("payload").GetProperty(field);
        if (!SourceJson.FromElement(value).Equals(expected.Data)) throw new FormatException("Exact artifact binding mismatch.");
    }
}

public enum ArtifactImportState { Imported, Incomplete, Unsupported, Cancelled, Exhausted }

/// <summary>Content-addressed synthetic offline index with atomic copy-on-write import and no network capability.</summary>
public sealed class PrototypeArtifactStore
{
    private readonly object _sync = new();
    private Dictionary<ArtifactIdentity, PrototypeArtifact> _artifacts = [];
    public int Count { get { lock (_sync) return _artifacts.Count; } }

    public PrototypeArtifact? Find(ArtifactIdentity identity)
    {
        lock (_sync) return _artifacts.GetValueOrDefault(identity);
    }

    public ArtifactImportState Import(ArtifactIdentity root, IEnumerable<ReadOnlyMemory<byte>> buffers,
        IEnumerable<ProjectSource> sources, CancellationToken cancellationToken = default,
        int maximumArtifacts = 4096, long maximumBytes = 16_777_216)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(buffers); ArgumentNullException.ThrowIfNull(sources);
        if (maximumArtifacts <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumArtifacts));
        if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
        // All suppliers and parsing finish before publication. Exceptions leave the previous index intact.
        var staged = new Dictionary<ArtifactIdentity, PrototypeArtifact>();
        long bytes = 0;
        using (var iterator = buffers.GetEnumerator())
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
                if (staged.Count >= maximumArtifacts) return ArtifactImportState.Exhausted;
                var hasNext = iterator.MoveNext();
                if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
                if (!hasNext) break;
                var buffer = iterator.Current;
                if (buffer.Length > maximumBytes - bytes) return ArtifactImportState.Exhausted;
                bytes += buffer.Length;
                var artifact = PrototypeArtifact.ReadCanonical(buffer.Span);
                if (!artifact.SemanticsSupported) return ArtifactImportState.Unsupported;
                if (!staged.TryAdd(artifact.Identity, artifact)) throw new FormatException("Duplicate offline artifact entry.");
            }
        }
        var sourcePins = new Dictionary<string, ProjectSource>(StringComparer.Ordinal);
        using (var iterator = sources.GetEnumerator())
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
                if (sourcePins.Count >= maximumArtifacts) return ArtifactImportState.Exhausted;
                var hasNext = iterator.MoveNext();
                if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
                if (!hasNext) break;
                var source = iterator.Current;
                var sourceBytes = ProjectSourceCodec.Encode(source);
                if (sourceBytes.Length > maximumBytes - bytes) return ArtifactImportState.Exhausted;
                bytes += sourceBytes.Length;
                if (ProjectSourceCodec.Decode(sourceBytes).Status != SourceDecodeStatus.StructurallyDecoded)
                    return ArtifactImportState.Unsupported;
                if (!sourcePins.TryAdd(ArtifactSourcePin.FromSource(source).Data.CanonicalText, source))
                    throw new FormatException("Duplicate offline Source entry.");
            }
        }
        lock (_sync)
        {
            var candidate = new Dictionary<ArtifactIdentity, PrototypeArtifact>(_artifacts);
            foreach (var (identity, artifact) in staged)
            {
                if (candidate.TryGetValue(identity, out var existing) && !existing.Data.Equals(artifact.Data))
                    throw new FormatException("One identity cannot name different canonical bytes.");
                candidate[identity] = artifact;
            }
            if (!candidate.ContainsKey(root)) return ArtifactImportState.Incomplete;
            // Iterative traversal avoids recursive depth exhaustion. Every supplied artifact is validated.
            var visited = new HashSet<ArtifactIdentity>();
            var frontier = new Stack<ArtifactIdentity>(staged.Keys.Append(root));
            while (frontier.TryPop(out var identity))
            {
                if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
                if (!visited.Add(identity)) continue;
                if (visited.Count > maximumArtifacts) return ArtifactImportState.Exhausted;
                if (!candidate.TryGetValue(identity, out var artifact)) return ArtifactImportState.Incomplete;
                if (!sourcePins.TryGetValue(artifact.Source!.Data.CanonicalText, out var source)) return ArtifactImportState.Incomplete;
                foreach (var dependency in artifact.Dependencies) frontier.Push(dependency);
                if (identity.ArtifactType == PrototypeArtifactRegistry.CompilerInputAssociation)
                {
                    var configuration = RelationFields.Child(RelationFields.Child(artifact.Data, "payload"), "compilerConfiguration");
                    var input = new CompilerInputManifest(source, configuration);
                    var payload = artifact.Data.Value.GetProperty("payload");
                    var legacyDigest = ContentDigest.Parse(SourceJson.FromElement(payload.GetProperty("legacyInputDigest")));
                    if (input.InputDigest.Value != legacyDigest.Value) throw new FormatException("Compiler configuration binding mismatch.");
                    var manifest = Resolve(candidate, payload, "dependencyManifest");
                    if (manifest is null) return ArtifactImportState.Incomplete;
                    if (ArtifactAssembly.Associate(input, manifest).Identity != identity)
                        throw new FormatException("CompilerInput association mismatch.");
                }
                else if (identity.ArtifactType == PrototypeArtifactRegistry.MaterializationSet)
                {
                    var manifest = Resolve(candidate, artifact.Data.Value.GetProperty("payload"), "dependencyManifest");
                    if (manifest is null) return ArtifactImportState.Incomplete;
                    ArtifactAssembly.RequireSameSource(artifact, manifest);
                    var declared = SourceFields.Array(manifest.Data.Value.GetProperty("payload"), "entries").EnumerateArray().ToArray();
                    var observed = SourceFields.Array(artifact.Data.Value.GetProperty("payload"), "entries").EnumerateArray().ToArray();
                    foreach (var item in observed)
                    {
                        var key = SourceFields.String(item, "entryKey");
                        var expected = declared.SingleOrDefault(e => SourceFields.String(e, "entryKey") == key);
                        if (expected.ValueKind == System.Text.Json.JsonValueKind.Undefined ||
                            !SourceJson.FromElement(expected.GetProperty("expected")).Equals(SourceJson.FromElement(item.GetProperty("expected"))))
                            throw new FormatException("Actual entry is not declared in the exact Manifest.");
                    }
                }
                else if (identity.ArtifactType == PrototypeArtifactRegistry.MaterializationReceipt)
                {
                    var payload = artifact.Data.Value.GetProperty("payload");
                    var association = Resolve(candidate, payload, "compilerInput");
                    var actual = Resolve(candidate, payload, "actualSet");
                    if (association is null || actual is null) return ArtifactImportState.Incomplete;
                    var manifest = Resolve(candidate, association.Data.Value.GetProperty("payload"), "dependencyManifest");
                    if (manifest is null) return ArtifactImportState.Incomplete;
                    _ = ArtifactAssembly.BindReceipt(artifact, association, manifest, actual);
                }
            }
            if (cancellationToken.IsCancellationRequested) return ArtifactImportState.Cancelled;
            _artifacts = candidate;
            return ArtifactImportState.Imported;
        }

    }

    private static PrototypeArtifact? Resolve(Dictionary<ArtifactIdentity, PrototypeArtifact> artifacts,
        System.Text.Json.JsonElement payload, string field)
    {
        var pin = payload.GetProperty(field);
        return artifacts.GetValueOrDefault(new(SourceFields.String(pin, "artifactType"),
            SourceFields.PositiveInteger(pin, "schemaVersion"), SourceFields.PositiveInteger(pin, "canonicalEncodingVersion"), SourceFields.String(pin, "digest")));
    }
}
