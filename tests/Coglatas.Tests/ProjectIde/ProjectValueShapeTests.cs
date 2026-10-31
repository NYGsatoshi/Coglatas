using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectValueShapeTests
{
    private const string Tenant = "00000000-0000-4000-8000-000000000001";
    private const string Project = "00000000-0000-4000-8000-000000000002";
    private const string Branch = "00000000-0000-4000-8000-000000000003";
    private const string Revision = "00000000-0000-4000-8000-000000000004";
    private const string Document = "00000000-0000-4000-8000-000000000005";
    private const string Target = "00000000-0000-4000-8000-000000000006";
    private const string Other = "00000000-0000-4000-8000-000000000007";
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static JsonObject Type(string kind, bool nullable = true, bool acceptsUnknown = true) =>
        JsonSerializer.SerializeToNode(new { kind, schemaVersion = 1, nullable, acceptsUnknown })!.AsObject();
    private static JsonObject Known(JsonObject type, object? value) => new()
    { ["type"] = type.DeepClone(), ["state"] = "coglatas.known", ["value"] = JsonSerializer.SerializeToNode(value) };
    private static JsonObject Text(string text) => Known(Type("coglatas.text"), text);
    private static JsonObject CollectionType(string kind, JsonObject? child = null)
    {
        var type = Type(kind);
        if (kind == "coglatas.map") { type["keyType"] = Type("coglatas.text"); type["valueType"] = child ?? Type("coglatas.text"); }
        else type["elementType"] = child ?? Type("coglatas.text");
        if (kind is "coglatas.map" or "coglatas.set") type["identity"] = "coglatas.canonical-value";
        return type;
    }
    private static ValueDecodeResult Decode(JsonNode value, ProjectValueLimits? limits = null) =>
        TypedProjectValueCodec.Decode(SourceJson.Parse(value.ToJsonString()), valueLimits: limits);
    private static SourceRevisionContext Context(string tenant = Tenant, string project = Project, string branch = Branch, string revision = Revision) =>
        SourceRevisionContext.Committed(new(new(TenantId.Parse(tenant), ProjectId.Parse(project), BranchId.Parse(branch)), RevisionId.Parse(revision)));
    private static JsonObject Reference(string kind)
    {
        var idField = kind switch
        {
            "coglatas.entity-reference" => "entityId", "coglatas.document-reference" => "documentId",
            "coglatas.resource-reference" => "resourceId", _ => "snapshotId"
        };
        var payload = new JsonObject
        {
            ["tenantId"] = Tenant, ["projectId"] = Project, [idField] = Target,
            ["context"] = JsonNode.Parse(Context().Data.CanonicalText),
            ["origin"] = JsonNode.Parse(ProjectLocation.Create(DocumentId.Parse(Document), Context(), "/ports/0/defaultValue").Data.CanonicalText),
            ["manifest"] = new JsonObject { ["version"] = "1", ["digest"] = new JsonObject { ["algorithm"] = "sha-256", ["value"] = new string('a', 64) } }
        };
        var type = Type(kind); type["expectedKind"] = "coglatas.task";
        return Known(type, payload);
    }

    [Theory]
    [InlineData("coglatas.entity-reference")]
    [InlineData("coglatas.document-reference")]
    [InlineData("coglatas.resource-reference")]
    [InlineData("coglatas.external-snapshot-reference")]
    public void TypedReferenceRetainsPinnedIdentityManifestAndSourceWithoutResolving(string kind)
    {
        var data = Reference(kind);
        var result = Decode(data);
        Assert.Equal(ValueDecodeStatus.Decoded, result.Status);
        var reference = Assert.IsType<TypedProjectReference>(result.Value!.KnownValue);
        Assert.Equal(kind, reference.Kind);
        Assert.Equal(ReferenceContextStatus.MatchesPinnedContext, reference.ValidateTargetContext(Context()));
        Assert.Equal(DocumentId.Parse(Document), reference.Origin.DocumentId);
        reference.RequireManifest("1", ContentDigest.Parse(Json(new { algorithm = "sha-256", value = new string('a', 64) })));
        Assert.Throws<FormatException>(() => reference.RequireManifest("2", reference.ManifestDigest));
        Assert.Throws<FormatException>(() => reference.RequireManifest("1", ContentDigest.Compute("different", [])));
        Assert.Equal(result.Data.ToCanonicalBytes(), TypedProjectValueCodec.Encode(result));
        Assert.Equal(ValueDecodeStatus.Decoded, TypedProjectValueCodec.Decode(SourceJson.Parse(TypedProjectValueCodec.Encode(result))).Status);
    }

    [Theory]
    [InlineData("tenant", ReferenceContextStatus.ScopeMismatch)]
    [InlineData("project", ReferenceContextStatus.ScopeMismatch)]
    [InlineData("branch", ReferenceContextStatus.BranchMismatch)]
    [InlineData("revision", ReferenceContextStatus.RevisionMismatch)]
    public void ReferenceContextChecksNeverCollapseDistinctScopes(string field, ReferenceContextStatus expected)
    {
        var reference = Assert.IsType<TypedProjectReference>(Decode(Reference("coglatas.entity-reference")).Value!.KnownValue);
        var context = Context(field == "tenant" ? Other : Tenant, field == "project" ? Other : Project,
            field == "branch" ? Other : Branch, field == "revision" ? Other : Revision);
        Assert.Equal(expected, reference.ValidateTargetContext(context));
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("projectId")]
    [InlineData("entityId")]
    [InlineData("context")]
    [InlineData("origin")]
    [InlineData("manifest")]
    public void ReferenceRequiresEveryIdentityAndInputContext(string missing)
    {
        var node = Reference("coglatas.entity-reference"); node["value"]!.AsObject().Remove(missing);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(node).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Task name")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-4000-8000-00000000000A")]
    public void ReferenceRejectsNoncanonicalOrDisplayIdentity(string id)
    {
        var node = Reference("coglatas.entity-reference"); node["value"]!["entityId"] = id;
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(node).Status);
    }

    [Fact]
    public void InconsistentReferenceScopeAndUnknownContextFailSafely()
    {
        var node = Reference("coglatas.entity-reference"); node["value"]!["projectId"] = Other;
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(node).Status);
        node = Reference("coglatas.entity-reference"); node["value"]!["origin"]!["context"]!["branch"]!["tenantId"] = Other;
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(node).Status);
        node = Reference("coglatas.entity-reference"); node["value"]!["context"]!["future"] = 5;
        AssertOpaque(node);
    }

    [Theory]
    [InlineData("coglatas.list")]
    [InlineData("coglatas.sequence")]
    [InlineData("coglatas.set")]
    public void CollectionsContainTypedValuesAndPreserveOrder(string kind)
    {
        var result = Decode(Known(CollectionType(kind), new[] { Text("a"), Text("b") }));
        Assert.Equal(ValueDecodeStatus.Decoded, result.Status);
        var values = Assert.IsType<ProjectCollectionValue>(result.Value!.KnownValue);
        Assert.Equal(new[] { "a", "b" }, values.Items.Select(item => Assert.IsType<string>(item.KnownValue)));
        Assert.Throws<NotSupportedException>(() => ((IList<TypedProjectValue>)values.Items).Clear());
        Assert.Empty(values.Entries);
    }

    [Fact]
    public void MapRequiresMatchingKeyAndValueTypesAndUniqueCanonicalKeys()
    {
        var entry = new { key = Text("a"), value = Text("first") };
        var type = CollectionType("coglatas.map");
        var result = Decode(Known(type, new[] { entry, new { key = Text("b"), value = Text("second") } }));
        Assert.Equal(2, Assert.IsType<ProjectCollectionValue>(result.Value!.KnownValue).Entries.Count);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new[] { entry, entry })).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new[] { new { key = Known(Type("coglatas.boolean"), true), value = Text("x") } })).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new[] { new { key = Text("a"), value = Known(Type("coglatas.boolean"), true) } })).Status);
    }

    [Fact]
    public void SetIdentityIsExplicitAndDuplicateAndUnorderedRepresentationsAreRejected()
    {
        var type = CollectionType("coglatas.set");
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new[] { Text("a"), Text("a") })).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new[] { Text("b"), Text("a") })).Status);
        type["identity"] = "future.object-equality";
        AssertOpaque(Known(type, new[] { Text("a") }));
        type.Remove("identity");
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, Array.Empty<object>())).Status);
    }

    [Theory]
    [InlineData("coglatas.null", ValueState.Null)]
    [InlineData("coglatas.unknown", ValueState.Unknown)]
    public void NullAndUnknownElementsRetainTheirSeparateStates(string token, ValueState expected)
    {
        var element = new JsonObject { ["type"] = Type("coglatas.text"), ["state"] = token };
        if (expected == ValueState.Unknown) { element["reason"] = "coglatas.missing-input"; element["basis"] = new JsonObject { ["coverage"] = "coglatas.partial" }; }
        var result = Decode(Known(CollectionType("coglatas.list"), new[] { element }));
        var item = Assert.Single(Assert.IsType<ProjectCollectionValue>(result.Value!.KnownValue).Items);
        Assert.Equal(expected, item.State); Assert.Null(item.KnownValue);
        var map = Known(CollectionType("coglatas.map"), new[] { new { key = element, value = Text("value") } });
        AssertOpaque(map);
    }

    [Fact]
    public void OptionalAbsenceNullAndUnknownAreDifferentFromEmptyCollection()
    {
        var type = CollectionType("coglatas.optional");
        var absent = Decode(Known(type, new { present = false }));
        Assert.False(Assert.IsType<OptionalProjectValue>(absent.Value!.KnownValue).Present);
        var present = Decode(Known(type, new { present = true, value = Text("x") }));
        Assert.True(Assert.IsType<OptionalProjectValue>(present.Value!.KnownValue).Present);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new { present = true })).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(type, new { present = false, value = Text("x") })).Status);
        Assert.Empty(Assert.IsType<ProjectCollectionValue>(Decode(Known(CollectionType("coglatas.list"), Array.Empty<object>())).Value!.KnownValue).Items);
        var nullable = CollectionType("coglatas.nullable");
        Assert.Equal(ValueDecodeStatus.Decoded, Decode(new JsonObject { ["type"] = nullable, ["state"] = "coglatas.null" }).Status);
    }

    [Fact]
    public void NestedCollectionsEnforceDepthElementAndTotalNodeBudgets()
    {
        var inner = CollectionType("coglatas.list");
        var nested = Known(CollectionType("coglatas.list", inner), new[] { Known(inner, new[] { Text("x") }) });
        Assert.Equal(ValueDecodeStatus.Decoded, Decode(nested).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(nested, new(MaximumDepth: 1)).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(nested, new(MaximumNodes: 3)).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(inner, new[] { Text("a"), Text("b") }), new(MaximumElements: 1)).Status);
        var mismatch = Known(inner, new[] { Known(Type("coglatas.decimal"), "1") });
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(mismatch).Status);
    }

    private static JsonObject StructureType(bool required = true)
    {
        var type = Type("coglatas.structure");
        type["fields"] = new JsonArray(new JsonObject { ["name"] = "name", ["required"] = required, ["type"] = Type("coglatas.text") });
        return type;
    }

    [Fact]
    public void StructureFieldsRespectRequiredOptionalNullableAndUnknownContracts()
    {
        var result = Decode(Known(StructureType(), new { name = Text("value") }));
        Assert.Equal("value", Assert.IsType<ProjectStructureValue>(result.Value!.KnownValue).Fields["name"].KnownValue);
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(StructureType(), new { })).Status);
        Assert.Empty(Assert.IsType<ProjectStructureValue>(Decode(Known(StructureType(false), new { })).Value!.KnownValue).Fields);
        var nullText = new JsonObject { ["type"] = Type("coglatas.text"), ["state"] = "coglatas.null" };
        Assert.Equal(ValueState.Null, Assert.IsType<ProjectStructureValue>(Decode(Known(StructureType(), new { name = nullText })).Value!.KnownValue).Fields["name"].State);
        AssertOpaque(Known(StructureType(), new { name = Text("x"), future = new { raw = "retained" } }));
        var duplicate = StructureType(); duplicate["fields"]!.AsArray().Add(duplicate["fields"]![0]!.DeepClone());
        Assert.Equal(ValueDecodeStatus.Invalid, Decode(Known(duplicate, new { name = Text("x") })).Status);
    }

    [Fact]
    public void StructureCanCarryAResourceReferenceWithoutClaimingResourceAvailability()
    {
        var reference = Reference("coglatas.resource-reference");
        var type = Type("coglatas.structure");
        type["fields"] = new JsonArray(new JsonObject { ["name"] = "resource", ["required"] = true, ["type"] = reference["type"]!.DeepClone() });
        var result = Decode(Known(type, new { resource = reference }));
        Assert.IsType<TypedProjectReference>(Assert.IsType<ProjectStructureValue>(result.Value!.KnownValue).Fields["resource"].KnownValue);
    }

    [Fact]
    public void NamedAndUnknownExternalTypesStayOpaqueWithoutRuntimeTypeLoading()
    {
        var named = Type("coglatas.named-structure"); named["typeId"] = "fixture.node"; named["self"] = "fixture.node";
        AssertOpaque(Known(named, new { next = "fixture.node" }));
        var future = new JsonObject { ["kind"] = "future.external", ["schemaVersion"] = 8, ["clrType"] = "System.IO.File" };
        AssertOpaque(Known(CollectionType("coglatas.list", future), new[] { new { arbitrary = "retained" } }));
    }

    [Fact]
    public void CollectionPortDefaultsKeepExistingConnectionAndTypeRules()
    {
        var type = CollectionType("coglatas.list");
        var port = Json(new { portId = Target, direction = "coglatas.input", type, minimumConnections = 1, maximumConnections = 1,
            defaultValue = Known(type, new[] { Text("value") }) });
        Assert.IsType<ProjectCollectionValue>(new PortDefinition(port).DefaultValue!.KnownValue);
        var node = JsonNode.Parse(port.CanonicalText)!; node["maximumConnections"] = 2;
        Assert.Throws<UnsupportedSourceException>(() => new PortDefinition(SourceJson.Parse(node.ToJsonString())));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void CanonicalBytesDigestAndRoundTripAreCultureIndependent(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var node = Known(CollectionType("coglatas.list"), new[] { Text("\u00e9"), Text("e\u0301") });
            var before = Decode(node);
            var expected = ContentDigest.Compute("fixture.collection/1", TypedProjectValueCodec.Encode(before));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var after = Decode(node);
            Assert.Equal(expected, ContentDigest.Compute("fixture.collection/1", TypedProjectValueCodec.Encode(after)));
            Assert.Equal(before.Data.ToCanonicalBytes(), TypedProjectValueCodec.Encode(after));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("type")]
    [InlineData("value")]
    [InlineData("root")]
    public void UnsupportedExtensionsAreLosslesslyRetained(string boundary)
    {
        var node = Reference("coglatas.entity-reference");
        var target = boundary == "root" ? node : node[boundary]!;
        target["future"] = JsonNode.Parse("{\"number\":123456789012345678901234567890.123456789,\"ordered\":[\"z\",\"a\"],\"nil\":null}");
        AssertOpaque(node);
    }

    private static void AssertOpaque(JsonNode node)
    {
        var result = Decode(node);
        Assert.Equal(ValueDecodeStatus.Unsupported, result.Status); Assert.Null(result.Value);
        Assert.Equal(SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes(), TypedProjectValueCodec.Encode(result));
    }

    [Fact]
    public void VersionedShapeFixturesRoundTripThroughSourceDocumentsAndManifests()
    {
        var fixtures = SourceJson.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "value-shapes-v1.json")));
        foreach (var entry in fixtures.Value.EnumerateArray())
        {
            var input = SourceJson.Parse(entry.GetProperty("input").GetRawText());
            var result = TypedProjectValueCodec.Decode(input);
            Assert.Equal(Enum.Parse<ValueDecodeStatus>(entry.GetProperty("expected").GetString()!), result.Status);
            Assert.Equal(input.ToCanonicalBytes(), TypedProjectValueCodec.Encode(result));
            var document = SourceDocument.Create(DocumentId.Parse(Document), "fixture",
                Json(new { entityId = Target, kind = "coglatas.project", schemaVersion = 1, payload = new { } }), input);
            var source = ProjectSource.Create(Context(), new[] { document });
            var decoded = ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(source)).RequireSource();
            Assert.Equal(input, SourceJson.Parse(decoded.Documents[0].Data.Value.GetProperty("extensions").GetRawText()));
            Assert.Equal(source.Digest, decoded.Digest);
        }
    }
}
