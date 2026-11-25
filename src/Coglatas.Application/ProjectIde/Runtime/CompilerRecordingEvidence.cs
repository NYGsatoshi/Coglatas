using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

/// <summary>Historical manifest identities, with no Source, diagnostic or captured output bodies.</summary>
public sealed record CompilerRecordingEvidence(
    SecuritySourceIdentitySnapshot Source,
    string SourceDigest,
    Guid RunId,
    Guid AttemptId,
    Guid CompilerInputId,
    string ExecutionInputDigest,
    string MaterializationsDigest,
    string ManifestDigest,
    string ReceiptDigest,
    CompilerHostStatus HostStatus,
    CompilerOutputSnapshotMode CaptureMode,
    CompilerOutputSnapshotState? OriginalCaptureState,
    string? CaptureDigest,
    Guid? OriginRunId,
    string? OriginReceiptDigest,
    CompilerRecordingResultEvidence? Result)
{
    public static CompilerRecordingEvidence FromInvocation(LocalCompilerRecordedInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var receipt = invocation.Receipt;
        var manifest = receipt.Manifest;
        var execution = invocation.Response.Execution;
        return new(manifest.SourceIdentity, manifest.SourceDigest.Value, manifest.RunId, receipt.AttemptId,
            Domain.ProjectIde.CompilerInputId.Derive(manifest.InputDigest).Value, manifest.ExecutionInputDigest.Value, manifest.MaterializationsDigest.Value,
            manifest.Digest.Value, receipt.Digest.Value, invocation.Response.Status, manifest.Mode,
            receipt.Snapshot?.State, receipt.Snapshot?.Digest.Value, manifest.OriginRunId, manifest.OriginReceiptDigest?.Value,
            execution is null ? null : CaptureResult(execution));
    }

    private static CompilerRecordingResultEvidence CaptureResult(CompilerHostExecutionReceipt execution)
    {
        var result = execution.Compilation.Result;
        return new(result.ResultId.Value, result.ResultDigest.Value, execution.Digest.Value,
            ContentDigest.Compute("coglatas.compiler-evidence/1", result.EvidenceManifest.ToCanonicalBytes()).Value,
            result.Execution, result.Coverage, result.Verdict, result.EvidenceComplete);
    }
}

public sealed record CompilerRecordingResultEvidence(
    Guid CompilerResultId,
    string ResultDigest,
    string HostExecutionDigest,
    string EvidenceManifestDigest,
    CompilerExecutionState Execution,
    CompilerCoverage Coverage,
    CompilerVerdict Verdict,
    bool EvidenceComplete);

public enum CompilerRecordingWriteOutcome { Unavailable, Recorded, AlreadyRecorded }

public static class CompilerRecordingOperations
{
    public static SecurityOperationRef Record { get; } = new("coglatas.projectide.compiler-recording.record");
    public static SecurityOperationRef Read { get; } = new("coglatas.projectide.compiler-recording.read");
}

/// <summary>Application boundary for server-owned historical receipts.</summary>
public interface ICompilerRecordingStore
{
    Task<CompilerRecordingWriteOutcome> RecordAsync(LocalCompilerRecordedInvocation invocation, CancellationToken cancellationToken);

    Task<CompilerRecordingEvidence?> FindAsync(SecuritySourceIdentitySnapshot source, string sourceDigest, Guid runId,
        CancellationToken cancellationToken);
}
