namespace Coglatas.Domain.ProjectIde.VersionedArtifacts;

public enum ArtifactDecodeStatus { Decoded, Unsupported, Invalid, Cancelled }

/// <summary>Unsupported readers retain the original buffer for non-destructive future migration.</summary>
public sealed class ArtifactDecodeResult
{
    private readonly byte[] _original;
    public ArtifactDecodeStatus Status { get; }
    public PrototypeArtifact? Artifact { get; }

    private ArtifactDecodeResult(ArtifactDecodeStatus status, PrototypeArtifact? artifact, byte[] original)
    {
        Status = status; Artifact = artifact; _original = original;
    }

    public byte[] RecoverOriginalBytes() => (byte[])_original.Clone();

    public static ArtifactDecodeResult Decode(ReadOnlySpan<byte> bytes, ArtifactIdentity? expected = null,
        CancellationToken cancellationToken = default)
    {
        var original = bytes.ToArray();
        if (cancellationToken.IsCancellationRequested) return new(ArtifactDecodeStatus.Cancelled, null, original);
        try
        {
            var artifact = PrototypeArtifact.ReadCanonical(original);
            if (cancellationToken.IsCancellationRequested) return new(ArtifactDecodeStatus.Cancelled, null, original);
            if (expected is not null && artifact.Identity != expected)
                return new(ArtifactDecodeStatus.Invalid, null, original);
            return new(artifact.SemanticsSupported ? ArtifactDecodeStatus.Decoded : ArtifactDecodeStatus.Unsupported, artifact, original);
        }
        catch (UnsupportedSourceException) { return new(ArtifactDecodeStatus.Unsupported, null, original); }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return new(ArtifactDecodeStatus.Invalid, null, original);
        }
    }
}
