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
    public string State { get; private set; } = "Empty";
    public ProjectSource? Source { get; private set; }
    public CompilerResult? Result { get; private set; }
    public CompilerSemanticInspection? Inspection { get; private set; }
    public CompilerInspectedSymbol? Selected { get; private set; }
    public ProjectLocation? SourceTarget { get; private set; }
    public IReadOnlyList<CompilerDiagnostic> Problems => Result?.Diagnostics ?? Array.Empty<CompilerDiagnostic>();
    public event PropertyChangedEventHandler? PropertyChanged;

    public void BeginSynthetic(CompilerHostRequest desired)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(desired);
        if (desired.ClientGeneration <= _lastGeneration) throw new ArgumentException("A new lifecycle needs a strictly newer generation.", nameof(desired));
        Reset();
        // Materialize before publishing context, so malformed input cannot leave a partial selection.
        var source = desired.Input.Materialize().Source;
        _lastGeneration = desired.ClientGeneration;
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
        if (response.Status != CompilerHostStatus.Completed || response.Compilation is not { Model: { } model } compilation ||
            response.Execution is not { } execution ||
            CompilerHostFreshnessAssessment.Assess(response, _desired).State != CompilerFreshness.Current ||
            !execution.InputBindingDigest.Equals(_desired.ExecutionInputDigest) ||
            !compilation.Input.Data.Equals(compilation.Result.Input.Data) || !compilation.Input.Source.Data.Equals(Source?.Data))
        {
            return false;
        }
        var inspection = new CompilerSemanticInspection(model);
        ClearResult(); Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed(); return true;
    }

    public bool Select(ProjectLocation location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Selected = Inspection?.Inspect(location); SourceTarget = null; Changed(); return Selected is not null;
    }

    public bool Navigate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SourceTarget = Selected?.Symbol.Location; Changed(); return SourceTarget is not null;
    }

    public void Reset()
    {
        _desired = null; Source = null; ClearResult(); State = "Empty"; Changed();
    }
    private void ClearResult() { Result = null; Inspection = null; Selected = null; SourceTarget = null; }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { Reset(); _disposed = true; }
}
