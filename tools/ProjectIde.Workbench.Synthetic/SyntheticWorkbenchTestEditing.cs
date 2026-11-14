using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private long _testEditGeneration;
    public ProjectTestEditPreview? TestEditPreview { get; private set; }
    public ProjectTestDefinitionExport? ExportTestDefinition(ProjectLocation target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (State != "Ready" || _compilation?.Model is not { } model || _desired is null) return null;
        return ProjectTestEditing.Export(model, target, _desired.Input.ExpectedInputDigest, cancellationToken);
    }
    public ProjectTestEditPreview? PreviewTestDefinition(ProjectLocation target, SourceJson definition, SourceRevisionContext next,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(next); cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _lifecycle; var generation = ++_testEditGeneration; var selection = _selectionGeneration;
        if (TestEditPreview is not null) { TestEditPreview = null; Changed(); }
        if (_disposed || lifecycle != _lifecycle || generation != _testEditGeneration || selection != _selectionGeneration ||
            State != "Ready" || _compilation?.Model is not { } model || _desired is null) return null;
        var preview = ProjectTestEditing.Preview(model, target, _desired.Input.ExpectedInputDigest, definition, next, cancellationToken);
        if (_disposed || lifecycle != _lifecycle || generation != _testEditGeneration || selection != _selectionGeneration) return null;
        TestEditPreview = preview; Changed();
        if (!_disposed && lifecycle == _lifecycle && generation == _testEditGeneration && selection == _selectionGeneration &&
            ReferenceEquals(TestEditPreview, preview)) return preview;
        // Clear only this interrupted publication; a reentrant newer preview belongs to its own generation.
        if (ReferenceEquals(TestEditPreview, preview)) { ClearTestEdit(); Changed(); }
        return null;
    }
    public CompilerHostRequest? ApplyPreviewedTestDefinition(ProjectTestEditPlan plan, long clientGeneration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        if (State != "Ready" || !ReferenceEquals(TestEditPreview?.Plan, plan) || _compilation is null || _desired is null ||
            clientGeneration <= _lastGeneration || Resolve(plan.Target) != "ready" ||
            plan.ApplyToDraft(_compilation.Input, cancellationToken) is not { } after) return null;
        var request = new CompilerHostRequest(new(after), clientGeneration, _desired.Snapshots, _desired.Units, _desired.Calendars);
        BeginSynthetic(request); return !_disposed && ReferenceEquals(_desired, request) ? request : null;
    }
    private void ClearTestEdit() { _testEditGeneration++; TestEditPreview = null; }
}
