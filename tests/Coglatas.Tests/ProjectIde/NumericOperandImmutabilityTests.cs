using System.Collections;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
namespace Coglatas.Tests.ProjectIde;
public sealed class NumericOperandImmutabilityTests
{
    private static SourceJson Encode(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static UnitDefinition Unit()
    {
        var data = Encode(new { unitId = "fixture.meter", version = 1, dimension = new Dictionary<string, int> { ["coglatas.length"] = 1 } });
        return new(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()));
    }
    private static NumericOperand Quantity(UnitDefinition unit) => NumericOperand.From(
        new Quantity(Encode(new { amount = "2", unitId = unit.UnitId, dimension = unit.Dimension.Data.Value })), unit);
    private static void ChangeStorage(IReadOnlyDictionary<string, int> view, string key, bool clearOnly)
    {
        var root = (view as ICollection)?.SyncRoot;
        try
        {
            if (root is IDictionary<string, int> map) { map.Clear(); if (!clearOnly) map.Add(key, 2); }
            else if (root is ICollection<KeyValuePair<string, int>> pairs && !pairs.IsReadOnly) { pairs.Clear(); if (!clearOnly) pairs.Add(new(key, 2)); }
            else if (root is IDictionary legacy && !legacy.IsReadOnly) { legacy.Clear(); if (!clearOnly) legacy.Add(key, 2); }
        }
        catch (NotSupportedException) { }
    }
    [Theory]
    [InlineData(NumericOperator.Add, true)]
    [InlineData(NumericOperator.Compare, true)]
    [InlineData(NumericOperator.Multiply, false)]
    [InlineData(NumericOperator.Divide, false)]
    public void UnitPowerViewsKeepExactArithmeticAndRetainedInputMeaning(NumericOperator operation, bool clearOnly)
    {
        var unit = Unit(); var left = Quantity(unit); var right = Quantity(unit);
        var data = left.Data; var before = ProjectNumericOperations.Evaluate(left, right, operation);
        Assert.Equal(NumericEvaluationStatus.Known, before.Status);
        ChangeStorage(left.UnitPowers, Assert.Single(left.UnitPowers).Key, clearOnly);
        var after = ProjectNumericOperations.Evaluate(left, right, operation);
        Assert.Equal(data, left.Data);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Comparison, after.Comparison);
        Assert.Equal(before.Value?.Data, after.Value?.Data);
        var retained = SourceJson.Parse(data.Value.GetProperty("units").GetRawText());
        Assert.Equal(retained, Encode(left.UnitPowers));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)left.UnitPowers).Clear());
    }
    [Fact]
    public void ScalarInputsCannotAcquireUnrecordedUnitFactorsAfterConstruction()
    {
        var value = TypedProjectValueCodec.Decode(Encode(new { type = new { kind = "coglatas.decimal", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "2" }));
        var left = NumericOperand.From(value); var right = NumericOperand.From(value);
        var before = ProjectNumericOperations.Evaluate(left, right, NumericOperator.Add);
        Assert.Equal(NumericEvaluationStatus.Known, before.Status); Assert.Empty(left.UnitPowers);
        var factor = Assert.Single(Quantity(Unit()).UnitPowers).Key;
        ChangeStorage(left.UnitPowers, factor, false);
        var after = ProjectNumericOperations.Evaluate(left, right, NumericOperator.Add);
        Assert.Equal(before.Status, after.Status); Assert.Equal(before.Value!.Data, after.Value!.Data);
        Assert.Empty(left.UnitPowers);
    }
}
