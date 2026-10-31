using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

public enum PredicateTruth { False, True, Unknown }

public sealed class PredicateEvaluation
{
    public PredicateTruth Truth { get; }
    public IReadOnlyList<SourceJson> EvaluatedPremises { get; }
    internal PredicateEvaluation(PredicateTruth truth, List<SourceJson> premises)
    { Truth = truth; EvaluatedPremises = new ReadOnlyCollection<SourceJson>(premises.ToArray()); }
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
        Data = data; Kind = kind; Literal = literal; Operands = new ReadOnlyCollection<PurePredicate>(operands);
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

    private PredicateTruth Evaluate(List<SourceJson> premises)
    {
        if (Literal is not null)
        {
            premises.Add(Literal.Data);
            return Literal.State == ValueState.Unknown ? PredicateTruth.Unknown : (bool)Literal.KnownValue! ? PredicateTruth.True : PredicateTruth.False;
        }
        if (Kind == "coglatas.not")
            return Operands[0].Evaluate(premises) switch { PredicateTruth.True => PredicateTruth.False, PredicateTruth.False => PredicateTruth.True, _ => PredicateTruth.Unknown };
        var unknown = false;
        foreach (var operand in Operands)
        {
            var truth = operand.Evaluate(premises);
            if (Kind == "coglatas.and" && truth == PredicateTruth.False) return PredicateTruth.False;
            if (Kind == "coglatas.or" && truth == PredicateTruth.True) return PredicateTruth.True;
            unknown |= truth == PredicateTruth.Unknown;
        }
        return unknown ? PredicateTruth.Unknown : Kind == "coglatas.and" ? PredicateTruth.True : PredicateTruth.False;
    }
}
