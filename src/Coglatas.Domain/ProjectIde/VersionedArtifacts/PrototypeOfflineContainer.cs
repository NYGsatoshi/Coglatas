using System.Buffers.Binary;

namespace Coglatas.Domain.ProjectIde.VersionedArtifacts;

/// <summary>
/// Experimental in-memory framing, not a formal artifact type or an adopted interchange format.
/// CGPC0001, uint32 big-endian frame count, then (byte kind, uint32 length, body).
/// Kind 1 is an existing canonical prototype artifact; kind 2 is an existing Source envelope.
/// Origin is an opaque caller label and grants no authority. No paths are opened or extracted.
/// </summary>
public sealed class PrototypeOfflineContainer
{
    private readonly byte[] _original;
    private readonly ReadOnlyMemory<byte>[] _artifacts;
    private readonly SourceDecodeResult[] _sources;
    public string Origin { get; }
    public ArtifactIdentity Root { get; }

    private PrototypeOfflineContainer(string origin, ArtifactIdentity root, byte[] original,
        ReadOnlyMemory<byte>[] artifacts, SourceDecodeResult[] sources)
    {
        Origin = origin; Root = root; _original = original; _artifacts = artifacts; _sources = sources;
    }

    public byte[] RecoverOriginalBytes() => (byte[])_original.Clone();

    public byte[]? RecoverSourceBytes(ArtifactSourcePin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        return _sources.SingleOrDefault(item => ArtifactSourcePin.FromSource(item.RequireSource()).Data.Equals(pin.Data))?.RecoverOriginalBytes();
    }

    internal IEnumerable<ReadOnlyMemory<byte>> ArtifactBuffers => _artifacts;
    internal IEnumerable<ProjectSource> Sources => _sources.Select(item => item.RequireSource());

    internal static (ArtifactImportState State, PrototypeOfflineContainer? Container) Read(
        ArtifactIdentity root, string origin, ReadOnlySpan<byte> bytes, CancellationToken cancellationToken,
        int maximumArtifacts, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 256 || origin.Any(char.IsControl))
            throw new ArgumentException("A bounded opaque origin label is required.", nameof(origin));
        if (maximumArtifacts <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumArtifacts));
        if (cancellationToken.IsCancellationRequested) return (ArtifactImportState.Cancelled, null);
        if (bytes.Length > maximumBytes) return (ArtifactImportState.Exhausted, null);
        // Freeze caller-owned memory before validating or retaining it.
        var original = bytes.ToArray();
        var remaining = original.AsSpan();
        if (remaining.Length < 12) throw new FormatException("Truncated prototype offline header.");
        if (!remaining[..8].SequenceEqual("CGPC0001"u8)) return (ArtifactImportState.Unsupported, null);
        var count = BinaryPrimitives.ReadUInt32BigEndian(remaining.Slice(8, 4));
        remaining = remaining[12..];
        if (count > 2L * maximumArtifacts) return (ArtifactImportState.Exhausted, null);
        var artifacts = new List<ReadOnlyMemory<byte>>();
        var sources = new List<SourceDecodeResult>();
        var artifactPins = new HashSet<ArtifactIdentity>();
        var sourcePins = new HashSet<string>(StringComparer.Ordinal);
        for (long index = 0; index < count; index++)
        {
            if (cancellationToken.IsCancellationRequested) return (ArtifactImportState.Cancelled, null);
            if (remaining.Length < 5) throw new FormatException("Truncated prototype offline frame.");
            var kind = remaining[0];
            var length = BinaryPrimitives.ReadUInt32BigEndian(remaining.Slice(1, 4));
            remaining = remaining[5..];
            if (length > remaining.Length) throw new FormatException("Truncated prototype offline body.");
            var body = remaining[..(int)length];
            remaining = remaining[(int)length..];
            if (kind == 1)
            {
                if (artifacts.Count >= maximumArtifacts) return (ArtifactImportState.Exhausted, null);
                var decoded = ArtifactDecodeResult.Decode(body, cancellationToken: cancellationToken);
                if (decoded.Status == ArtifactDecodeStatus.Cancelled) return (ArtifactImportState.Cancelled, null);
                if (decoded.Status == ArtifactDecodeStatus.Unsupported) return (ArtifactImportState.Unsupported, null);
                if (decoded.Status != ArtifactDecodeStatus.Decoded) throw new FormatException("Invalid offline artifact body.");
                if (!artifactPins.Add(decoded.Artifact!.Identity)) throw new FormatException("Duplicate offline artifact frame.");
                artifacts.Add(decoded.RecoverOriginalBytes());
            }
            else if (kind == 2)
            {
                if (sources.Count >= maximumArtifacts) return (ArtifactImportState.Exhausted, null);
                var decoded = ProjectSourceCodec.Decode(body, cancellationToken: cancellationToken);
                if (decoded.Status == SourceDecodeStatus.Cancelled) return (ArtifactImportState.Cancelled, null);
                if (decoded.Status == SourceDecodeStatus.Unsupported) return (ArtifactImportState.Unsupported, null);
                if (decoded.Status != SourceDecodeStatus.StructurallyDecoded) throw new FormatException("Invalid offline Source body.");
                if (!sourcePins.Add(ArtifactSourcePin.FromSource(decoded.RequireSource()).Data.CanonicalText))
                    throw new FormatException("Duplicate or conflicting offline Source frame.");
                sources.Add(decoded);
            }
            else return (ArtifactImportState.Unsupported, null);
        }
        if (!remaining.IsEmpty) throw new FormatException("Trailing bytes after prototype offline frames.");
        if (cancellationToken.IsCancellationRequested) return (ArtifactImportState.Cancelled, null);
        return (ArtifactImportState.Imported, new(origin, root, original, artifacts.ToArray(), sources.ToArray()));
    }
}
