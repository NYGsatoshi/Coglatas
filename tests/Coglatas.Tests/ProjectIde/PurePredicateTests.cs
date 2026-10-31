using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class PurePredicateTests
{
    public static TheoryData<bool?, bool?> Inputs => new()
    { { true, true }, { true, false }, { true, null }, { false, true }, { false, false }, { false, null }, { null, true }, { null, false }, { null, null } };
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static PurePredicate Binary(string kind, bool? left, bool? right) => PurePredicate.Parse(Json(new { kind, operands = new[] { PlanDeclarationTests.Literal(left), PlanDeclarationTests.Literal(right) } }));

    [Theory]
    [MemberData(nameof(Inputs))]
    public void BooleanOperatorsUseThreeValuedLogic(bool? left, bool? right)
    {
        var and = left == false || right == false ? PredicateTruth.False : left == true && right == true ? PredicateTruth.True : PredicateTruth.Unknown;
        var or = left == true || right == true ? PredicateTruth.True : left == false && right == false ? PredicateTruth.False : PredicateTruth.Unknown;
        Assert.Equal(and, Binary("coglatas.and", left, right).Evaluate().Truth);
        Assert.Equal(or, Binary("coglatas.or", left, right).Evaluate().Truth);
    }

    [Theory]
    [InlineData(true, PredicateTruth.False)]
    [InlineData(false, PredicateTruth.True)]
    [InlineData(null, PredicateTruth.Unknown)]
    public void NegationRetainsUnknown(bool? value, PredicateTruth expected)
    {
        var predicate = PurePredicate.Parse(Json(new { kind = "coglatas.not", operands = new[] { PlanDeclarationTests.Literal(value) } }));
        Assert.Equal(expected, predicate.Evaluate().Truth);
    }

    [Fact]
    public void ShortCircuitRetainsOnlyActuallyEvaluatedPremises()
    {
        var conjunction = Binary("coglatas.and", false, null).Evaluate();
        Assert.Equal(PredicateTruth.False, conjunction.Truth);
        Assert.Single(conjunction.EvaluatedPremises);
        Assert.Single(Binary("coglatas.or", true, null).Evaluate().EvaluatedPremises);
        Assert.Equal(2, Binary("coglatas.and", null, false).Evaluate().EvaluatedPremises.Count);
    }

    [Fact]
    public void UnsupportedOperatorsAndExhaustedLogicalBoundsNeverExecute()
    {
        Assert.Throws<UnsupportedSourceException>(() => PurePredicate.Parse(Json(new { kind = "future.execute", url = "https://example.invalid" })));
        var binary = Binary("coglatas.and", true, null);
        Assert.Throws<FormatException>(() => PurePredicate.Parse(binary.Data, maximumNodes: 2));
        Assert.Throws<FormatException>(() => PurePredicate.Parse(binary.Data, maximumDepth: 1));
        Assert.Throws<FormatException>(() => PurePredicate.Parse(Json(new { kind = "coglatas.and", operands = Array.Empty<object>() })));
        Assert.Throws<FormatException>(() => PurePredicate.Parse(Json(new { kind = "coglatas.not", operands = new[] { PlanDeclarationTests.Literal(true), PlanDeclarationTests.Literal(false) } })));
    }
}
