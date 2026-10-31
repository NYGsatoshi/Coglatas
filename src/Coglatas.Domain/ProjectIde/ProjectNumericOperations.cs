using System.Collections.ObjectModel;
using System.Globalization;

namespace Coglatas.Domain.ProjectIde;

public enum NumericEvaluationStatus { Known, Null, Unknown, Invalid, Unsupported }
public enum NumericOperator { Add, Subtract, Multiply, Divide, Compare }

/// <summary>Provisional deterministic processing limits; no hardware or product-capacity assurance.</summary>
public sealed record NumericEvaluationLimits(ExactNumberLimits? NumberLimits = null, int MaximumOperations = 1024,
    int MaximumDimensionTerms = 64, int MaximumAbsoluteExponent = 1024)
{
    public ExactNumberLimits Numbers => NumberLimits ?? new();
    internal void Validate()
    {
        Numbers.Validate();
        if (MaximumOperations is < 1 or > 100_000 || MaximumDimensionTerms is < 1 or > 1024 ||
            MaximumAbsoluteExponent is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(NumericEvaluationLimits));
    }
}

/// <summary>Exact derived magnitude using the existing rational type, with declared unit factors and original input basis.</summary>
public sealed class NumericOperand
{
    public NumericEvaluationStatus Status { get; }
    public ExactRational? Amount { get; }
    public Dimension? Dimension { get; }
    public IReadOnlyDictionary<string, int> UnitPowers { get; }
    public SourceJson Basis { get; }
    public SourceJson Data { get; }
    public int OperationCount { get; }

    internal NumericOperand(NumericEvaluationStatus status, ExactRational? amount, Dimension? dimension,
        IDictionary<string, int> units, SourceJson basis, int operationCount = 0)
    {
        Status = status; Amount = amount; Dimension = dimension; Basis = basis; OperationCount = operationCount;
        UnitPowers = new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(units, StringComparer.Ordinal));
        Data = SourceJson.FromObject(new
        {
            state = "coglatas." + status.ToString().ToLowerInvariant(),
            amount = amount is null ? null : new { numerator = amount.Numerator.ToString(CultureInfo.InvariantCulture),
                denominator = amount.Denominator.ToString(CultureInfo.InvariantCulture) },
            dimension = dimension?.Data.Value, units = UnitPowers, operationCount, basis = basis.Value
        });
    }

    public static NumericOperand From(ValueDecodeResult value, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Status != ValueDecodeStatus.Decoded)
            return new(value.Status == ValueDecodeStatus.Invalid ? NumericEvaluationStatus.Invalid : NumericEvaluationStatus.Unsupported,
                null, null, new Dictionary<string, int>(), value.Data);
        return From(value.Value!, limits);
    }

    public static NumericOperand From(TypedProjectValue value, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Type.Kind is not ("coglatas.decimal" or "coglatas.rational" or "coglatas.quantity"))
            return new(NumericEvaluationStatus.Unsupported, null, null, new Dictionary<string, int>(), value.Data);
        if (value.State != ValueState.Known)
            return new(value.State == ValueState.Null ? NumericEvaluationStatus.Null : NumericEvaluationStatus.Unknown,
                null, null, new Dictionary<string, int>(), value.Data);
        return value.KnownValue switch
        {
            ExactDecimal number => Known(number.ToRational(limits), EmptyDimension(), new Dictionary<string, int>(), value.Data),
            ExactRational number => Known(ExactRational.Create(number.Numerator, number.Denominator, limits), EmptyDimension(), new Dictionary<string, int>(), value.Data),
            Quantity quantity => From(quantity, limits),
            _ => new(NumericEvaluationStatus.Unsupported, null, null, new Dictionary<string, int>(), value.Data)
        };
    }

    public static NumericOperand From(Quantity value, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ValueFields.HasUnknown(value.Data.Value, "amount", "unitId", "dimension"))
            return new(NumericEvaluationStatus.Unsupported, null, null, new Dictionary<string, int>(), value.Data);
        return Known(value.Amount.ToRational(limits), value.Dimension, new Dictionary<string, int> { [value.UnitId] = 1 }, value.Data);
    }

    public static NumericOperand From(ConvertedQuantity value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Include version/digest in the algebraic unit identity; distinct pinned definitions never compare implicitly.
        var identity = value.NormalizedUnit.IdentityData.CanonicalText;
        return Known(value.Amount, value.NormalizedUnit.Dimension, new Dictionary<string, int> { [identity] = 1 }, value.Data, value.Provenance.Count);
    }

    internal static NumericOperand Known(ExactRational amount, Dimension dimension, IDictionary<string, int> units, SourceJson basis, int operationCount = 0) =>
        new(NumericEvaluationStatus.Known, amount, dimension, units, basis, operationCount);
    internal static Dimension EmptyDimension() => new(SourceJson.Parse("{}"));
}

