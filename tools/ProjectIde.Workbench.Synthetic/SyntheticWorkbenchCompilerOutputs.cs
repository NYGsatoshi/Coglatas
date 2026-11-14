using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectCompilerOutputSnapshot? _compilerOutputSnapshot;
    private CoreCompilation? _outputSnapshotCompilation;
    private long _outputSnapshotGeneration;
    public ProjectCompilerOutputSnapshot? CompilerOutputSnapshot => CurrentScopedSources &&
        ReferenceEquals(_compilation, _outputSnapshotCompilation) ? _compilerOutputSnapshot : null;

    /// <summary>Read already produced outputs of the owned admitted synthetic compilation. Runs no compiler or external operation.</summary>
    public bool ReadSyntheticCompilerOutputs(ContentDigest expectedResult, CompilerOutputSnapshotMode mode = CompilerOutputSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearCompilerOutputSnapshot(); var generation = _outputSnapshotGeneration;
        var compilation = _compilation; var request = _desired; var scopedGeneration = _scopedGeneration;
        bool Current() => !_disposed && generation == _outputSnapshotGeneration && scopedGeneration == _scopedGeneration && CurrentScopedSources &&
            compilation is not null && ReferenceEquals(compilation, _compilation) && ReferenceEquals(request, _desired);
        Changed(); if (!Current()) return false;
        var snapshot = ProjectCompilerOutputSnapshot.Read(compilation!, expectedResult, mode, maximumBodyBytes, token, compilation!.Input);
        if (!Current()) return false;
        _compilerOutputSnapshot = snapshot; _outputSnapshotCompilation = compilation; Changed();
        return Current() && ReferenceEquals(CompilerOutputSnapshot, snapshot) && snapshot.State is not
            (CompilerOutputSnapshotState.RefusedWrongResult or CompilerOutputSnapshotState.RefusedInputBinding or CompilerOutputSnapshotState.RefusedOutputBinding);
    }
    private void ClearCompilerOutputSnapshot()
    {
        _outputSnapshotGeneration++; _compilerOutputSnapshot = null; _outputSnapshotCompilation = null;
    }
}
