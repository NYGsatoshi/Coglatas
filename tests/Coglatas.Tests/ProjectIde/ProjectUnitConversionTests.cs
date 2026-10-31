using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectUnitConversionTests
{
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static SourceJson Length => Json(new Dictionary<string, int> { ["coglatas.length"] = 1 });
    private static SourceJson Time => Json(new Dictionary<string, int> { ["coglatas.time"] = 1 });
    private static UnitDefinition Unit(string id, SourceJson? dimension = null, int version = 1)
    {
        var data = Json(new { unitId = id, version, dimension = (dimension ?? Length).Value });
        return new(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()));
    }
    private static object Identity(UnitDefinition unit) => new { unitId = unit.UnitId, version = unit.Version, digest = unit.Digest.Data.Value };
    private static SourceJson EntryData(UnitDefinition source, UnitDefinition target, string numerator, string denominator = "1", string kind = "coglatas.multiplicative") =>
        Json(new { conversionId = "fixture.convert", version = 1, kind, sourceUnit = Identity(source), targetUnit = Identity(target), factor = new { numerator, denominator } });
    private static UnitConversionDefinition Entry(UnitDefinition source, UnitDefinition target, string numerator, string denominator = "1")
    {
        var data = EntryData(source, target, numerator, denominator);
        return new(data, ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes()), source, target);
    }
    private static Quantity Quantity(string amount, UnitDefinition unit) =>
        new(Json(new { amount, unitId = unit.UnitId, dimension = unit.Dimension.Data.Value }));
    private static NumericOperand Scalar(string amount, string state = "coglatas.known", string kind = "coglatas.decimal")
    {
        var fields = new Dictionary<string, object?> { ["type"] = new { kind, schemaVersion = 1, nullable = true, acceptsUnknown = true }, ["state"] = state };
        if (state == "coglatas.known") fields["value"] = amount;
        if (state == "coglatas.unknown") { fields["reason"] = "fixture.missing-input"; fields["basis"] = new { coverage = "fixture.partial" }; }
        return NumericOperand.From(TypedProjectValueCodec.Decode(Json(fields)));
    }

    [Fact]
    public void ConversionRetainsOriginalAndPinnedProvenanceWithoutDecimalRounding()
    {
        var meter = Unit("fixture.meter");
        var foot = Unit("fixture.foot");
        var definition = Entry(foot, meter, "381", "1250");
        var source = Quantity("1", foot);
        var result = ProjectUnitConversions.Convert(source, foot, meter, [definition]);
        Assert.Equal(NumericEvaluationStatus.Known, result.Status);
        Assert.Same(source, result.Value!.Original);
        Assert.Same(foot, result.Value.SourceUnit);
        Assert.Same(meter, result.Value.NormalizedUnit);
        Assert.Equal("381/1250", result.Value.Amount.ToString());
        Assert.Same(definition, Assert.Single(result.Value.Provenance));
        Assert.Contains(definition.Digest.Value, result.Value.Data.CanonicalText, StringComparison.Ordinal);
        Assert.Equal("1", source.Amount.CanonicalText);
        Assert.Throws<NotSupportedException>(() => ((IList<UnitConversionDefinition>)result.Value.Provenance).Clear());
    }

    [Fact]
    public void ExplicitInverseAndMultistepConversionsAreExact()
    {
        var inch = Unit("fixture.inch"); var foot = Unit("fixture.foot"); var meter = Unit("fixture.meter");
        var inchToFoot = Entry(inch, foot, "1", "12"); var footToMeter = Entry(foot, meter, "381", "1250");
        var input = Quantity("1200", inch);
        var result = ProjectUnitConversions.Convert(input, inch, meter, [inchToFoot, footToMeter]);
        Assert.Equal("762/25", result.Value!.Amount.ToString());
        Assert.Equal(2, result.Value.Provenance.Count);
        var inverse = ProjectUnitConversions.Convert(Quantity("30.48", meter), meter, inch,
            [Entry(meter, foot, "1250", "381"), Entry(foot, inch, "12")]);
        Assert.Equal("1200/1", inverse.Value!.Amount.ToString());
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectUnitConversions.Convert(Quantity("1", meter), meter, inch, []).Status);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("2", "4")]
    [InlineData("01", "1")]
    [InlineData("1", "-1")]
    public void InvalidConversionFactorsAreRejected(string numerator, string denominator)
    {
        var source = Unit("fixture.a"); var target = Unit("fixture.b");
        var data = EntryData(source, target, numerator, denominator);
        Assert.Throws<FormatException>(() => new UnitConversionDefinition(data,
            ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes()), source, target));
    }

    [Fact]
    public void ZeroDenominatorIsRejectedWithoutCreatingAConversion()
    {
        var source = Unit("fixture.a"); var target = Unit("fixture.b"); var data = EntryData(source, target, "1", "0");
        Assert.Throws<DivideByZeroException>(() => new UnitConversionDefinition(data,
            ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes()), source, target));
    }

    [Theory]
    [InlineData("coglatas.affine")]
    [InlineData("coglatas.nonlinear")]
    [InlineData("future.conversion")]
    public void UnsupportedConversionKindsAreNeverLinearized(string kind)
    {
        var source = Unit("fixture.a"); var target = Unit("fixture.b"); var data = EntryData(source, target, "1", kind: kind);
        Assert.Throws<UnsupportedSourceException>(() => new UnitConversionDefinition(data,
            ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes()), source, target));
        Assert.Equal(data.CanonicalText, SourceJson.Parse(data.ToCanonicalBytes()).CanonicalText);
    }

    [Fact]
    public void VersionDigestDimensionAndPathMismatchesAreNotConvertible()
    {
        var meter = Unit("fixture.meter"); var centimeter = Unit("fixture.centimeter"); var seconds = Unit("fixture.second", Time);
        Assert.Throws<FormatException>(() => Entry(meter, seconds, "1"));
        var incompatible = ProjectUnitConversions.Convert(Quantity("1", meter), meter, seconds, []);
        Assert.Equal(NumericEvaluationStatus.Invalid, incompatible.Status);
        Assert.Equal("coglatas.dimension-mismatch", incompatible.Reason);
        var entry = Entry(centimeter, meter, "1", "100");
        Assert.Equal(NumericEvaluationStatus.Invalid, ProjectUnitConversions.Convert(Quantity("1", meter), meter, centimeter, [entry]).Status);
        var v2 = Unit("fixture.meter", version: 2);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectUnitConversions.CanConvert(meter, v2, []).Status);
        Assert.Throws<FormatException>(() => new UnitDefinition(meter.Data, v2.Digest));
        var data = EntryData(centimeter, meter, "1", "100");
        Assert.Throws<FormatException>(() => new UnitConversionDefinition(data, v2.Digest, centimeter, meter));
        Assert.Throws<FormatException>(() => new UnitConversionDefinition(data,
            ContentDigest.Compute("coglatas.unit-conversion/1", data.ToCanonicalBytes()), centimeter, v2));
    }

    [Fact]
    public void IdentityRequiresUnitBindingAndFactorOne()
    {
        var unit = Unit("fixture.unitless", SourceJson.Parse("{}"));
        Assert.Equal("-2/1", ProjectUnitConversions.Convert(Quantity("-2", unit), unit, unit, []).Value!.Amount.ToString());
        Assert.Throws<FormatException>(() => Entry(unit, unit, "2"));
        var other = Unit("fixture.other", SourceJson.Parse("{}"));
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectUnitConversions.Convert(Quantity("2", other), unit, unit, []).Status);
    }

    [Fact]
    public void ConversionBudgetsAndNumberBoundsReturnUnknown()
    {
        var a = Unit("fixture.a"); var b = Unit("fixture.b"); var c = Unit("fixture.c");
        var path = new[] { Entry(a, b, "100"), Entry(b, c, "100") };
        Assert.Equal("coglatas.operation-budget-exhausted", ProjectUnitConversions.Convert(Quantity("1", a), a, c, path,
            new(MaximumOperations: 1)).Reason);
        var large = ProjectUnitConversions.Convert(Quantity("99", a), a, b, [path[0]], new(new(3, 2)));
        Assert.Equal(NumericEvaluationStatus.Unknown, large.Status);
        Assert.Null(large.Value);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("ar-SA")]
    public void ConversionCanonicalBytesAndDigestsAreCultureIndependent(string culture)
    {
        var unit = Unit("fixture.meter"); var target = Unit("fixture.centimeter");
        var baseline = ProjectUnitConversions.Convert(Quantity("-1.25", unit), unit, target, [Entry(unit, target, "100")]).Value!;
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var actual = ProjectUnitConversions.Convert(Quantity("-1.25", unit), unit, target, [Entry(unit, target, "100")]).Value!;
            Assert.Equal(baseline.Data.ToCanonicalBytes(), actual.Data.ToCanonicalBytes());
            Assert.Equal(ContentDigest.Compute("fixture.result", baseline.Data.ToCanonicalBytes()), ContentDigest.Compute("fixture.result", actual.Data.ToCanonicalBytes()));
            Assert.Equal("-125/1", actual.Amount.ToString());
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(NumericOperator.Add, "0.1", "0.2", "3/10")]
    [InlineData(NumericOperator.Subtract, "0.1", "0.2", "-1/10")]
    [InlineData(NumericOperator.Multiply, "-0.1", "0.2", "-1/50")]
    [InlineData(NumericOperator.Divide, "1", "3", "1/3")]
    [InlineData(NumericOperator.Add, "123456789012345678901234567890", "0.00000000000000000001", "12345678901234567890123456789000000000000000000001/100000000000000000000")]
    public void NumericOperationsUseExactExistingRational(NumericOperator operation, string left, string right, string expected)
    {
        var result = ProjectNumericOperations.Evaluate(Scalar(left), Scalar(right), operation);
        Assert.Equal(NumericEvaluationStatus.Known, result.Status);
        Assert.Equal(expected, result.Value!.Amount!.ToString());
        Assert.Empty(result.Value.Dimension!.Powers);
        Assert.Empty(result.Value.UnitPowers);
    }

    [Fact]
    public void DerivedDimensionsAndUnitsAreExplicitAndChecked()
    {
        var distance = NumericOperand.From(Quantity("10", Unit("fixture.meter")));
        var elapsed = NumericOperand.From(Quantity("2", Unit("fixture.second", Time)));
        var speed = ProjectNumericOperations.Evaluate(distance, elapsed, NumericOperator.Divide).Value!;
        Assert.Equal("5/1", speed.Amount!.ToString());
        Assert.Equal(1, speed.Dimension!.Powers["coglatas.length"]);
        Assert.Equal(-1, speed.Dimension.Powers["coglatas.time"]);
        Assert.Equal(-1, speed.UnitPowers["fixture.second"]);
        var restored = ProjectNumericOperations.Evaluate(speed, elapsed, NumericOperator.Multiply).Value!;
        Assert.Equal(distance.Amount, restored.Amount);
        Assert.Equal(distance.Dimension, restored.Dimension);
        Assert.Equal(NumericEvaluationStatus.Invalid, ProjectNumericOperations.Evaluate(distance, elapsed, NumericOperator.Add).Status);
        Assert.Equal(NumericEvaluationStatus.Unknown, ProjectNumericOperations.Evaluate(distance, distance, NumericOperator.Multiply,
            new(MaximumAbsoluteExponent: 1)).Status);
    }

    [Fact]
    public void ComparisonRequiresExplicitPinnedConversion()
    {
        var m = Unit("fixture.meter"); var cm = Unit("fixture.centimeter");
        var meters = Quantity("1", m); var centimeters = Quantity("100", cm);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Evaluate(NumericOperand.From(meters), NumericOperand.From(centimeters), NumericOperator.Compare).Status);
        var left = ProjectUnitConversions.Convert(meters, m, m, []).Value!;
        var right = ProjectUnitConversions.Convert(centimeters, cm, m, [Entry(cm, m, "1", "100")]).Value!;
        var equal = ProjectNumericOperations.Evaluate(NumericOperand.From(left), NumericOperand.From(right), NumericOperator.Compare);
        Assert.Equal(0, equal.Comparison);
        Assert.Equal(NumericEvaluationStatus.Known, equal.Status);
        Assert.Equal(-1, ProjectNumericOperations.Evaluate(Scalar("-1"), Scalar("0"), NumericOperator.Compare).Comparison);
        var future = Unit("fixture.meter", version: 2);
        var newVersion = ProjectUnitConversions.Convert(Quantity("1", future), future, future, []).Value!;
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Evaluate(NumericOperand.From(left), NumericOperand.From(newVersion), NumericOperator.Compare).Status);
    }

    [Theory]
    [InlineData("coglatas.null", NumericEvaluationStatus.Null)]
    [InlineData("coglatas.unknown", NumericEvaluationStatus.Unknown)]
    public void MissingValuesAreNeverConvertedToZero(string state, NumericEvaluationStatus expected)
    {
        var result = ProjectNumericOperations.Evaluate(Scalar("0", state), Scalar("0"), NumericOperator.Multiply);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Value);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public void InvalidAndUnsupportedInputsNeverBecomeNumericSuccess()
    {
        var malformed = NumericOperand.From(TypedProjectValueCodec.Decode(SourceJson.Parse("{}")));
        var unsupported = Scalar("1", kind: "future.numeric");
        var text = Scalar("1", kind: "coglatas.text");
        Assert.Equal(NumericEvaluationStatus.Invalid, ProjectNumericOperations.Evaluate(malformed, Scalar("1"), NumericOperator.Add).Status);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Evaluate(unsupported, Scalar("1"), NumericOperator.Add).Status);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Evaluate(text, Scalar("1"), NumericOperator.Add).Status);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Evaluate(Scalar("1"), Scalar("1"), (NumericOperator)100).Status);
        var zero = ProjectNumericOperations.Evaluate(Scalar("1"), Scalar("0"), NumericOperator.Divide);
        Assert.Equal(NumericEvaluationStatus.Invalid, zero.Status);
        Assert.Equal("coglatas.division-by-zero", zero.Reason);
    }

    [Fact]
    public void UnknownExtensionsRemainLosslessAndNonExecutable()
    {
        var unit = Unit("fixture.a"); var node = JsonNode.Parse(unit.Data.CanonicalText)!;
        node["future"] = JsonNode.Parse("{\"sequence\":[3,2,1],\"large\":123456789012345678901234567890}");
        var extended = SourceJson.Parse(node.ToJsonString());
        Assert.Throws<UnsupportedSourceException>(() => new UnitDefinition(extended, ContentDigest.Compute("coglatas.unit-definition/1", extended.ToCanonicalBytes())));
        var quantityNode = JsonNode.Parse(Quantity("1", unit).Data.CanonicalText)!;
        quantityNode["future"] = true;
        var quantity = new Quantity(SourceJson.Parse(quantityNode.ToJsonString()));
        Assert.Equal(NumericEvaluationStatus.Unsupported, NumericOperand.From(quantity).Status);
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectUnitConversions.Convert(quantity, unit, unit, []).Status);
        Assert.Equal(extended.ToCanonicalBytes(), SourceJson.Parse(extended.ToCanonicalBytes()).ToCanonicalBytes());
    }

    [Fact]
    public void QuantizationIsExplicitWithOriginalBasisAndOnlyApprovedRounding()
    {
        var eighth = ProjectNumericOperations.Evaluate(Scalar("1"), Scalar("8"), NumericOperator.Divide).Value!;
        var rounded = ProjectNumericOperations.Quantize(eighth, 2, "coglatas.nearest-ties-to-even");
        Assert.Equal(NumericEvaluationStatus.Known, rounded.Status);
        Assert.Same(eighth.Amount, rounded.Quantization!.Original);
        Assert.Equal("0.12", rounded.Quantization.Rounded.CanonicalText);
        Assert.Equal(2, rounded.Quantization.Scale);
        Assert.Equal("3/25", rounded.Value!.Amount!.ToString());
        Assert.Equal(NumericEvaluationStatus.Unsupported, ProjectNumericOperations.Quantize(eighth, 2, "future.rounding").Status);
        Assert.Equal(NumericEvaluationStatus.Invalid, ProjectNumericOperations.Quantize(eighth, -1, "coglatas.nearest-ties-to-even").Status);
        Assert.Contains("roundingMode", rounded.Basis.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericBoundsAndCumulativeOperationBudgetsRemainUnknown()
    {
        Assert.Equal(NumericEvaluationStatus.Unknown, ProjectNumericOperations.Evaluate(Scalar("99"), Scalar("99"), NumericOperator.Multiply,
            new(new(3, 2))).Status);
        var first = ProjectNumericOperations.Evaluate(Scalar("1"), Scalar("1"), NumericOperator.Add).Value!;
        Assert.Equal("coglatas.operation-budget-exhausted", ProjectNumericOperations.Evaluate(first, Scalar("1"), NumericOperator.Add,
            new(MaximumOperations: 1)).Reason);
        Assert.Equal("coglatas.operation-budget-exhausted", ProjectNumericOperations.Quantize(first, 1, "coglatas.nearest-ties-to-even",
            new(MaximumOperations: 1)).Reason);
    }
}