public sealed record NumericEvaluationResult(NumericEvaluationStatus Status, NumericOperand? Value, int? Comparison,
    QuantizedExactValue? Quantization, string Reason, SourceJson Basis);

public static class ProjectNumericOperations
{
    public static NumericEvaluationResult Evaluate(NumericOperand left, NumericOperand right, NumericOperator operation,
        NumericEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        limits ??= new();
        limits.Validate();
        // Fixed-depth provenance references prevent repeated arithmetic from expanding its JSON exponentially.
        var basis = SourceJson.FromObject(new { operation = operation.ToString(), left = ContentDigest.Compute("coglatas.numeric-operand/1", left.Data.ToCanonicalBytes()).Data.Value,
            right = ContentDigest.Compute("coglatas.numeric-operand/1", right.Data.ToCanonicalBytes()).Data.Value });
        NumericEvaluationResult State(NumericEvaluationStatus state, string reason) => new(state, null, null, null, reason, basis);
        if (!Enum.IsDefined(operation)) return State(NumericEvaluationStatus.Unsupported, "coglatas.operator-unsupported");
        foreach (var state in new[] { NumericEvaluationStatus.Invalid, NumericEvaluationStatus.Unsupported, NumericEvaluationStatus.Unknown, NumericEvaluationStatus.Null })
            if (left.Status == state || right.Status == state) return State(state, "coglatas.input-" + state.ToString().ToLowerInvariant());
        try
        {
            var operationCount = checked(left.OperationCount + right.OperationCount + 1);
            if (operationCount > limits.MaximumOperations) return State(NumericEvaluationStatus.Unknown, "coglatas.operation-budget-exhausted");
            var a = left.Amount!;
            var b = right.Amount!;
            CheckOperand(left, limits); CheckOperand(right, limits);
            var additive = operation is NumericOperator.Add or NumericOperator.Subtract or NumericOperator.Compare;
            if (additive && !left.Dimension!.Equals(right.Dimension)) return State(NumericEvaluationStatus.Invalid, "coglatas.dimension-mismatch");
            if (additive && !SameUnits(left.UnitPowers, right.UnitPowers)) return State(NumericEvaluationStatus.Unsupported, "coglatas.explicit-conversion-required");
            if (operation == NumericOperator.Compare)
                return new(NumericEvaluationStatus.Known, null, a.CompareTo(b, limits.Numbers), null, "coglatas.exact-comparison", basis);
            var amount = operation switch
            {
                NumericOperator.Add => a.Add(b, limits.Numbers),
                NumericOperator.Subtract => a.Subtract(b, limits.Numbers),
                NumericOperator.Multiply => a.Multiply(b, limits.Numbers),
                NumericOperator.Divide => a.Divide(b, limits.Numbers),
                _ => throw new UnsupportedSourceException("Unsupported numeric operation.")
            };
            var direction = operation == NumericOperator.Divide ? -1 : 1;
            var powers = additive ? new Dictionary<string, int>(left.Dimension!.Powers) : Combine(left.Dimension!.Powers, right.Dimension!.Powers, direction, limits);
            var units = additive ? new Dictionary<string, int>(left.UnitPowers) : Combine(left.UnitPowers, right.UnitPowers, direction, limits);
            var dimension = new Dimension(SourceJson.FromObject(powers));
            return new(NumericEvaluationStatus.Known, NumericOperand.Known(amount, dimension, units, basis, operationCount), null, null, "coglatas.exact-arithmetic", basis);
        }
        catch (DivideByZeroException) { return State(NumericEvaluationStatus.Invalid, "coglatas.division-by-zero"); }
        catch (OverflowException) { return State(NumericEvaluationStatus.Unknown, "coglatas.numeric-bound-exhausted"); }
        catch (UnsupportedSourceException) { return State(NumericEvaluationStatus.Unsupported, "coglatas.operation-unsupported"); }
    }

