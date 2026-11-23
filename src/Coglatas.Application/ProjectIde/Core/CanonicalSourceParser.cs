using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

public sealed class CompilerSourceParseResult
{
    private readonly byte[] _buffer;
    public SourceDecodeStatus Status { get; }
    public ProjectSource? Source { get; }
    public ContentDigest BufferDigest { get; }
    public SourceRevisionContext Context { get; }
    internal CompilerSourceParseResult(byte[] buffer, SourceDecodeStatus status, ProjectSource? source, SourceRevisionContext context)
    {
        _buffer = (byte[])buffer.Clone(); Status = status; Source = source; Context = context;
        BufferDigest = ContentDigest.Compute("coglatas.raw-source-buffer/1", buffer);
    }
    public byte[] RecoverBuffer() => (byte[])_buffer.Clone();
}

public static class CanonicalSourceParser
{
    public static CompilerSourceParseResult Parse(ReadOnlySpan<byte> buffer, SourceRevisionContext context,
        string format = ProjectSource.Format, SourceCodecLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (format != ProjectSource.Format) return new(buffer.ToArray(), SourceDecodeStatus.Unsupported, null, context);
        var decoded = ProjectSourceCodec.Decode(buffer, limits);
        if (decoded.Source is { } source && !source.Context.Equals(context)) return new(buffer.ToArray(), SourceDecodeStatus.Invalid, null, context);
        return new(buffer.ToArray(), decoded.Status, decoded.Source, context);
    }
}
