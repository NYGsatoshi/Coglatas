using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private long _passBoundaryGeneration;
    private CoreCompilation? _passBoundaryCompilation;
    private ProjectCompilerPassBoundarySnapshot? _recordedPassBoundary;
    public ProjectCompilerPassBoundarySnapshot? RecordedPassBoundary => CurrentPassSource &&
        ReferenceEquals(_passBoundaryCompilation, _compilation) ? _recordedPassBoundary : null;

    /// <summary>Explicit selected original boundary; requires the same current Source/run/result as whole-root reads.</summary>
    public bool ReadRecordedPassBoundary(ContentDigest expectedResult, int ordinal,
        CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearPassBoundarySnapshot();
        var generation = _passBoundaryGeneration; var root = _rootSourceGeneration;
        var request = _desiredPassRequest; var actual = _acceptedPassInvocation; var compilation = _compilation;
        bool Current() => !_disposed && generation == _passBoundaryGeneration && root == _rootSourceGeneration &&
            CurrentPassSource && ReferenceEquals(request, _desiredPassRequest) && ReferenceEquals(actual, _acceptedPassInvocation) &&
            ReferenceEquals(compilation, _compilation) && compilation!.Result.ResultDigest.Equals(expectedResult);
        Changed();
        if (!Current())
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        var execution = actual!.Response.Execution!;
        var snapshot = ProjectCompilerPassBoundarySnapshot.Read(execution, compilation!, execution.Observation,
            compilation!.Input.InputDigest, expectedResult, ordinal, mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _recordedPassBoundary = snapshot;
        _passBoundaryCompilation = compilation;
        var notificationCompleted = false;
        bool cancelledAtPublication;
        try
        {
            Changed();
            notificationCompleted = true;
        }
        finally
        {
            cancelledAtPublication = token.IsCancellationRequested;
            if (cancelledAtPublication && generation == _passBoundaryGeneration &&
                ReferenceEquals(_recordedPassBoundary, snapshot))
            {
                ClearPassBoundarySnapshot();
                try { Changed(); }
                catch when (!notificationCompleted)
                {
                    // Preserve the publication exception after withdrawing the cancelled boundary and its Source page.
                }
            }
        }
        if (cancelledAtPublication) token.ThrowIfCancellationRequested();
        return Current() && ReferenceEquals(RecordedPassBoundary, snapshot) && snapshot.State is not
            (CompilerPassBoundarySnapshotState.RefusedInvocation or CompilerPassBoundarySnapshotState.RefusedInputBinding or
            CompilerPassBoundarySnapshotState.RefusedWrongResult or CompilerPassBoundarySnapshotState.BoundaryNotRecorded);
    }

    private void ClearPassBoundarySnapshot()
    { _passBoundaryGeneration++; _recordedPassBoundary = null; _passBoundaryCompilation = null; ClearPassBoundarySourcePage(); }
}