    public static NumericEvaluationResult Quantize(NumericOperand value, int scale, string roundingMode,
        NumericEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        limits ??= new(); limits.Validate();
        var basis = SourceJson.FromObject(new { value = ContentDigest.Compute("coglatas.numeric-operand/1", value.Data.ToCanonicalBytes()).Data.Value, scale, roundingMode });
        NumericEvaluationResult State(NumericEvaluationStatus state, string reason) => new(state, null, null, null, reason, basis);
        if (roundingMode != "coglatas.nearest-ties-to-even") return State(NumericEvaluationStatus.Unsupported, "coglatas.rounding-mode-unsupported");
        if (value.Status != NumericEvaluationStatus.Known) return State(value.Status, "coglatas.input-" + value.Status.ToString().ToLowerInvariant());
        try
        {
            var operationCount = checked(value.OperationCount + 1);
            if (operationCount > limits.MaximumOperations) return State(NumericEvaluationStatus.Unknown, "coglatas.operation-budget-exhausted");
            CheckOperand(value, limits);
            var quantization = value.Amount!.Quantize(scale, limits.Numbers);
            return new(NumericEvaluationStatus.Known, NumericOperand.Known(quantization.Rounded.ToRational(limits.Numbers),
                value.Dimension!, new Dictionary<string, int>(value.UnitPowers), basis, operationCount), null, quantization, "coglatas.explicit-quantization", basis);
        }
        catch (ArgumentOutOfRangeException) { return State(NumericEvaluationStatus.Invalid, "coglatas.invalid-quantization-scale"); }
        catch (OverflowException) { return State(NumericEvaluationStatus.Unknown, "coglatas.numeric-bound-exhausted"); }
    }

    private static bool SameUnits(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right) =>
        left.Count == right.Count && left.All(term => right.TryGetValue(term.Key, out var power) && power == term.Value);

    private static Dictionary<string, int> Combine(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right,
        int direction, NumericEvaluationLimits limits)
    {
        var result = new Dictionary<string, int>(left, StringComparer.Ordinal);
        foreach (var term in right)
        {
            var power = checked(result.GetValueOrDefault(term.Key) + checked(direction * term.Value));
            if (Math.Abs((long)power) > limits.MaximumAbsoluteExponent) throw new OverflowException("Dimension exponent bound exceeded.");
            if (power == 0) result.Remove(term.Key); else result[term.Key] = power;
        }
        if (result.Count > limits.MaximumDimensionTerms) throw new OverflowException("Dimension term bound exceeded.");
        return result;
    }

    private static void CheckOperand(NumericOperand value, NumericEvaluationLimits limits)
    {
        limits.Numbers.Check(value.Amount!.Numerator); limits.Numbers.Check(value.Amount.Denominator);
        if (value.Dimension!.Powers.Count > limits.MaximumDimensionTerms || value.UnitPowers.Count > limits.MaximumDimensionTerms ||
            value.Dimension.Powers.Values.Concat(value.UnitPowers.Values).Any(power => Math.Abs((long)power) > limits.MaximumAbsoluteExponent))
            throw new OverflowException("Dimension processing bound exceeded.");
    }
}
