namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Original local invocation and its recording, constructed together only by executing the owned session.</summary>
public sealed class LocalCompilerRecordedInvocation
{
    public CompilerHostResponse Response { get; }
    public LocalCompilerRecordingReceipt Receipt { get; }
    private LocalCompilerRecordedInvocation(CompilerHostResponse response, LocalCompilerRecordingReceipt receipt)
    { Response = response; Receipt = receipt; }

    public static async Task<LocalCompilerRecordedInvocation> ExecuteAsync(LocalCompilerRecordingSession session,
        LocalCompilerRecordingManifest manifest, CompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var actual = await session.ExecuteAsync(manifest, request, cancellationToken).ConfigureAwait(false);
        return new(actual.Response, actual.Receipt);
    }
}
