using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class TypedProjectValueTests
{
    private static SourceJson Value(string kind, string state, object? value = null, bool nullable = true, bool acceptsUnknown = true)
    {
        var fields = new Dictionary<string, object?>
        {
            ["type"] = new { kind, schemaVersion = 1, nullable, acceptsUnknown }, ["state"] = state
        };
        if (state == "coglatas.known") fields["value"] = value;
        if (state == "coglatas.unknown") { fields["reason"] = "coglatas.missing-input"; fields["basis"] = new { coverage = "coglatas.partial" }; }
        return SourceJson.Parse(JsonSerializer.Serialize(fields));
    }

    [Fact]
    public void KnownNullUnknownAndAbsenceAreDifferent()
    {
        var known = TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.known", "0"));
        var nullValue = TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.null"));
        var unknown = TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.unknown"));
        Assert.Equal(ValueDecodeStatus.Decoded, known.Status);
        Assert.Equal(ValueState.Known, known.Value!.State);
        Assert.Equal("0", Assert.IsType<ExactDecimal>(known.Value.KnownValue).CanonicalText);
        Assert.Equal(ValueState.Null, nullValue.Value!.State);
        Assert.Null(nullValue.Value.KnownValue);
        Assert.Equal(ValueState.Unknown, unknown.Value!.State);
        Assert.Null(unknown.Value.KnownValue);
        Assert.Equal("coglatas.missing-input", unknown.Value.UnknownReason);
        Assert.NotNull(unknown.Value.UnknownBasis);
        var absent = JsonNode.Parse(known.Data.CanonicalText)!;
        absent.AsObject().Remove("value");
        Assert.Equal(ValueDecodeStatus.Invalid, TypedProjectValueCodec.Decode(SourceJson.Parse(absent.ToJsonString())).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.null", nullable: false)).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.unknown", acceptsUnknown: false)).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, TypedProjectValueCodec.Decode(Value("coglatas.decimal", "coglatas.known")).Status);
    }

    [Theory]
    [InlineData("coglatas.boolean", "true", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.boolean", "0", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.text", "\"\"", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.decimal", "0.1", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.decimal", "\"-0\"", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.decimal", "\"1e2\"", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.rational", "{\"numerator\":\"1\",\"denominator\":\"3\"}", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.rational", "{\"numerator\":\"2\",\"denominator\":\"6\"}", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.rational", "{\"numerator\":\"1\",\"denominator\":\"0\"}", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.rational", "{\"numerator\":\"-1\",\"denominator\":\"-3\"}", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.calendar-period", "{\"years\":0,\"months\":1,\"days\":0}", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.calendar-period", "{\"years\":0,\"months\":2147483648,\"days\":0}", ValueDecodeStatus.Invalid)]
    [InlineData("coglatas.elapsed-duration", "\"-1\"", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.instant", "\"2026-10-31T00:00:00.0000000Z\"", ValueDecodeStatus.Decoded)]
    [InlineData("coglatas.instant", "\"2026-10-31T00:00:00Z\"", ValueDecodeStatus.Invalid)]
    [InlineData("future.numeric", "{\"amount\":123456789012345678901234567890}", ValueDecodeStatus.Unsupported)]
    [InlineData("coglatas.zoned-local-date-time", "{\"local\":\"2026-10-31T00:00:00\"}", ValueDecodeStatus.Unsupported)]
    public void BuiltInValuesValidateWithoutCoercion(string kind, string json, ValueDecodeStatus expected)
    {
        var data = Value(kind, "coglatas.known", JsonSerializer.Deserialize<JsonElement>(json));
        var result = TypedProjectValueCodec.Decode(data);
        Assert.Equal(expected, result.Status);
        Assert.Equal(data.CanonicalText, Encoding.UTF8.GetString(TypedProjectValueCodec.Encode(result)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("value")]
    [InlineData("root")]
    public void UnknownMembersArePreservedAtEveryBoundaryAndPreventCompatibilityClaims(string boundary)
    {
        var node = JsonNode.Parse(Value("coglatas.rational", "coglatas.known", new { numerator = "1", denominator = "3" }).CanonicalText)!;
        var target = boundary == "root" ? node : node[boundary]!;
        target["future"] = JsonNode.Parse("{\"value\":123456789012345678901234567890,\"ordered\":[\"z\",\"a\"],\"null\":null}");
        var data = SourceJson.Parse(node.ToJsonString());
        var result = TypedProjectValueCodec.Decode(data);
        Assert.Equal(ValueDecodeStatus.Unsupported, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(data, SourceJson.Parse(TypedProjectValueCodec.Encode(result)));
    }

    [Theory]
    [InlineData("future.numeric", 1)]
    [InlineData("coglatas.decimal", 2)]
    public void FutureTypeDescriptorsDoNotRequireVersionOneFlags(string kind, int version)
    {
        var data = SourceJson.Parse(JsonSerializer.Serialize(new
        {
            type = new { kind, schemaVersion = version, futureFlag = "future.null-semantics" },
            state = "coglatas.known", value = "0"
        }));
        var result = TypedProjectValueCodec.Decode(data);
        Assert.Equal(ValueDecodeStatus.Unsupported, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(data.ToCanonicalBytes(), TypedProjectValueCodec.Encode(result));
    }

    [Fact]
    public void StatePayloadsCannotContradictEachOther()
    {
        foreach (var state in new[] { "coglatas.null", "coglatas.unknown" })
        {
            var node = JsonNode.Parse(Value("coglatas.decimal", state).CanonicalText)!;
            node["value"] = "0";
            Assert.Equal(ValueDecodeStatus.Invalid, TypedProjectValueCodec.Decode(SourceJson.Parse(node.ToJsonString())).Status);
        }
    }

    [Fact]
    public void UnknownEnumNeverFallsBackToFirstToken()
    {
        var node = JsonNode.Parse(Value("coglatas.enum", "coglatas.known", "future.ready").CanonicalText)!;
        node["type"]!["tokens"] = new JsonArray("coglatas.ready", "coglatas.done");
        var data = SourceJson.Parse(node.ToJsonString());
        Assert.Equal(ValueDecodeStatus.Unsupported, TypedProjectValueCodec.Decode(data).Status);
        node["value"] = "coglatas.ready";
        Assert.Equal(ValueDecodeStatus.Decoded, TypedProjectValueCodec.Decode(SourceJson.Parse(node.ToJsonString())).Status);
    }

    [Theory]
    [InlineData("unit.metre", "dimension.length", ValueDecodeStatus.Decoded)]
    [InlineData("unit.second", "dimension.time", ValueDecodeStatus.Invalid)]
    [InlineData("unit.centimetre", "dimension.length", ValueDecodeStatus.Unsupported)]
    public void QuantityChecksDimensionsAndRequiresExplicitUnitConversion(string unitId, string dimension, ValueDecodeStatus expected)
    {
        var data = SourceJson.Parse(JsonSerializer.Serialize(new
        {
            type = new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = true,
                unitId = "unit.metre", dimension = new Dictionary<string, int> { ["dimension.length"] = 1 } },
            state = "coglatas.known",
            value = new { amount = "0.1", unitId, dimension = new Dictionary<string, int> { [dimension] = 1 } }
        }));
        Assert.Equal(expected, TypedProjectValueCodec.Decode(data).Status);
    }

    [Fact]
    public void DimensionRejectsZeroAndOverflowingPowers()
    {
        Assert.Empty(new Dimension(SourceJson.Parse("{}")).Powers);
        Assert.Throws<FormatException>(() => new Dimension(SourceJson.Parse("{\"dimension.length\":0}")));
        Assert.Throws<FormatException>(() => new Dimension(SourceJson.Parse("{\"dimension.length\":2147483648}")));
    }

    [Fact]
    public void SharedGoldenFixturesRetainSourceCodecIdentityAndExactOpaquePayloads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "typed-values-v1.json");
        var fixtures = SourceJson.Parse(File.ReadAllBytes(path));
        foreach (var entry in fixtures.Value.EnumerateArray())
        {
            var data = SourceJson.Parse(entry.GetProperty("input").GetRawText());
            var result = TypedProjectValueCodec.Decode(data);
            Assert.Equal(Enum.Parse<ValueDecodeStatus>(entry.GetProperty("expected").GetString()!), result.Status);
            Assert.Equal(data.ToCanonicalBytes(), TypedProjectValueCodec.Encode(result));
            var root = SourceJson.Parse("""
                {"entityId":"00000000-0000-4000-8000-000000000001","kind":"coglatas.project","schemaVersion":1,"payload":{}}
                """);
            var document = SourceDocument.Create(DocumentId.New(), "values", root, data);
            Assert.Equal(data, SourceJson.Parse(document.Data.Value.GetProperty("extensions").GetRawText()));
            Assert.Equal(document.Digest, SourceDocument.Parse(document.Data).Digest);
        }
    }
}
