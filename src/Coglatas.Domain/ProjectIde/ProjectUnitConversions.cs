using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

/// <summary>A pinned unit definition supplied by the input manifest. Integrity is not authorization.</summary>
public sealed class UnitDefinition
{
    public string UnitId { get; }
    public int Version { get; }
    public Dimension Dimension { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }

    public UnitDefinition(SourceJson data, ContentDigest expectedDigest)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(expectedDigest);
        Data = data;
        UnitId = SourceFields.Token(data.Value, "unitId");
        Version = SourceFields.PositiveInteger(data.Value, "version");
        Dimension = new(SourceJson.FromElement(SourceFields.Object(data.Value, "dimension"), data.Limits));
        if (ValueFields.HasUnknown(data.Value, "unitId", "version", "dimension"))
            throw new UnsupportedSourceException("Unknown unit definition members are preserved without evaluation.");
        Digest = ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes());
        if (!Digest.Equals(expectedDigest)) throw new FormatException("Unit definition digest mismatch.");
    }

    internal SourceJson IdentityData => SourceJson.FromObject(new { unitId = UnitId, version = Version, digest = Digest.Data.Value });
    internal bool SameIdentity(UnitDefinition other) => UnitId == other.UnitId && Version == other.Version && Digest.Equals(other.Digest);
}

/// <summary>Only declared positive multiplicative conversions are executable. Affine/nonlinear definitions stay unsupported.</summary>
public sealed class UnitConversionDefinition
{
    public string ConversionId { get; }
    public int Version { get; }
    public UnitDefinition SourceUnit { get; }
    public UnitDefinition TargetUnit { get; }
    public ExactRational Factor { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }

    public UnitConversionDefinition(SourceJson data, ContentDigest expectedDigest, UnitDefinition sourceUnit,
        UnitDefinition targetUnit, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(expectedDigest);
        ArgumentNullException.ThrowIfNull(sourceUnit);
        ArgumentNullException.ThrowIfNull(targetUnit);
        Data = data;
        ConversionId = SourceFields.Token(data.Value, "conversionId");
        Version = SourceFields.PositiveInteger(data.Value, "version");
        if (SourceFields.Token(data.Value, "kind") != "coglatas.multiplicative")
            throw new UnsupportedSourceException("A non-multiplicative conversion requires a supported versioned rule.");
        if (ValueFields.HasUnknown(data.Value, "conversionId", "version", "kind", "sourceUnit", "targetUnit", "factor"))
            throw new UnsupportedSourceException("Unknown conversion members are preserved without evaluation.");
        RequireIdentity(SourceJson.FromElement(SourceFields.Object(data.Value, "sourceUnit"), data.Limits), sourceUnit);
        RequireIdentity(SourceJson.FromElement(SourceFields.Object(data.Value, "targetUnit"), data.Limits), targetUnit);
        if (!sourceUnit.Dimension.Equals(targetUnit.Dimension)) throw new FormatException("Conversion dimension mismatch.");
        var factor = SourceFields.Object(data.Value, "factor");
        if (ValueFields.HasUnknown(factor, "numerator", "denominator"))
            throw new UnsupportedSourceException("Unknown conversion factor members.");
        var numerator = SourceFields.String(factor, "numerator");
        var denominator = SourceFields.String(factor, "denominator");
        Factor = ExactRational.Parse(numerator, denominator, limits);
        if (Factor.Numerator.Sign <= 0 || Factor.ToString() != numerator + "/" + denominator)
            throw new FormatException("Conversion factor must be positive, canonical and reduced.");
        if (sourceUnit.SameIdentity(targetUnit) && Factor.ToString() != "1/1")
            throw new FormatException("An identity conversion must have factor one.");
        SourceUnit = sourceUnit;
        TargetUnit = targetUnit;
        Digest = ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes());
        if (!Digest.Equals(expectedDigest)) throw new FormatException("Conversion definition digest mismatch.");
    }

    private static void RequireIdentity(SourceJson identity, UnitDefinition definition)
    {
        if (!identity.Equals(definition.IdentityData)) throw new FormatException("Pinned conversion unit identity mismatch.");
    }
}

/// <summary>Original Source quantity plus an explicit normalized amount and complete ordered conversion provenance.</summary>
public sealed class ConvertedQuantity
{
    public Quantity Original { get; }
    public UnitDefinition SourceUnit { get; }
    public UnitDefinition NormalizedUnit { get; }
    public ExactRational Amount { get; }
    public IReadOnlyList<UnitConversionDefinition> Provenance { get; }
    public SourceJson Data { get; }

