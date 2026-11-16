using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed class SyntheticPassSourceTarget
{
    public string Kind { get; }
    public string Identity { get; }
    public ProjectLocation Location { get; }
    internal SyntheticPassSourceTarget(string kind, string identity, ProjectLocation location)
    { Kind = kind; Identity = identity; Location = location; }
}

public sealed class SyntheticPassSourcePage
{
    public int Offset { get; }
    public int MaximumTargets { get; }
    public int TotalTargets { get; }
    public IReadOnlyList<SyntheticPassSourceTarget> Targets { get; }
    public ContentDigest OriginalMapDigest { get; }
    public ContentDigest OriginalRecordDigest { get; }
    internal SyntheticPassSourcePage(int offset, int maximumTargets, int totalTargets,
        IReadOnlyList<SyntheticPassSourceTarget> targets, ContentDigest originalMap, ContentDigest originalRecord)
    {
        Offset = offset; MaximumTargets = maximumTargets; TotalTargets = totalTargets;
        Targets = new TargetList(targets); OriginalMapDigest = originalMap; OriginalRecordDigest = originalRecord;
    }
    private sealed class TargetList : IReadOnlyList<SyntheticPassSourceTarget>
    {
        private readonly SyntheticPassSourceTarget[] _values;
        internal TargetList(IEnumerable<SyntheticPassSourceTarget> values) => _values = values.ToArray();
        public int Count => _values.Length;
        public SyntheticPassSourceTarget this[int index] => (uint)index < (uint)_values.Length
            ? _values[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<SyntheticPassSourceTarget> GetEnumerator()
        {
            foreach (var value in _values) yield return value;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public sealed partial class SyntheticWorkbenchViewModel
{
    private SyntheticPassSourcePage? _passSourcePage;
    private ProjectCompilerPassBoundarySnapshot? _passSourcePageBoundary;
    private ProjectSource? _passSourcePageRoot;
    private long _passSourcePageGeneration;
    private long? _passSourceNavigationSelection;
    public SyntheticPassSourcePage? PassBoundarySourcePage => CurrentPassSource &&
        ReferenceEquals(_passSourcePageBoundary, RecordedPassBoundary) && ReferenceEquals(_passSourcePageRoot, RootSource) ? _passSourcePage : null;

    /// <summary>Derive a bounded page only from the selected retained original map; no map is borrowed from an unread result.</summary>
    public bool ReadPassBoundarySourcePage(int offset = 0, int maximumTargets = 32, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearPassBoundarySourcePage();
        var generation = _passSourcePageGeneration; var rootGeneration = _rootSourceGeneration;
        var boundary = RecordedPassBoundary; var source = RootSource; var compilation = _compilation;
        bool Current() => !_disposed && generation == _passSourcePageGeneration && rootGeneration == _rootSourceGeneration &&
            CurrentPassSource && ReferenceEquals(boundary, RecordedPassBoundary) && ReferenceEquals(source, RootSource) &&
            ReferenceEquals(compilation, _compilation);
        Changed();
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (maximumTargets is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumTargets));
        token.ThrowIfCancellationRequested();
        if (!Current() || source is null || boundary?.State != CompilerPassBoundarySnapshotState.AvailableOriginalJson ||
            boundary.OriginalBoundaryMetadata is null || compilation?.Validation?.Ir is not { } ir ||
            !ir.Input.Data.Equals(compilation.Input.Data)) return false;
        var references = boundary.OriginalBoundaryMetadata.Value.GetProperty("originalBodies").EnumerateArray().Where(reference =>
            reference.GetProperty("role").GetString() == "source-map" &&
            reference.GetProperty("domain").GetString() == "coglatas.semantic-source-map/1").Take(2).ToArray();
        if (references.Length != 1 || !SourceJson.Parse(references[0].GetProperty("digest").GetRawText()).Equals(ir.SourceMapDigest.Data) ||
            !boundary.Bodies.TryGetValue("coglatas.semantic-source-map/1:" + ir.SourceMapDigest.Value, out var originalMap) ||
            !ReferenceEquals(originalMap, ir.SourceMap)) return false;
        var nodes = originalMap.Value.GetProperty("nodes"); var relations = originalMap.Value.GetProperty("relations");
        var total = checked(nodes.GetArrayLength() + relations.GetArrayLength());
        var entries = nodes.EnumerateArray().Select(entry => (Kind: "node", Identity: entry.GetProperty("irNodeId").GetString()!, Entry: entry))
            .Concat(relations.EnumerateArray().Select(entry => (Kind: "relation", Identity: entry.GetProperty("relationId").GetString()!, Entry: entry)));
        var targets = new List<SyntheticPassSourceTarget>();
        foreach (var entry in entries.Skip(offset).Take(maximumTargets))
        {
            token.ThrowIfCancellationRequested();
            var location = ProjectLocation.Parse(SourceJson.Parse(entry.Entry.GetProperty("location").GetRawText()));
            if (Resolve(location, requireSourceMap: true) != "ready") return false;
            targets.Add(new(entry.Kind, entry.Identity, location));
        }
        if (!Current()) return false;
        var page = new SyntheticPassSourcePage(offset, maximumTargets, total, targets, ir.SourceMapDigest, boundary.OriginalRecordDigest!);
        _passSourcePage = page; _passSourcePageBoundary = boundary; _passSourcePageRoot = source; Changed();
        if (token.IsCancellationRequested)
        {
            if (generation == _passSourcePageGeneration && ReferenceEquals(_passSourcePage, page))
            { ClearPassBoundarySourcePage(); Changed(); }
            token.ThrowIfCancellationRequested();
        }
        return Current() && ReferenceEquals(PassBoundarySourcePage, page);
    }

    public bool NavigatePassBoundarySource(SyntheticPassSourcePage page, SyntheticPassSourceTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(target);
        var generation = ClearSelection();
        bool Current() => !_disposed && generation == _selectionGeneration && ReferenceEquals(PassBoundarySourcePage, page);
        if (!Current() || !page.Targets.Any(item => ReferenceEquals(item, target)) || Resolve(target.Location, requireSourceMap: true) != "ready")
        { NavigationState = "unavailable"; Changed(); return false; }
        _passSourceNavigationSelection = generation; SourceTarget = target.Location; NavigationState = "ready"; Changed();
        return Current() && ReferenceEquals(SourceTarget, target.Location);
    }

    private void ClearPassBoundarySourcePage()
    {
        _passSourcePageGeneration++; _passSourcePage = null; _passSourcePageBoundary = null; _passSourcePageRoot = null;
        if (_passSourceNavigationSelection == _selectionGeneration) { SourceTarget = null; NavigationState = ""; }
        _passSourceNavigationSelection = null;
    }
}
