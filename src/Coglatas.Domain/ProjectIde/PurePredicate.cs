namespace Coglatas.Domain.ProjectIde;

public enum PredicateTruth { False, True, Unknown }

public sealed class PredicateEvaluation
{
    public PredicateTruth Truth { get; }
    public IReadOnlyList<SourceJson> EvaluatedPremises { get; }
    internal PredicateEvaluation(PredicateTruth truth, List<SourceJson> premises)
    { Truth = truth; EvaluatedPremises = new ImmutableListView<SourceJson>(premises); }
}

/// <summary>Bounded, pure Boolean Source AST. Reference binding and richer operators remain unsupported.</summary>
public sealed class PurePredicate
{
    public string Kind { get; }
    public TypedProjectValue? Literal { get; }
    public IReadOnlyList<PurePredicate> Operands { get; }
    public SourceJson Data { get; }

    private PurePredicate(SourceJson data, string kind, TypedProjectValue? literal, PurePredicate[] operands)
    {
        Data = data; Kind = kind; Literal = literal; Operands = new ImmutableListView<PurePredicate>(operands);
    }

    public static PurePredicate Parse(SourceJson data, int maximumNodes = 1024, int maximumDepth = 32)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (maximumNodes is < 1 or > 100_000 || maximumDepth is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        var remaining = maximumNodes;
        return Read(data, 1, maximumDepth, ref remaining);
    }

    private static PurePredicate Read(SourceJson data, int depth, int maximumDepth, ref int remaining)
    {
        if (depth > maximumDepth || --remaining < 0) throw new FormatException("Predicate logical processing bound exceeded.");
        var kind = SourceFields.Token(data.Value, "kind");
        if (kind == "coglatas.literal")
        {
            if (ValueFields.HasUnknown(data.Value, "kind", "value")) throw new UnsupportedSourceException("Unknown predicate fields.");
            var result = TypedProjectValueCodec.Decode(SourceJson.FromElement(SourceFields.Object(data.Value, "value"), data.Limits));
            if (result.Status == ValueDecodeStatus.Invalid) throw new FormatException("Invalid predicate literal.");
            if (result.Status == ValueDecodeStatus.Unsupported) throw new UnsupportedSourceException("Unsupported predicate literal.");
            if (result.Value!.Type.Kind != "coglatas.boolean" || result.Value.State == ValueState.Null)
                throw new FormatException("Predicate literals require Boolean Known/Unknown values.");
            return new(data, kind, result.Value, []);
        }
        if (kind is not ("coglatas.not" or "coglatas.and" or "coglatas.or"))
            throw new UnsupportedSourceException("Unsupported predicate operator; no script or ambient input evaluation.");
        if (ValueFields.HasUnknown(data.Value, "kind", "operands")) throw new UnsupportedSourceException("Unknown predicate fields.");
        var items = SourceFields.Array(data.Value, "operands").EnumerateArray().ToArray();
        if (kind == "coglatas.not" ? items.Length != 1 : items.Length is < 2 or > 32)
            throw new FormatException("Invalid bounded predicate arity.");
        var operands = new PurePredicate[items.Length];
        for (var i = 0; i < items.Length; i++)
            operands[i] = Read(SourceJson.FromElement(items[i], data.Limits), depth + 1, maximumDepth, ref remaining);
        return new(data, kind, null, operands);
    }

    public PredicateEvaluation Evaluate()
    {
        var premises = new List<SourceJson>();
        return new(Evaluate(premises), premises);
    }

    /// <summary>Observe this actual bounded evaluation with an explicit policy; no prior Compiler trace is reconstructed.</summary>
    public RecordedPredicateEvaluation EvaluateWithObservation(PredicateEvaluationCapturePolicy capturePolicy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capturePolicy);
        try { _ = SourceJson.Parse(Data.ToCanonicalBytes()); }
        catch (FormatException) { throw new UnsupportedSourceException("Predicate observation requires the default bounded JSON profile."); }
        var recorder = new PredicateEvaluationRecorder(capturePolicy, cancellationToken);
        var premises = new List<SourceJson>();
        try
        {
            var evaluation = new PredicateEvaluation(Evaluate(premises, recorder), premises);
            recorder.CheckCancellation();
            return new(this, evaluation, recorder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            recorder.Interrupt();
            return new(this, null, recorder);
        }
    }

    private PredicateTruth Evaluate(List<SourceJson> premises, PredicateEvaluationRecorder? recorder = null,
        PredicateEvaluationRecorder.Visit? parent = null, int operand = -1)
    {
        var visit = recorder?.Enter(parent, operand);
        PredicateTruth Complete(PredicateTruth truth, int premiseOrdinal = -1) =>
            recorder is null ? truth : recorder.Complete(visit, this, truth, premiseOrdinal);
        if (Literal is not null)
        {
            premises.Add(Literal.Data);
            return Complete(Literal.State == ValueState.Unknown ? PredicateTruth.Unknown : (bool)Literal.KnownValue! ? PredicateTruth.True : PredicateTruth.False,
                premises.Count - 1);
        }
        if (Kind == "coglatas.not")
            return Complete(Operands[0].Evaluate(premises, recorder, visit, 0) switch { PredicateTruth.True => PredicateTruth.False, PredicateTruth.False => PredicateTruth.True, _ => PredicateTruth.Unknown });
        var unknown = false;
        for (var index = 0; index < Operands.Count; index++)
        {
            var truth = Operands[index].Evaluate(premises, recorder, visit, index);
            if (Kind == "coglatas.and" && truth == PredicateTruth.False) return Complete(PredicateTruth.False);
            if (Kind == "coglatas.or" && truth == PredicateTruth.True) return Complete(PredicateTruth.True);
            unknown |= truth == PredicateTruth.Unknown;
        }
        return Complete(unknown ? PredicateTruth.Unknown : Kind == "coglatas.and" ? PredicateTruth.True : PredicateTruth.False);
    }
}