    internal ConvertedQuantity(Quantity original, UnitDefinition sourceUnit, UnitDefinition normalizedUnit,
        ExactRational amount, UnitConversionDefinition[] provenance)
    {
        Original = original; SourceUnit = sourceUnit; NormalizedUnit = normalizedUnit; Amount = amount;
        Provenance = new ReadOnlyCollection<UnitConversionDefinition>(provenance);
        Data = SourceJson.FromObject(new
        {
            original = original.Data.Value, sourceUnit = sourceUnit.IdentityData.Value,
            normalizedUnit = normalizedUnit.IdentityData.Value,
            amount = new { numerator = amount.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
                denominator = amount.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            conversions = provenance.Select(entry => new { definition = entry.Data.Value, digest = entry.Digest.Data.Value }).ToArray()
        });
    }
}

public sealed record UnitConversionResult(NumericEvaluationStatus Status, ConvertedQuantity? Value, string Reason);

public static class ProjectUnitConversions
{
    /// <summary>No registry search or implicit inverse. The caller supplies the exact ordered manifest entries.</summary>
    public static UnitConversionResult Convert(Quantity quantity, UnitDefinition sourceUnit, UnitDefinition targetUnit,
        IReadOnlyList<UnitConversionDefinition> path, NumericEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(sourceUnit);
        ArgumentNullException.ThrowIfNull(targetUnit);
        ArgumentNullException.ThrowIfNull(path);
        limits ??= new();
        limits.Validate();
        try
        {
            if (path.Count > limits.MaximumOperations) return new(NumericEvaluationStatus.Unknown, null, "coglatas.operation-budget-exhausted");
            if (ValueFields.HasUnknown(quantity.Data.Value, "amount", "unitId", "dimension"))
                return new(NumericEvaluationStatus.Unsupported, null, "coglatas.quantity-extension-unsupported");
            quantity.RequireCompatible(sourceUnit.UnitId, sourceUnit.Dimension);
            if (!sourceUnit.Dimension.Equals(targetUnit.Dimension)) return new(NumericEvaluationStatus.Invalid, null, "coglatas.dimension-mismatch");
            var amount = quantity.Amount.ToRational(limits.Numbers);
            var current = sourceUnit;
            var cumulativeFactor = ExactRational.Parse("1", "1", limits.Numbers);
            var visited = new Dictionary<string, ExactRational>(StringComparer.Ordinal) { [sourceUnit.IdentityData.CanonicalText] = cumulativeFactor };
            var definitions = new Dictionary<(string, int), ContentDigest>();
            var provenance = new UnitConversionDefinition[path.Count];
            for (var index = 0; index < path.Count; index++)
            {
                var entry = path[index];
                if (entry is null || !entry.SourceUnit.SameIdentity(current))
                    return new(NumericEvaluationStatus.Invalid, null, "coglatas.conversion-path-mismatch");
                var definitionIdentity = (entry.ConversionId, entry.Version);
                if (definitions.TryGetValue(definitionIdentity, out var priorDigest) && !priorDigest.Equals(entry.Digest))
                    return new(NumericEvaluationStatus.Invalid, null, "coglatas.conversion-version-conflict");
                definitions[definitionIdentity] = entry.Digest;
                cumulativeFactor = cumulativeFactor.Multiply(entry.Factor, limits.Numbers);
                var targetIdentity = entry.TargetUnit.IdentityData.CanonicalText;
                if (visited.TryGetValue(targetIdentity, out var priorFactor) && priorFactor != cumulativeFactor)
                    return new(NumericEvaluationStatus.Invalid, null, "coglatas.inconsistent-conversion-cycle");
                visited[targetIdentity] = cumulativeFactor;
                amount = amount.Multiply(entry.Factor, limits.Numbers);
                current = entry.TargetUnit;
                provenance[index] = entry;
            }
            if (!current.SameIdentity(targetUnit)) return new(NumericEvaluationStatus.Unsupported, null, "coglatas.conversion-definition-missing");
            return new(NumericEvaluationStatus.Known, new(quantity, sourceUnit, targetUnit, amount, provenance), "coglatas.exact-conversion");
        }
        catch (UnsupportedSourceException) { return new(NumericEvaluationStatus.Unsupported, null, "coglatas.unit-binding-required"); }
        catch (OverflowException) { return new(NumericEvaluationStatus.Unknown, null, "coglatas.numeric-bound-exhausted"); }
        catch (Exception error) when (error is FormatException or DivideByZeroException or ArgumentException)
        { return new(NumericEvaluationStatus.Invalid, null, "coglatas.invalid-conversion"); }
    }

    public static UnitConversionResult CanConvert(UnitDefinition sourceUnit, UnitDefinition targetUnit,
        IReadOnlyList<UnitConversionDefinition> path, NumericEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(sourceUnit);
        var zero = new Quantity(SourceJson.FromObject(new { amount = "0", unitId = sourceUnit.UnitId, dimension = sourceUnit.Dimension.Data.Value }));
        return Convert(zero, sourceUnit, targetUnit, path, limits);
    }
}
