using System.Collections;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
namespace Coglatas.Tests.ProjectIde;
public sealed class EvaluationSnapshotImmutabilityTests
{
    private static SourceJson Encode(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static NumericOperand Number(string value) => NumericOperand.From(TypedProjectValueCodec.Decode(Encode(new { type = new { kind = "coglatas.decimal", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value })));
    private static object Literal(bool value) => new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value } };
    [Theory]
    [InlineData("2", "3", NumericOperator.Add)]
    [InlineData("2", "3", NumericOperator.Multiply)]
    [InlineData("2", "3", NumericOperator.Compare)]
    public void NumericResultInputsStillReproduceRetainedValueAndBasis(string left, string right, NumericOperator operation)
    {
        var result = ProjectNumericOperations.Evaluate(Number(left), Number(right), operation);
        Assert.Equal(NumericEvaluationStatus.Known, result.Status);
        if (result.Inputs is ICollection legacy && legacy.SyncRoot is NumericOperand[] storage) storage[0] = Number("20");
        var replay = ProjectNumericOperations.Evaluate(result.Inputs[0], result.Inputs[1], operation);
        Assert.Equal(result.Basis, replay.Basis);
        Assert.Equal(result.Value?.Data, replay.Value?.Data);
        Assert.Equal(result.Comparison, replay.Comparison);
        Assert.Throws<NotSupportedException>(() => ((IList<NumericOperand>)result.Inputs)[0] = Number("20"));
    }
    [Fact]
    public void ShortCircuitTruthKeepsItsActuallyEvaluatedLiteral()
    {
        var predicate = PurePredicate.Parse(Encode(new { kind = "coglatas.and", operands = new[] { Literal(false), Literal(true) } }));
        var result = predicate.Evaluate();
        Assert.Equal(PredicateTruth.False, result.Truth);
        var expected = Assert.Single(result.EvaluatedPremises);
        if (result.EvaluatedPremises is ICollection legacy && legacy.SyncRoot is SourceJson[] storage)
            storage[0] = PurePredicate.Parse(Encode(Literal(true))).Literal!.Data;
        Assert.Equal(expected, Assert.Single(result.EvaluatedPremises));
        Assert.Throws<NotSupportedException>(() => ((IList<SourceJson>)result.EvaluatedPremises).Clear());
    }
}
