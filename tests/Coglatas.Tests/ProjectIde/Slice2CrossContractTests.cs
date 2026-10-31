using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class Slice2CrossContractTests
{
    private static SourceJson Json(object data) => SourceJson.Parse(JsonSerializer.Serialize(data));
    private static object QuantityType => new { kind = "coglatas.quantity", schemaVersion = 1, nullable = true, acceptsUnknown = true,
        unitId = "fixture.meter", dimension = new Dictionary<string, int> { ["coglatas.length"] = 1 } };
    private static object QuantityValue(string amount) => new { type = QuantityType, state = "coglatas.known",
        value = new { amount, unitId = "fixture.meter", dimension = new Dictionary<string, int> { ["coglatas.length"] = 1 } } };

    [Fact]
    public void CollectionPortQuantitiesNeedPinnedBindingsBeforeArithmetic()
    {
        var type = new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = true, elementType = QuantityType };
        var port = new PortDefinition(Json(new { portId = "00000000-0000-4000-8000-000000000001", direction = "coglatas.input",
            type, minimumConnections = 1, maximumConnections = 1,
            defaultValue = new { type, state = "coglatas.known", value = new[] { QuantityValue("0.1"), QuantityValue("0.2") } } }));
        var values = Assert.IsType<ProjectCollectionValue>(port.DefaultValue!.KnownValue);
        Assert.Equal(NumericEvaluationStatus.Unsupported, NumericOperand.From(values.Items[0]).Status);
        var definition = Json(new { unitId = "fixture.meter", version = 1, dimension = new Dictionary<string, int> { ["coglatas.length"] = 1 } });
        var unit = new UnitDefinition(definition, ContentDigest.Compute("coglatas.unit-definition/1", definition.ToCanonicalBytes()));
        var left = NumericOperand.From(Assert.IsType<Quantity>(values.Items[0].KnownValue), unit);
        var right = NumericOperand.From(Assert.IsType<Quantity>(values.Items[1].KnownValue), unit);
        var sum = ProjectNumericOperations.Evaluate(left, right, NumericOperator.Add);
        Assert.Equal(NumericEvaluationStatus.Known, sum.Status);
        Assert.Equal("3/10", sum.Value!.Amount!.ToString());
        Assert.Equal(port.Data.ToCanonicalBytes(), SourceJson.Parse(port.Data.CanonicalText).ToCanonicalBytes());
    }

    [Theory]
    [InlineData("coglatas.working-duration")]
    [InlineData("coglatas.calendar-period")]
    public void WorkingTimeAndCivilPeriodsCannotBeUsedAsNumericQuantities(string kind)
    {
        var calendar = new { calendarId = "fixture.calendar", version = "1", timeZoneId = "Etc/UTC",
            rulesDigest = new { algorithm = "sha-256", value = new string('a', 64) } };
        object value = kind == "coglatas.working-duration" ? new { ticks = "100", calendar } : new { years = 0, months = 1, days = 0 };
        var decoded = TypedProjectValueCodec.Decode(Json(new { type = new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value }));
        Assert.Equal(ValueDecodeStatus.Decoded, decoded.Status);
        Assert.Equal(NumericEvaluationStatus.Unsupported, NumericOperand.From(decoded).Status);
    }

    [Fact]
    public void UnknownQuantityAndUnknownPredicateKeepInputBasisWithoutSuccessCoercion()
    {
        var value = Json(new { type = QuantityType, state = "coglatas.unknown", reason = "fixture.missing-input", basis = new { coverage = "fixture.partial" } });
        var operand = NumericOperand.From(TypedProjectValueCodec.Decode(value));
        Assert.Equal(NumericEvaluationStatus.Unknown, operand.Status);
        Assert.Contains("fixture.partial", operand.Basis.CanonicalText, StringComparison.Ordinal);
        var predicate = PurePredicate.Parse(Json(new { kind = "coglatas.literal", value = new {
            type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true },
            state = "coglatas.unknown", reason = "fixture.missing-input", basis = new { coverage = "fixture.partial" } } }));
        Assert.Equal(PredicateTruth.Unknown, predicate.Evaluate().Truth);
    }

    [Fact]
    public void OpaqueCollectionExtensionsStayNonExecutableThroughSourceRoundTrip()
    {
        var type = new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = true, elementType = QuantityType };
        var node = JsonSerializer.SerializeToNode(new { type, state = "coglatas.known", value = new[] { QuantityValue("0.1") } })!;
        node["value"]![0]!["value"]!["future"] = JsonNode.Parse("{\"decimal\":12345678901234567890.123456789,\"ordered\":[2,1],\"null\":null}");
        var source = SourceJson.Parse(node.ToJsonString());
        var decoded = TypedProjectValueCodec.Decode(source);
        Assert.Equal(ValueDecodeStatus.Unsupported, decoded.Status); Assert.Null(decoded.Value);
        Assert.Equal(source.ToCanonicalBytes(), TypedProjectValueCodec.Encode(decoded));
    }
}
