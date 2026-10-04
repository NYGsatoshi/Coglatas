using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectSourceTests
{
    private const string Id = "00000000-0000-4000-8000-000000000001";
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures");
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory, name));
    private static JsonObject FixtureNode() => JsonNode.Parse(Fixture("source-v1.json"))!.AsObject();
    private static SourceDecodeResult Decode(JsonObject node) => ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()));

    public static TheoryData<Type> IdTypes => new()
    {
        typeof(TenantId), typeof(ProjectId), typeof(EntityId), typeof(RelationId), typeof(BranchId), typeof(RevisionId),
        typeof(ProposalId), typeof(CandidateRevisionId), typeof(ScenarioId), typeof(ScenarioRevisionId), typeof(DocumentId)
    };

    [Theory]
    [MemberData(nameof(IdTypes))]
    public void TypedIdsRoundTripAndRejectNonDomainUuids(Type type)
    {
        var parse = type.GetMethod("Parse", [typeof(string)])!;
        var id = parse.Invoke(null, [Id])!;
        Assert.Equal(Id, id.ToString());
        var json = JsonSerializer.Serialize(id, type);
        Assert.Equal("\"" + Id + "\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize(json, type));
        foreach (var invalid in new[] { "", "name", Id.Replace("-", ""), "{" + Id + "}", " " + Id,
                     "00000000-0000-0000-0000-000000000000", "00000000-0000-8000-8000-000000000001",
                     "00000000-0000-4000-0000-000000000001" })
            Assert.Throws<TargetInvocationException>(() => parse.Invoke(null, [invalid]));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("42", type));
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name == "op_Implicit");
    }

    [Fact]
    public void CrossTypeIdentityEqualityAndConversionsAreForbidden()
    {
        var identities = new[] { typeof(ProjectId), typeof(EntityId), typeof(RevisionId), typeof(CandidateRevisionId) }
            .Select(type => type.GetMethod("Parse", [typeof(string)])!.Invoke(null, [Id])!).ToArray();
        foreach (var (left, right) in identities.SelectMany((left, i) => identities.Skip(i + 1).Select(right => (left, right))))
            Assert.NotEqual(left, right);
        Assert.Throws<ArgumentException>(() => new BranchRef(default, ProjectId.Parse(Id), BranchId.Parse(Id)));
        Assert.Throws<ArgumentException>(() => JsonSerializer.Serialize(default(EntityId)));
        Assert.False(EntityId.TryParse("bad", out _));
        Assert.True(EntityId.TryParse(Id, out var valid));
        Assert.Equal(Id, valid.ToString());
    }

    [Fact]
    public void CanonicalGoldenBytesAndDomainTaggedDigestsAreStable()
    {
        var decoded = ProjectSourceCodec.Decode(Fixture("source-v1.json"));
        Assert.Equal(SourceDecodeStatus.Unsupported, decoded.Status);
        var source = decoded.RequireSource();
        Assert.Equal(Fixture("source-v1.canonical.json"), ProjectSourceCodec.Encode(source));
        Assert.Equal(Encoding.UTF8.GetString(Fixture("source-v1.sha256")).Trim(), source.Digest.Value);
        Assert.Equal(ProjectSourceCodec.Encode(source), ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(source))));
        Assert.NotEqual(source.Digest.Value, ContentDigest.Compute(SourceDocument.DigestDomain, source.Data.ToCanonicalBytes()).Value);
        Assert.Equal(ContentDigest.Algorithm, SourceFieldsForTest(source.Digest.Data, "algorithm"));
    }

    [Fact]
    public void CanonicalJsonUsesScalarOrderingExactDecimalsAndRequiredEscapes()
    {
        var canonical = SourceJson.Parse(Fixture("canonical-json-v1.json"));
        Assert.Equal(Fixture("canonical-json-v1.canonical.json"), canonical.ToCanonicalBytes());
        Assert.Equal(canonical.ToCanonicalBytes(), SourceJson.Parse(canonical.ToCanonicalBytes()).ToCanonicalBytes());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("ja-JP")]
    public void CanonicalBytesDoNotDependOnCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(Fixture("source-v1.canonical.json"), ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(Fixture("source-v1.json"))));
            Assert.Equal(Fixture("canonical-json-v1.canonical.json"), SourceJson.Parse(Fixture("canonical-json-v1.json")).ToCanonicalBytes());
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Fact]
    public void DocumentReorderingChangesNeitherBytesNorDigestButOpaqueArrayOrderDoes()
    {
        var original = Decode(FixtureNode()).RequireSource();
        var reordered = FixtureNode();
        Reverse(reordered["documents"]!.AsArray());
        Reverse(reordered["manifest"]!["documents"]!.AsArray());
        Assert.Equal(original.Data.CanonicalText, Decode(reordered).RequireSource().Data.CanonicalText);
        Assert.Equal(original.Digest.Value, Decode(reordered).RequireSource().Digest.Value);
        var a = SourceJson.Parse("{\"steps\":[2,1]}");
        var b = SourceJson.Parse("{\"steps\":[1,2]}");
        Assert.NotEqual(a.CanonicalText, b.CanonicalText);
    }

    [Fact]
    public void UnknownValuesRemainAtTheirOriginalObjectBoundaries()
    {
        var result = ProjectSourceCodec.Decode(Fixture("source-v1.json"));
        var source = result.RequireSource();
        using var encoded = JsonDocument.Parse(ProjectSourceCodec.Encode(source));
        var root = encoded.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("futureSource").GetProperty("value").ValueKind);
        Assert.False(root.GetProperty("manifest").GetProperty("futureManifest").GetProperty("visible").GetBoolean());
        var unknown = source.Documents.Single(document => document.Root.Kind == "future.widget");
        Assert.Equal("future.unknown", unknown.Root.Payload.Value.GetProperty("nested").GetProperty("enum").GetString());
        Assert.Equal("123456789012345678901234567890", unknown.Root.Payload.Value.GetProperty("nested").GetProperty("amount").GetRawText());
        Assert.Equal("z", unknown.Root.Payload.Value.GetProperty("ordered")[0].GetString());
        Assert.Equal("future.enum", root.GetProperty("extensionDeclarations")[0].GetProperty("payload").GetProperty("unknownToken").GetString());
        Assert.Equal("é / é / 日本語", unknown.Data.Value.GetProperty("futureAnnotation").GetProperty("name").GetString());
        Assert.Equal(SourceDecodeStatus.Unsupported, result.Status);
    }

    [Theory]
    [InlineData("documents")]
    [InlineData("declarations")]
    [InlineData("relations")]
    [InlineData("extensionDeclarations")]
    [InlineData("packageLock")]
    [InlineData("sourceReferences")]
    [InlineData("manifest")]
    public void MissingRequiredFieldsAreNotDefaulted(string field)
    {
        var node = FixtureNode();
        node.Remove(field);
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
    }

    [Fact]
    public void DuplicateIdsAndDocumentDigestTamperingAreRejected()
    {
        var duplicate = FixtureNode();
        duplicate["documents"]!.AsArray().Add(duplicate["documents"]![0]!.DeepClone());
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(duplicate).Status);
        var tampered = FixtureNode();
        tampered["documents"]![0]!["logicalName"] = "tampered";
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(tampered).Status);
        var missing = FixtureNode();
        missing["manifest"]!["documents"]!.AsArray().RemoveAt(1);
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(missing).Status);
        var repeatedManifest = FixtureNode();
        repeatedManifest["manifest"]!["documents"]!.AsArray().Add(repeatedManifest["manifest"]!["documents"]![0]!.DeepClone());
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(repeatedManifest).Status);
    }

    [Theory]
    [InlineData("format", "coglatas.project-source/2")]
    [InlineData("schemaId", "future.schema")]
    public void FutureWholeSchemasRetainOriginalBytesWithoutParsingAsV1(string field, string value)
    {
        var node = FixtureNode(); node[field] = value;
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var result = ProjectSourceCodec.Decode(bytes);
        Assert.Equal(SourceDecodeStatus.Unsupported, result.Status);
        Assert.Null(result.Source);
        Assert.Equal(bytes, ProjectSourceCodec.Encode(result));
        var recovered = result.RecoverOriginalBytes(); recovered[0] = 0;
        Assert.Equal(bytes, result.RecoverOriginalBytes());
    }

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"a\":{\"x\":1,\"x\":2}}")]
    [InlineData("{\"x\":\"\\ud800\"}")]
    [InlineData("{\"\\udc00\":1}")]
    [InlineData("{\"x\":1e999999999999999}")]
    [InlineData("{\"x\":NaN}")]
    [InlineData("{\"x\":")]
    public void InvalidDraftBuffersAreRetainedExactly(string json)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        var result = ProjectSourceCodec.Decode(raw);
        Assert.Equal(SourceDecodeStatus.Invalid, result.Status);
        Assert.Null(result.Source);
        Assert.Equal(raw, ProjectSourceCodec.Encode(result));
    }

    [Fact]
    public void MalformedUtf8AndBoundExhaustionAreInvalidWithoutTruncation()
    {
        byte[] invalidUtf8 = [123, 34, 120, 34, 58, 34, 0xff, 34, 125];
        Assert.Equal(SourceDecodeStatus.Invalid, ProjectSourceCodec.Decode(invalidUtf8).Status);
        Assert.Equal(invalidUtf8, ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(invalidUtf8)));
        var raw = Fixture("source-v1.json");
        var bounded = ProjectSourceCodec.Decode(raw, new(MaximumBytes: 10));
        Assert.Equal(SourceDecodeStatus.Invalid, bounded.Status);
        Assert.Equal(raw, bounded.RecoverOriginalBytes());
        Assert.Throws<FormatException>(() => SourceJson.Parse("1e1000", new(MaximumNumberCharacters: 10)));
        Assert.Throws<FormatException>(() => SourceJson.Parse("[[[1]]]", new(MaximumDepth: 2)));
        Assert.Throws<FormatException>(() => SourceJson.Parse("[1e20,1e20,1e20]", new(MaximumBytes: 50)));
    }

    [Fact]
    public void RenameAndMoveRetainIdentityAndCrossDocumentReference()
    {
        var source = ProjectSourceCodec.Decode(Fixture("source-v1.json")).RequireSource();
        var old = source.Documents[1];
        var moved = SourceDocument.Create(old.DocumentId, "new/logical/name", old.Root.Data);
        Assert.Equal(old.DocumentId, moved.DocumentId);
        Assert.Equal(old.Root.EntityId, moved.Root.EntityId);
        Assert.NotEqual(old.Digest.Value, moved.Digest.Value);
        var rebuilt = ProjectSource.Create(source.Context, [source.Documents[0], moved], sourceReferences: source.SourceReferences);
        Assert.Equal(old.Root.EntityId.ToString(), rebuilt.SourceReferences.Value[0].GetProperty("entityId").GetString());
        Assert.Equal(old.DocumentId.ToString(), rebuilt.SourceReferences.Value[0].GetProperty("documentId").GetString());
    }

    [Fact]
    public void CoreHasNoProjectVendorTransportOrPersistenceDependency()
    {
        var assembly = typeof(ProjectSource).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is not null &&
            (reference.Name.StartsWith("Coglatas.", StringComparison.Ordinal) || reference.Name.Contains("Avalonia", StringComparison.Ordinal) ||
             reference.Name.Contains("EntityFramework", StringComparison.Ordinal) || reference.Name.Contains("AspNetCore", StringComparison.Ordinal) ||
             reference.Name.Contains("Kiota", StringComparison.Ordinal) || reference.Name.Contains("Msagl", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(assembly.GetExportedTypes(), type => type.Namespace == "Coglatas.Domain.ProjectIde");
    }

    [Fact]
    public void DuplicateEntityRelationAndDescriptorSetsAreRejected()
    {
        var documentId = "00000000-0000-4000-8000-000000000005";
        var entityId = "00000000-0000-4000-8000-000000000009";
        var declaration = JsonNode.Parse("{\"entityId\":\"" + entityId + "\",\"documentId\":\"" + documentId +
            "\",\"kind\":\"future.entity\",\"schemaVersion\":1,\"payload\":{\"state\":\"future.enum\"}}")!;
        var node = FixtureNode();
        node["declarations"]!.AsArray().Add(declaration.DeepClone());
        var decoded = Decode(node);
        Assert.Equal(SourceDecodeStatus.Unsupported, decoded.Status);
        Assert.Equal("future.enum", decoded.RequireSource().Declarations.Value[0].GetProperty("payload").GetProperty("state").GetString());
        node["declarations"]!.AsArray().Add(declaration.DeepClone());
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
        var relation = JsonNode.Parse("{\"relationId\":\"" + entityId + "\",\"documentId\":\"" + documentId +
            "\",\"kind\":\"future.relation\",\"schemaVersion\":1,\"source\":{\"entityId\":\"" + entityId +
            "\"},\"target\":{\"entityId\":\"" + Id + "\"},\"payload\":{}}")!;
        node = FixtureNode();
        node["relations"]!.AsArray().Add(relation.DeepClone());
        Assert.Equal(SourceDecodeStatus.Unsupported, Decode(node).Status);
        node["relations"]!.AsArray().Add(relation.DeepClone());
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
        node = FixtureNode();
        node["extensionDeclarations"]!.AsArray().Add(node["extensionDeclarations"]![0]!.DeepClone());
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
    }

    [Fact]
    public void EmptyProjectRootsBadScopeAndUnpinnedReferencesAreRejected()
    {
        var source = ProjectSource.Create(SourceRevisionContext.Committed(new(
            new(TenantId.Parse(Id), ProjectId.Parse(Id), BranchId.Parse(Id)), RevisionId.Parse(Id))),
            [SourceDocument.Create(DocumentId.Parse(Id), "project", SourceJson.Parse(
                "{\"entityId\":\"" + Id + "\",\"kind\":\"coglatas.project\",\"schemaVersion\":1,\"payload\":{}}"))]);
        Assert.Equal(SourceDecodeStatus.StructurallyDecoded, ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(source)).Status);
        var node = FixtureNode();
        node["documents"] = new JsonArray();
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
        node = FixtureNode(); node["tenantId"] = "00000000-0000-4000-8000-00000000000a";
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
        node = FixtureNode(); node["sourceReferences"]![0]!["entityId"] = "display name";
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
        node = FixtureNode(); node["sourceReferences"]![0]!["externalManifest"] = new JsonObject { ["projectId"] = Id };
        Assert.Equal(SourceDecodeStatus.Invalid, Decode(node).Status);
    }

    [Fact]
    public void UnknownContextAndManifestEntryDataCannotBeCalledSupported()
    {
        var minimal = ProjectSource.Create(SourceRevisionContext.Committed(new(
            new(TenantId.Parse(Id), ProjectId.Parse(Id), BranchId.Parse(Id)), RevisionId.Parse(Id))),
            [SourceDocument.Create(DocumentId.Parse(Id), "project", SourceJson.Parse(
                "{\"entityId\":\"" + Id + "\",\"kind\":\"coglatas.project\",\"schemaVersion\":1,\"payload\":{}}"))]);
        var node = JsonNode.Parse(minimal.Data.CanonicalText)!.AsObject();
        node["context"]!["futureMeaning"] = new JsonObject { ["opaque"] = true };
        var result = Decode(node);
        Assert.Equal(SourceDecodeStatus.Unsupported, result.Status);
        Assert.True(result.RequireSource().Context.Data.Value.GetProperty("futureMeaning").GetProperty("opaque").GetBoolean());
        node = JsonNode.Parse(minimal.Data.CanonicalText)!.AsObject();
        node["manifest"]!["documents"]![0]!["futureMeaning"] = true;
        Assert.Equal(SourceDecodeStatus.Unsupported, Decode(node).Status);
    }

    [Fact]
    public void CustomProcessingBoundsApplyThroughNestedUnknownObjectBoundaries()
    {
        var node = FixtureNode();
        JsonNode nested = JsonValue.Create(1);
        for (var depth = 0; depth < 80; depth++) nested = new JsonObject { ["child"] = nested };
        node["futureSource"] = nested;
        var raw = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { MaxDepth = 128 }));
        Assert.Equal(SourceDecodeStatus.Invalid, ProjectSourceCodec.Decode(raw).Status);
        var larger = new SourceCodecLimits(MaximumDepth: 128);
        var decoded = ProjectSourceCodec.Decode(raw, larger);
        Assert.Equal(SourceDecodeStatus.Unsupported, decoded.Status);
        Assert.NotNull(decoded.Source);
        Assert.Equal(ProjectSourceCodec.Encode(decoded), ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(decoded), larger)));
    }

    private static void Reverse(JsonArray array)
    {
        var items = array.Select(item => item!.DeepClone()).Reverse().ToArray();
        array.Clear(); foreach (var item in items) array.Add(item);
    }
    private static string? SourceFieldsForTest(SourceJson json, string field) => json.Value.GetProperty(field).GetString();
}
