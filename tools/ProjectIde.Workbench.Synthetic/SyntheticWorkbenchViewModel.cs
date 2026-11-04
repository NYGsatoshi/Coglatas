using System.ComponentModel;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Pure .NET synthetic ViewModel preparation for Windows. No Avalonia activation, real data or access grant.</summary>
public sealed class SyntheticWorkbenchViewModel : INotifyPropertyChanged, IDisposable
{
    private CompilerHostRequest? _desired;
    private bool _disposed;
    private long _lastGeneration = -1;
    private long _lifecycle;
    private long _selectionGeneration;
    private CoreCompilation? _compilation;
    public string State { get; private set; } = "Empty";
    public ProjectSource? Source { get; private set; }
    public CompilerResult? Result { get; private set; }
    public CompilerSemanticInspection? Inspection { get; private set; }
    public CompilerInspectedSymbol? Selected { get; private set; }
    public CompilerDiagnostic? SelectedDiagnostic { get; private set; }
    public string NavigationState { get; private set; } = "";
    public ProjectLocation? SourceTarget { get; private set; }
    public IReadOnlyList<CompilerDiagnostic> Problems => Result?.Diagnostics ?? Array.Empty<CompilerDiagnostic>();
    public event PropertyChangedEventHandler? PropertyChanged;

    public void BeginSynthetic(CompilerHostRequest desired)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(desired);
        if (desired.ClientGeneration <= _lastGeneration) throw new ArgumentException("A new lifecycle needs a strictly newer generation.", nameof(desired));
        var lifecycle = ++_lifecycle;
        _lastGeneration = desired.ClientGeneration;
        ClearContext(); Changed();
        // A synchronous notification may reset, dispose or begin a newer context.
        if (_disposed || lifecycle != _lifecycle) return;
        // Materialize before publishing context, so malformed input cannot leave a partial selection.
        var source = desired.Input.Materialize().Source;
        _desired = desired; Source = source; State = "Waiting"; Changed();
    }

    public bool TryAccept(CompilerHostResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (_disposed || _desired is null || State != "Waiting") return false;
        if (response.ClientGeneration != _desired.ClientGeneration || !response.RequestedInputDigest.Equals(_desired.Input.ExpectedInputDigest)) return false;
        if (response.Status != CompilerHostStatus.Completed)
        {
            if (!Enum.IsDefined(response.Status)) return false;
            ClearResult(); State = response.Status.ToString(); Changed(); return false;
        }
        if (response.Compilation is not { Model: { } model } compilation ||
            response.Execution is not { } execution ||
            CompilerHostFreshnessAssessment.Assess(response, _desired).State != CompilerFreshness.Current ||
            !execution.InputBindingDigest.Equals(_desired.ExecutionInputDigest) ||
            !compilation.Input.Data.Equals(compilation.Result.Input.Data) || !compilation.Input.Source.Data.Equals(Source?.Data))
        {
            return false;
        }
        var inspection = new CompilerSemanticInspection(model);
        var lifecycle = _lifecycle;
        ClearResult(); _compilation = compilation; Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed();
        return !_disposed && lifecycle == _lifecycle && ReferenceEquals(Result, compilation.Result);
    }

    public bool Select(ProjectLocation location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(location);
        var generation = ClearSelection();
        NavigationState = Resolve(location);
        if (NavigationState == "ready") Selected = Inspection?.Inspect(location);
        if (Selected is null && NavigationState == "ready") NavigationState = "unavailable";
        Changed(); return !_disposed && generation == _selectionGeneration && Selected is not null;
    }

    public bool SelectDiagnostic(CompilerDiagnostic diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(diagnostic);
        var generation = ClearSelection();
        var matches = Problems.Where(item => item.DiagnosticId == diagnostic.DiagnosticId).Take(2).ToArray();
        if (matches.Length == 1 && ReferenceEquals(matches[0], diagnostic))
        {
            SelectedDiagnostic = matches[0];
            NavigationState = Resolve(matches[0].Location);
        }
        else NavigationState = "unavailable";
        Changed(); return !_disposed && generation == _selectionGeneration && SelectedDiagnostic is not null;
    }

    public bool Navigate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = ++_selectionGeneration;
        SourceTarget = null;
        var location = SelectedDiagnostic?.Location ?? Selected?.Symbol.Location;
        NavigationState = location is null ? "unavailable" : Resolve(location);
        if (NavigationState == "ready") SourceTarget = location;
        Changed(); return !_disposed && generation == _selectionGeneration && SourceTarget is not null;
    }

    public void Reset()
    {
        _lifecycle++; ClearContext(); Changed();
    }
    private void ClearContext() { _desired = null; Source = null; ClearResult(); State = "Empty"; }
    private long ClearSelection()
    {
        Selected = null; SelectedDiagnostic = null; SourceTarget = null; NavigationState = "";
        return ++_selectionGeneration;
    }
    private void ClearResult() { _compilation = null; Result = null; Inspection = null; ClearSelection(); }
    private string Resolve(ProjectLocation location) => SyntheticSourceNavigation.Resolve(_desired, Source, _compilation, location);
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
