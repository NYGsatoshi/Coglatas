using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private CompilerScopedIr? _scopedSources;
    private CoreCompilation? _scopedCompilation;
    private CompilerHostRequest? _scopedRequest;
    private IReadOnlyList<ProjectSource> _scopedSourceViews = Array.Empty<ProjectSource>();
    private long _scopedGeneration;
    public string ScopedReadState { get; private set; } = "Idle";
    public IReadOnlyList<ProjectSource> ScopedSources => CurrentScopedSources ? _scopedSourceViews : Array.Empty<ProjectSource>();
    public ProjectSource? ScopedSource { get; private set; }
    public ProjectLocation? ScopedSourceTarget { get; private set; }
    public SourceJson? ScopedSourceRecord { get; private set; }
    private bool CurrentScopedSources => !_disposed && State == "Ready" && _desired is not null &&
        ReferenceEquals(_desired, _scopedRequest) && ReferenceEquals(_compilation, _scopedCompilation) &&
        ReferenceEquals(_compilation?.Dependencies?.ScopedIr, _scopedSources) && _scopedSources is not null;

    /// <summary>Explicit captured synthetic input admission, never current runtime authorization.</summary>
    public bool LoadSyntheticScopedSources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearScopedSources(); var generation = _scopedGeneration;
        var desired = _desired; var compilation = _compilation;
        try
        {
            if (State != "Ready" || desired is null || compilation is null || Source is null ||
                !compilation.Input.InputDigest.Equals(desired.Input.ExpectedInputDigest) || !Source.Data.Equals(compilation.Input.Source.Data) ||
                SyntheticScopedSourceProjection.Export(compilation) is not { } projection) throw new FormatException("Scoped inputs unavailable.");
            var scoped = SyntheticScopedSourceProjection.Read(compilation, projection);
            if (!compilation.Result.Stages.Any(stage => stage.StageId == "coglatas.dependency-analysis" && stage.Data.Value.GetProperty("outputs").EnumerateArray().Any(output => SourceJson.Parse(output.GetRawText()).Equals(compilation.Dependencies!.Digest.Data))))
                throw new FormatException("Scoped result stage binding mismatch.");
            foreach (var region in scoped.Regions)
            {
                var source = region.Binding.Source;
                if (!ProjectSourceCodec.Decode(source.Data.ToCanonicalBytes()).RequireSource().Data.Equals(source.Data)) throw new FormatException("Source bytes mismatch.");
                if (region.Binding.IsRoot) { if (!source.Data.Equals(Source.Data)) throw new FormatException("Root mismatch."); }
                else if (desired.Snapshots.Count(item => item.Snapshot.IsAuthorized && item.Snapshot.DeclarationsComplete && item.Snapshot.RelationsComplete &&
                    item.Snapshot.Source.Context.Equals(source.Context) && item.Snapshot.Source.Data.Equals(source.Data)) != 1)
                    throw new FormatException("An exact complete captured snapshot is required.");
            }
            _scopedSources = scoped; _scopedCompilation = compilation; _scopedRequest = desired;
            _scopedSourceViews = Array.AsReadOnly(scoped.Regions.Select(region => region.Binding.Source).ToArray()); ScopedReadState = "Ready";
        }
        catch (Exception error) when (error is FormatException or ArgumentException or UnsupportedSourceException)
        { ClearScopedSources(); ScopedReadState = "Unavailable"; }
        Changed(); return CurrentScopedSources && generation == _scopedGeneration;
    }

    public bool NavigateScopedSource(ProjectLocation location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(location);
        var generation = ClearSelection();
        if (!CurrentScopedSources) { NavigationState = "unavailable"; Changed(); return false; }
        var region = _scopedSources!.Regions.SingleOrDefault(region => region.Binding.Source.Context.Equals(location.Context));
        NavigationState = "unavailable";
        if (region is not null && (location.EntityId is not null ^ location.RelationId is not null))
        {
            var source = region.Binding.Source;
            var records = location.EntityId is { } entity
                ? region.Binding.Declarations.Where(row => row.DocumentId == location.DocumentId && row.Declaration?.EntityId == entity).Select(row => row.Data).ToArray()
                : region.Binding.Relations.Where(row => row.Relation is { } relation && relation.RelationId == location.RelationId && relation.Location.DocumentId == location.DocumentId).Select(row => row.Data).ToArray();
            var map = region.Validation!.Ir!.SourceMap.Value.GetProperty(location.EntityId is null ? "relations" : "nodes");
            var mapped = map.EnumerateArray().Select(entry => ProjectLocation.Parse(SourceJson.Parse(entry.GetProperty("location").GetRawText())))
                .Count(entry => entry.Context.Equals(location.Context) && entry.DocumentId == location.DocumentId && entry.EntityId == location.EntityId && entry.RelationId == location.RelationId);
            if (records.Length == 1 && mapped == 1)
            {
                NavigationState = SyntheticSourceNavigation.ValidatePath(source, records[0].Value, location);
                if (NavigationState == "ready") { ScopedSource = source; ScopedSourceTarget = location; ScopedSourceRecord = records[0]; }
            }
        }
        Changed(); return !_disposed && generation == _selectionGeneration && CurrentScopedSources && ScopedSourceTarget is not null;
    }

    public bool NavigateScopedDiagnostic(CompilerDependencyRegion region, CompilerDiagnostic diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(region); ArgumentNullException.ThrowIfNull(diagnostic);
        if (!CurrentScopedSources || !_scopedSources!.Regions.Any(item => ReferenceEquals(item, region)) ||
            !region.Diagnostics.Any(item => ReferenceEquals(item, diagnostic)))
        { ClearSelection(); NavigationState = "unavailable"; Changed(); return false; }
        return NavigateScopedSource(diagnostic.Location);
    }
    private void ClearScopedSelection() { ScopedSource = null; ScopedSourceTarget = null; ScopedSourceRecord = null; }
    private void ClearScopedSources()
    {
        _scopedGeneration++; ClearScopedSelection(); _scopedSources = null; _scopedCompilation = null; _scopedRequest = null;
        _scopedSourceViews = Array.Empty<ProjectSource>(); ScopedReadState = "Idle";
    }
}
