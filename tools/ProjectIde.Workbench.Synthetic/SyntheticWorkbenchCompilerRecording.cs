using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private LocalCompilerRecordingReceipt? _compilerRecordingReceipt;
    private CoreCompilation? _recordingCompilation;
    private long _recordingGeneration;
    public LocalCompilerRecordingReceipt? CompilerRecordingReceipt => CurrentScopedSources &&
        ReferenceEquals(_compilation, _recordingCompilation) ? _compilerRecordingReceipt : null;

    /// <summary>Read an already executed owned local invocation. Runs no Compiler and grants no current runtime authorization.</summary>
    public bool ReadSyntheticCompilerRecording(LocalCompilerRecordedInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(invocation);
        ClearCompilerRecording(); var generation = _recordingGeneration;
        var compilation = _compilation; var request = _desired; var scopedGeneration = _scopedGeneration;
        bool Current() => !_disposed && generation == _recordingGeneration && scopedGeneration == _scopedGeneration && CurrentScopedSources &&
            compilation is not null && ReferenceEquals(compilation, _compilation) && ReferenceEquals(request, _desired);
        Changed(); if (!Current()) return false;
        var response = invocation.Response; var receipt = invocation.Receipt;
        if (!ReferenceEquals(response.Compilation, compilation) || request is null ||
            CompilerHostFreshnessAssessment.Assess(response, request).State != CompilerFreshness.Current ||
            response.AttemptId != receipt.AttemptId || receipt.Manifest.ClientGeneration != request.ClientGeneration ||
            !receipt.Manifest.InputDigest.Equals(request.Input.ExpectedInputDigest) ||
            !receipt.Manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest) || response.Execution is not { } execution ||
            receipt.Snapshot is not { } snapshot) return false;
        var data = receipt.Data.Value;
        if (!SourceJson.Parse(data.GetProperty("hostExecutionDigest").GetRawText()).Equals(execution.Digest.Data) ||
            !SourceJson.Parse(data.GetProperty("resultDigest").GetRawText()).Equals(compilation!.Result.ResultDigest.Data) ||
            !SourceJson.Parse(data.GetProperty("captureDigest").GetRawText()).Equals(snapshot.Digest.Data) || !Current()) return false;
        _compilerRecordingReceipt = receipt; _recordingCompilation = compilation; Changed();
        return Current() && ReferenceEquals(CompilerRecordingReceipt, receipt);
    }

    private void ClearCompilerRecording()
    {
        _recordingGeneration++; _compilerRecordingReceipt = null; _recordingCompilation = null;
    }
}
