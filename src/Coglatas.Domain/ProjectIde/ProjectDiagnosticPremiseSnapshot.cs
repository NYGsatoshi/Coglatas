using System.Text;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum DiagnosticPremiseSnapshotMode { Manifest, AvailableBodies }
public enum DiagnosticPremiseSnapshotState
{
    ManifestOnly, CapturedKnownBodies, NoRecordedPremises, NoKnownBodies, BodyBudgetExceeded, LookupLimitReached,
    RefusedWrongResult, RefusedDiagnostic, RefusedInputBinding, RefusedBodyBinding
}
public enum DiagnosticPremiseBodyKind { RootInput, BindingRegion, AnalysisRegion, RootSource }
public enum DiagnosticPremiseResolution { ResolvedRetainedObject, Unavailable, Ambiguous, LookupLimit }

public sealed class DiagnosticPremiseReference
{
    public ContentDigest Digest { get; }
    public DiagnosticPremiseBodyKind? Kind { get; }
    public DiagnosticPremiseResolution Resolution { get; }
    internal DiagnosticPremiseReference(ContentDigest digest, DiagnosticPremiseBodyKind? kind, DiagnosticPremiseResolution resolution)
    { Digest = digest; Kind = kind; Resolution = resolution; }
}

/// <summary>Reads already retained input objects for an owned finding; no reason DAG, execution or runtime authority is reconstructed.</summary>
public sealed class ProjectDiagnosticPremiseSnapshot
{
    public const string Format = "coglatas.local-diagnostic-premise-snapshot/1";
    public const int MaximumObjectLookups = 4096;
    public DiagnosticPremiseSnapshotState State { get; }
    public DiagnosticPremiseSnapshotMode Mode { get; }
    public string BodyScope { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public bool LookupComplete { get; }
    public CompilerDiagnostic? Diagnostic { get; }
    public ContentDigest? InputDigest { get; }
    public ContentDigest? ResultDigest { get; }
    public IReadOnlyList<DiagnosticPremiseReference> References { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectDiagnosticPremiseSnapshot(DiagnosticPremiseSnapshotState state, DiagnosticPremiseSnapshotMode mode, int budget, string bodyScope,
        CoreCompilation? compilation = null, CompilerDiagnostic? diagnostic = null, IEnumerable<DiagnosticPremiseReference>? references = null,
        Dictionary<string, SourceJson>? bodies = null, bool lookupComplete = false)
    {
        State = state; Mode = mode; MaximumBodyBytes = budget; BodyScope = bodyScope; LookupComplete = lookupComplete; Diagnostic = diagnostic;
        InputDigest = compilation?.Input.InputDigest; ResultDigest = compilation?.Result.ResultDigest;
        References = new ImmutableListView<DiagnosticPremiseReference>(references ?? []);
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText));
        Data = SourceJson.Parse(JsonSerializer.Serialize(new {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes = budget, retainedBodyBytes = RetainedBodyBytes,
            lookupComplete, bodyScope = BodyScope, relationshipStructure = "NotRecorded", earlierStates = "NotRecorded",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            basis = compilation is null || diagnostic is null ? null : new {
                inputDigest = compilation.Input.InputDigest.Data.Value, resultDigest = compilation.Result.ResultDigest.Data.Value,
                sourceDigest = compilation.Input.Source.Digest.Data.Value, context = compilation.Input.Source.Context.Data.Value,
                compiler = compilation.Input.Compiler.Data.Value, diagnosticId = diagnostic.DiagnosticId.ToString(),
                diagnosticDigest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes()).Data.Value,
                ruleId = diagnostic.RuleId, ruleVersion = diagnostic.RuleVersion, stageOrdinal = diagnostic.StageOrdinal,
                location = diagnostic.Location.Data.Value, evidence = diagnostic.Evidence.Data.Value },
            result = compilation is null ? null : new { execution = compilation.Result.Execution.ToString(), coverage = compilation.Result.Coverage.ToString(),
                verdict = compilation.Result.Verdict.ToString(), evidenceComplete = compilation.Result.EvidenceComplete },
            references = References.Select(reference => new { digest = reference.Digest.Data.Value, kind = reference.Kind?.ToString(),
                resolution = reference.Resolution.ToString(), bodyCaptured = Bodies.ContainsKey(reference.Digest.Value) }).ToArray()
        }));
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static string BodyDomain(DiagnosticPremiseBodyKind kind) => kind switch {
        DiagnosticPremiseBodyKind.RootInput => CompilerInputManifest.Format,
        DiagnosticPremiseBodyKind.RootSource => ProjectSource.Format,
        DiagnosticPremiseBodyKind.BindingRegion => "coglatas.semantic-binding-region/1",
        DiagnosticPremiseBodyKind.AnalysisRegion => "coglatas.compiler-dependency-region/1",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static ProjectDiagnosticPremiseSnapshot Read(CoreCompilation compilation, CompilerDiagnostic diagnostic, ContentDigest expectedResult,
        DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default) =>
        ReadCore(compilation, diagnostic, expectedResult, mode, maximumBodyBytes, cancellationToken, false);

    public static ProjectDiagnosticPremiseSnapshot ReadRoot(ProjectRootSourceAdmission admission, CompilerDiagnostic diagnostic, ContentDigest expectedResult,
        DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return ReadCore(admission.Compilation, diagnostic, expectedResult, mode, maximumBodyBytes, cancellationToken, true);
    }

    private static ProjectDiagnosticPremiseSnapshot ReadCore(CoreCompilation compilation, CompilerDiagnostic diagnostic, ContentDigest expectedResult,
        DiagnosticPremiseSnapshotMode mode, int maximumBodyBytes, CancellationToken cancellationToken, bool rootOnly)
    {
        var bodyScope = rootOnly ? "RootCompilerInputObjects" : "RetainedCompilerInputObjects";
        ArgumentNullException.ThrowIfNull(compilation); ArgumentNullException.ThrowIfNull(diagnostic); ArgumentNullException.ThrowIfNull(expectedResult);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!compilation.Result.ResultDigest.Equals(expectedResult)) return new(DiagnosticPremiseSnapshotState.RefusedWrongResult, mode, maximumBodyBytes, bodyScope);
        if (!compilation.Result.Input.Data.Equals(compilation.Input.Data)) return new(DiagnosticPremiseSnapshotState.RefusedInputBinding, mode, maximumBodyBytes, bodyScope);
        if (!compilation.Result.Diagnostics.Any(item => ReferenceEquals(item, diagnostic)) ||
            rootOnly && !diagnostic.Location.Context.Equals(compilation.Input.Source.Context)) return new(DiagnosticPremiseSnapshotState.RefusedDiagnostic, mode, maximumBodyBytes, bodyScope);
        var requested = diagnostic.Evidence.Premises.ToHashSet();
        if (requested.Count == 0) return new(mode == DiagnosticPremiseSnapshotMode.Manifest ? DiagnosticPremiseSnapshotState.ManifestOnly
            : DiagnosticPremiseSnapshotState.NoRecordedPremises, mode, maximumBodyBytes, bodyScope, compilation, diagnostic, lookupComplete: true);
        var candidates = new Dictionary<ContentDigest, List<(DiagnosticPremiseBodyKind Kind, SourceJson Data)>>();
        var remaining = MaximumObjectLookups; var complete = true;
        void Add(DiagnosticPremiseBodyKind kind, ContentDigest digest, SourceJson data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0) { complete = false; return; }
            if (!requested.Contains(digest)) return;
            if (!candidates.TryGetValue(digest, out var matches)) candidates.Add(digest, matches = []);
            matches.Add((kind, data));
        }
        Add(DiagnosticPremiseBodyKind.RootInput, compilation.Input.InputDigest, compilation.Input.Data);
        Add(DiagnosticPremiseBodyKind.RootSource, compilation.Input.Source.Digest, compilation.Input.Source.Data);
        if (!rootOnly && compilation.Dependencies is { } dependencies)
        {
            foreach (var region in dependencies.Binding.Regions)
            { Add(DiagnosticPremiseBodyKind.BindingRegion, region.Digest, region.Data); if (!complete) break; }
            if (complete)
            {
                foreach (var region in dependencies.Regions)
                { Add(DiagnosticPremiseBodyKind.AnalysisRegion, region.Digest, region.Data); if (!complete) break; }
            }
        }
        var references = diagnostic.Evidence.Premises.Select(digest => {
            if (!complete) return new DiagnosticPremiseReference(digest, null, DiagnosticPremiseResolution.LookupLimit);
            if (!candidates.TryGetValue(digest, out var matches)) return new DiagnosticPremiseReference(digest, null, DiagnosticPremiseResolution.Unavailable);
            return matches.Count == 1 ? new DiagnosticPremiseReference(digest, matches[0].Kind, DiagnosticPremiseResolution.ResolvedRetainedObject)
                : new DiagnosticPremiseReference(digest, null, DiagnosticPremiseResolution.Ambiguous);
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (!complete) return new(DiagnosticPremiseSnapshotState.LookupLimitReached, mode, maximumBodyBytes, bodyScope, compilation, diagnostic, references);
        if (mode == DiagnosticPremiseSnapshotMode.Manifest) return new(DiagnosticPremiseSnapshotState.ManifestOnly, mode, maximumBodyBytes, bodyScope, compilation, diagnostic, references, lookupComplete: true);
        var retained = new Dictionary<string, SourceJson>(StringComparer.Ordinal); long bytes = 0;
        foreach (var reference in references.Where(item => item.Resolution == DiagnosticPremiseResolution.ResolvedRetainedObject))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[reference.Digest].Single();
            bytes += Encoding.UTF8.GetByteCount(candidate.Data.CanonicalText);
            if (bytes > maximumBodyBytes) return new(DiagnosticPremiseSnapshotState.BodyBudgetExceeded, mode, maximumBodyBytes, bodyScope, compilation, diagnostic, references, lookupComplete: true);
            if (!ContentDigest.Compute(BodyDomain(candidate.Kind), candidate.Data.ToCanonicalBytes()).Equals(reference.Digest))
                return new(DiagnosticPremiseSnapshotState.RefusedBodyBinding, mode, maximumBodyBytes, bodyScope);
            retained.Add(reference.Digest.Value, candidate.Data);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(references.Length == 0 ? DiagnosticPremiseSnapshotState.NoRecordedPremises : retained.Count == 0
            ? DiagnosticPremiseSnapshotState.NoKnownBodies : DiagnosticPremiseSnapshotState.CapturedKnownBodies,
            mode, maximumBodyBytes, bodyScope, compilation, diagnostic, references, retained, true);
    }
}
