using System.Text;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum RecordedPredicateSnapshotState
{
    ManifestOnly, CapturedOriginalArrays, NoRecordedPremises, NoAvailableOriginalArrays, BodyBudgetExceeded,
    RefusedWrongResult, RefusedRecord, RefusedDiagnostic, RefusedBodyBinding
}

public sealed class RecordedPredicatePremiseReference
{
    public ContentDigest Digest { get; }
    public bool AvailableInOriginalRecord { get; }
    internal RecordedPredicatePremiseReference(ContentDigest digest, bool available)
    { Digest = digest; AvailableInOriginalRecord = available; }
}

/// <summary>Explicit selected-diagnostic reads of owned original root arrays; no execution, run selection or runtime read grant.</summary>
public sealed class ProjectRecordedPredicatePremiseSnapshot
{
    public const string Format = "coglatas.local-recorded-predicate-premise-snapshot/1";
    public RecordedPredicateSnapshotState State { get; }
    public DiagnosticPremiseSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public CompilerDiagnostic? Diagnostic { get; }
    public ContentDigest? InputDigest { get; }
    public ContentDigest? ResultDigest { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public SourceJson? OriginalRecordMetadata { get; }
    public PredicatePremiseRecordState? OriginalCaptureState { get; }
    public IReadOnlyList<RecordedPredicatePremiseReference> References { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectRecordedPredicatePremiseSnapshot(RecordedPredicateSnapshotState state, DiagnosticPremiseSnapshotMode mode, int budget,
        CoreCompilation? compilation = null, ProjectPredicatePremiseRecord? record = null, CompilerDiagnostic? diagnostic = null,
        IEnumerable<RecordedPredicatePremiseReference>? references = null, Dictionary<string, SourceJson>? bodies = null)
    {
        State = state; Mode = mode; MaximumBodyBytes = budget; Diagnostic = diagnostic;
        InputDigest = compilation?.Input.InputDigest; ResultDigest = compilation?.Result.ResultDigest;
        OriginalRecordDigest = record?.Digest; OriginalRecordMetadata = record?.Data; OriginalCaptureState = record?.State;
        References = new ImmutableListView<RecordedPredicatePremiseReference>(references ?? []);
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText));
        Data = SourceJson.Parse(JsonSerializer.Serialize(new {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes = budget, retainedBodyBytes = RetainedBodyBytes,
            bodyScope = "SelectedDiagnosticOriginalRootPredicateArrays", regionalEvaluation = "NotRecorded", relationshipStructure = "NotRecorded",
            earlierStates = "NotRecorded", runSelection = "NotEvaluated", authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            basis = compilation is null || diagnostic is null ? null : new {
                inputDigest = compilation.Input.InputDigest.Data.Value, resultDigest = compilation.Result.ResultDigest.Data.Value,
                sourceDigest = compilation.Input.Source.Digest.Data.Value, context = compilation.Input.Source.Context.Data.Value,
                compiler = compilation.Input.Compiler.Data.Value, diagnosticId = diagnostic.DiagnosticId.ToString(),
                diagnosticDigest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes()).Data.Value },
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, metadata = record.Data.Value },
            result = compilation is null ? null : new { execution = compilation.Result.Execution.ToString(), coverage = compilation.Result.Coverage.ToString(),
                verdict = compilation.Result.Verdict.ToString(), evidenceComplete = compilation.Result.EvidenceComplete },
            references = References.Select(reference => new { digest = reference.Digest.Data.Value,
                availableInOriginalRecord = reference.AvailableInOriginalRecord, bodyCaptured = Bodies.ContainsKey(reference.Digest.Value) }).ToArray()
        }));
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectRecordedPredicatePremiseSnapshot Read(ProjectRootSourceAdmission admission, ProjectPredicatePremiseRecord record,
        CompilerDiagnostic diagnostic, ContentDigest expectedResult, DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission); ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(diagnostic); ArgumentNullException.ThrowIfNull(expectedResult);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        var compilation = admission.Compilation;
        if (!compilation.Result.ResultDigest.Equals(expectedResult)) return new(RecordedPredicateSnapshotState.RefusedWrongResult, mode, maximumBodyBytes);
        if (!record.IsOwnedBy(compilation)) return new(RecordedPredicateSnapshotState.RefusedRecord, mode, maximumBodyBytes);
        if (!compilation.Result.Diagnostics.Any(item => ReferenceEquals(item, diagnostic)) ||
            !diagnostic.Location.Context.Equals(compilation.Input.Source.Context)) return new(RecordedPredicateSnapshotState.RefusedDiagnostic, mode, maximumBodyBytes);
        var references = new List<RecordedPredicatePremiseReference>();
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        foreach (var premise in diagnostic.Evidence.Premises)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var available = record.State == PredicatePremiseRecordState.CapturedOriginalArrays && record.Bodies.TryGetValue(premise.Value, out _);
            references.Add(new(premise, available));
            if (!available) continue;
            var body = record.Bodies[premise.Value];
            if (!ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, body.ToCanonicalBytes()).Equals(premise))
                return new(RecordedPredicateSnapshotState.RefusedBodyBinding, mode, maximumBodyBytes);
            if (mode == DiagnosticPremiseSnapshotMode.AvailableBodies) bodies.TryAdd(premise.Value, body);
        }
        var state = mode == DiagnosticPremiseSnapshotMode.Manifest ? RecordedPredicateSnapshotState.ManifestOnly
            : references.Count == 0 ? RecordedPredicateSnapshotState.NoRecordedPremises
            : bodies.Count == 0 ? RecordedPredicateSnapshotState.NoAvailableOriginalArrays : RecordedPredicateSnapshotState.CapturedOriginalArrays;
        if (bodies.Values.Sum(body => (long)Encoding.UTF8.GetByteCount(body.CanonicalText)) > maximumBodyBytes)
        { bodies.Clear(); state = RecordedPredicateSnapshotState.BodyBudgetExceeded; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(state, mode, maximumBodyBytes, compilation, record, diagnostic, references, bodies);
    }
}
