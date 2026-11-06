using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;

namespace Coglatas.Tests.ProjectIde;

public sealed class VersionedArtifactTests
{
    [Fact]
    public void Canonical_golden_vectors_match_Node_hashes_without_rounding_or_array_sorting()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "artifact-hash-vectors-v1.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        var vectors = fixture.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Equal(4, vectors.Length);
        foreach (var vector in vectors)
        {
            var canonical = vector.GetProperty("canonical").GetString()!;
            var data = SourceJson.Parse(canonical);
            Assert.Equal(canonical, data.CanonicalText);
            Assert.Equal(vector.GetProperty("sha256").GetString(),
                ContentDigest.Compute(vector.GetProperty("domain").GetString()!, data.ToCanonicalBytes()).Value);
        }
    }

    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ArtifactSourcePin Pin(ProjectSource? source = null) => ArtifactSourcePin.FromSource(source ?? CompilerContractFixtures.Source());
    private static PrototypeArtifact Manifest(ProjectSource? source = null, params object[] entries) =>
        PrototypeArtifact.Create(PrototypeArtifactRegistry.DependencyManifest, Pin(source), Json(new { entries }));
    private static PrototypeArtifact Change(PrototypeArtifact artifact, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(artifact.Data.CanonicalText)!.AsObject(); change(node);
        return PrototypeArtifact.Parse(SourceJson.Parse(node.ToJsonString()));
    }
    private static object Entry(string key = "a", string kind = "unit", string? digest = null, ProjectSource? source = null) => new
    {
        entryKey = key, dependencyKind = kind,
        expected = new ArtifactIdentity("coglatas.prototype." + kind, 1, 1, digest ?? new string('a', 64)).Data.Value,
        scope = new { kind = "project", context = (source ?? CompilerContractFixtures.Source()).Context.Data.Value },
        target = new { kind, id = CompilerContractFixtures.Id(9) }, coverageClaim = "complete",
        sourceBinding = new { referenceSchemaVersion = 2, referenceKey = key }
    };
    private static PrototypeArtifact Actual(PrototypeArtifact manifest, string enumeration = "completed", params object[] entries) =>
        PrototypeArtifact.Create(PrototypeArtifactRegistry.MaterializationSet, manifest.Source!,
            Json(new { dependencyManifest = manifest.Identity.Data.Value, entries, enumeration }));
    private static PrototypeArtifact Receipt(PrototypeArtifact association, PrototypeArtifact actual) =>
        PrototypeArtifact.Create(PrototypeArtifactRegistry.MaterializationReceipt, association.Source!, Json(new
        {
            compilerInput = association.Identity.Data.Value, actualSet = actual.Identity.Data.Value,
            producerClaim = new { kind = "local-diagnostic", identity = "developer" },
            captureContext = new { runId = "run-1", capturedAtUtc = "2026-11-06T00:00:00.0000000Z" },
            execution = "completed", coverage = "complete", provenance = new[] { "synthetic" }
        }));
    private static ReadOnlyMemory<byte>[] Buffers(params PrototypeArtifact[] artifacts) =>
        artifacts.Select(a => new ReadOnlyMemory<byte>(a.Data.ToCanonicalBytes())).ToArray();

    [Fact]
    public void Four_registry_domains_are_distinct_and_do_not_reallocate_existing_v1()
    {
        var domains = PrototypeArtifactRegistry.ArtifactTypes.Select(t => PrototypeArtifactRegistry.Domain(t, 1, 1)).ToArray();
        Assert.Equal(4, domains.Distinct().Count());
        Assert.All(domains, d => Assert.StartsWith("coglatas.prototype.", d));
        Assert.DoesNotContain(CompilerInputManifest.Format, domains);
        Assert.Equal(4, domains.Select(d => ContentDigest.Compute(d, "{}"u8).Value).Distinct().Count());
    }

    [Theory]
    [InlineData("coglatas.prototype.dependency-manifest", 2, 1)]
    [InlineData("coglatas.prototype.dependency-manifest", 1, 2)]
    [InlineData("coglatas.dependency-manifest", 1, 1)]
    public void Unallocated_schema_or_encoding_is_rejected(string type, int schema, int encoding) =>
        Assert.Throws<UnsupportedSourceException>(() => PrototypeArtifactRegistry.Domain(type, schema, encoding));

    [Fact]
    public void Hash_framing_matches_independent_sha256_and_rejects_nul_domain()
    {
        const string domain = "coglatas.prototype.dependency-manifest/1/canonical/1";
        var payload = SourceJson.Parse("{\"unicode\":\"é😀\",\"precise\":12345678901234567890.12345678901234567890,\"ordered\":[3,1,2]}");
        const string expectedCanonical = "{\"ordered\":[3,1,2],\"precise\":12345678901234567890.1234567890123456789,\"unicode\":\"é😀\"}";
        Assert.Equal(expectedCanonical, payload.CanonicalText);
        var framed = Encoding.UTF8.GetBytes(domain).Concat(new byte[] { 0 }).Concat(Encoding.UTF8.GetBytes(expectedCanonical)).ToArray();
        var expected = Convert.ToHexStringLower(SHA256.HashData(framed));
        Assert.Equal(expected, ContentDigest.Compute(domain, payload.ToCanonicalBytes()).Value);
        Assert.NotEqual(expected, ContentDigest.Compute(domain, []).Value);
        Assert.NotEqual(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(domain + expectedCanonical))));
        Assert.NotEqual(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(domain + "\n" + expectedCanonical))));
        Assert.Throws<ArgumentException>(() => ContentDigest.Compute("a\0b", payload.ToCanonicalBytes()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(63)]
    [InlineData(65)]
    public void Shortened_or_extended_digest_is_never_an_identity(int length) =>
        Assert.Throws<FormatException>(() => new ArtifactIdentity(PrototypeArtifactRegistry.DependencyManifest, 1, 1, new string('a', length)));

    [Fact]
    public void Identity_compares_type_schema_encoding_and_all_256_bits()
    {
        var identity = new ArtifactIdentity(PrototypeArtifactRegistry.DependencyManifest, 1, 1, new string('a', 64));
        Assert.NotEqual(identity, new ArtifactIdentity(PrototypeArtifactRegistry.MaterializationSet, 1, 1, identity.FullDigest));
        Assert.NotEqual(identity, new ArtifactIdentity(identity.ArtifactType, 2, 1, identity.FullDigest));
        Assert.NotEqual(identity, new ArtifactIdentity(identity.ArtifactType, 1, 2, identity.FullDigest));
        Assert.NotEqual(identity, new ArtifactIdentity(identity.ArtifactType, 1, 1, new string('a', 63) + "b"));
        Assert.Contains(identity.FullDigest, identity.ToString());
    }

    [Fact]
    public void Same_source_content_at_a_different_revision_cannot_substitute_for_the_bound_source()
    {
        var manifest = Manifest();
        var next = CompilerContractFixtures.Input(source: CompilerContractFixtures.Source(7));
        Assert.Throws<FormatException>(() => ArtifactAssembly.Associate(next, manifest));
        Assert.NotEqual(manifest.Identity, Manifest(CompilerContractFixtures.Source(7)).Identity);
    }

    [Fact]
    public void Changed_settings_produce_a_new_association_without_mutating_the_old_input()
    {
        var manifest = Manifest(); var input = CompilerContractFixtures.Input();
        var before = input.Data.CanonicalText;
        var settings = CompilerContractFixtures.Settings(); settings["executionProfile"]!["maximumOperations"] = 123;
        var first = ArtifactAssembly.Associate(input, manifest);
        var second = ArtifactAssembly.Associate(CompilerContractFixtures.Input(settings), manifest);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Equal(before, input.Data.CanonicalText);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("payload")]
    [InlineData("source")]
    public void Unknown_fields_round_trip_change_identity_and_prevent_supported_execution(string boundary)
    {
        var original = Manifest();
        var unknown = Change(original, root =>
        {
            var target = boundary == "root" ? root : root[boundary]!.AsObject();
            target["futureMeaning"] = new JsonArray(3, 2, 1);
        });
        Assert.NotEqual(original.Identity, unknown.Identity);
        Assert.False(unknown.SemanticsSupported);
        Assert.Throws<UnsupportedSourceException>(unknown.RequireSupported);
        var decoded = ArtifactDecodeResult.Decode(unknown.Data.ToCanonicalBytes());
        Assert.Equal(ArtifactDecodeStatus.Unsupported, decoded.Status);
        Assert.Equal(unknown.Data.CanonicalText, decoded.Artifact!.Data.CanonicalText);
        Assert.Equal(unknown.Data.ToCanonicalBytes(), decoded.RecoverOriginalBytes());
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("canonicalEncodingVersion")]
    public void Old_reader_retains_unknown_versions_without_rewriting_them(string field)
    {
        var node = JsonNode.Parse(Manifest().Data.CanonicalText)!; node[field] = 2;
        var bytes = SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes();
        var decoded = ArtifactDecodeResult.Decode(bytes);
        Assert.Equal(ArtifactDecodeStatus.Unsupported, decoded.Status);
        Assert.Null(decoded.Artifact);
        var recovered = decoded.RecoverOriginalBytes(); recovered[0] = 0;
        Assert.Equal(bytes, decoded.RecoverOriginalBytes());
    }

    [Fact]
    public void Duplicate_members_and_noncanonical_bytes_cannot_enter_the_index()
    {
        Assert.Throws<FormatException>(() => SourceJson.Parse("{\"x\":1,\"x\":2}"));
        Assert.Throws<FormatException>(() => PrototypeArtifact.ReadCanonical(Encoding.UTF8.GetBytes(" " + Manifest().Data.CanonicalText)));
        Assert.Throws<FormatException>(() => PrototypeArtifact.ReadCanonical([0xef, 0xbb, 0xbf, .. Manifest().Data.ToCanonicalBytes()]));
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("not-json")]
    [InlineData("invalid-utf8")]
    [InlineData("missing-payload")]
    [InlineData("missing-context")]
    [InlineData("missing-actual")]
    public void Malformed_artifact_buffers_are_invalid_and_recover_the_exact_original_bytes(string input)
    {
        byte[] bytes;
        if (input == "truncated") bytes = "{\"artifactType\":"u8.ToArray();
        else if (input == "not-json") bytes = "not-json"u8.ToArray();
        else if (input == "invalid-utf8") bytes = [0xff, 0xfe];
        else
        {
            var manifest = Manifest(null, Entry());
            var data = input == "missing-actual"
                ? Actual(manifest, "completed", new { entryKey = "a", expected = new ArtifactIdentity("coglatas.prototype.unit", 1, 1, new string('a', 64)).Data.Value,
                    state = "missing", actual = (object?)null }).Data
                : manifest.Data;
            var node = JsonNode.Parse(data.CanonicalText)!.AsObject();
            if (input == "missing-payload") node.Remove("payload");
            else if (input == "missing-context") node["source"]!.AsObject().Remove("context");
            else node["payload"]!["entries"]![0]!.AsObject().Remove("actual");
            bytes = SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes();
        }
        var result = ArtifactDecodeResult.Decode(bytes);
        Assert.Equal(ArtifactDecodeStatus.Invalid, result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(bytes, result.RecoverOriginalBytes());
        var recovered = result.RecoverOriginalBytes(); recovered[0] ^= 1;
        Assert.Equal(bytes, result.RecoverOriginalBytes());
    }

    [Fact]
    public void Entry_keys_and_reference_bindings_are_unique_and_project_scope_is_exact()
    {
        Assert.Throws<FormatException>(() => Manifest(null, Entry(), Entry()));
        Assert.Throws<FormatException>(() => Manifest(null, Entry("b"), Entry()));
        Assert.Throws<FormatException>(() => Manifest(null, Entry(source: CompilerContractFixtures.Source(7))));
        var manifest = Manifest(null, Entry(), Entry("b"));
        Assert.Throws<FormatException>(() => Change(manifest, root => root["payload"]!["entries"]![1]!["sourceBinding"]!["referenceKey"] = "a"));
        Assert.Throws<FormatException>(() => Change(manifest, root => root["payload"]!["entries"]![0]!["target"]!["kind"] = "calendar"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("unsupported")]
    [InlineData("denied")]
    public void Incomplete_materializations_never_become_complete(string state)
    {
        var manifest = Manifest(null, Entry());
        var expected = new ArtifactIdentity("coglatas.prototype.unit", 1, 1, new string('a', 64));
        var actual = Actual(manifest, "completed", new { entryKey = "a", expected = expected.Data.Value, state, actual = (object?)null });
        Assert.False(ArtifactAssembly.MaterializationComplete(ArtifactAssembly.Associate(CompilerContractFixtures.Input(), manifest), manifest, actual));
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("exhausted")]
    [InlineData("invalid")]
    public void Interrupted_empty_enumeration_is_incomplete(string enumeration)
    {
        var manifest = Manifest();
        Assert.False(ArtifactAssembly.MaterializationComplete(ArtifactAssembly.Associate(CompilerContractFixtures.Input(), manifest), manifest, Actual(manifest, enumeration)));
    }

    [Fact]
    public void Present_body_must_match_its_entire_expected_pin()
    {
        var manifest = Manifest(null, Entry());
        var expected = new ArtifactIdentity("coglatas.prototype.unit", 1, 1, new string('a', 64));
        var substituted = new ArtifactIdentity(expected.ArtifactType, 1, 1, new string('b', 64));
        Assert.Throws<FormatException>(() => Actual(manifest, "completed", new { entryKey = "a", expected = expected.Data.Value, state = "present", actual = substituted.Data.Value }));
    }

    [Fact]
    public void Receipt_binds_exact_input_and_actual_set_without_including_its_own_digest()
    {
        var manifest = Manifest(); var association = ArtifactAssembly.Associate(CompilerContractFixtures.Input(), manifest);
        var actual = Actual(manifest); var receipt = Receipt(association, actual);
        Assert.True(ArtifactAssembly.BindReceipt(receipt, association, manifest, actual));
        var cancelled = Actual(manifest, "cancelled");
        Assert.Throws<FormatException>(() => ArtifactAssembly.BindReceipt(receipt, association, manifest, cancelled));
        var nextManifest = Manifest(CompilerContractFixtures.Source(7));
        var nextInput = ArtifactAssembly.Associate(CompilerContractFixtures.Input(source: CompilerContractFixtures.Source(7)), nextManifest);
        Assert.Throws<FormatException>(() => ArtifactAssembly.BindReceipt(receipt, nextInput, nextManifest, Actual(nextManifest)));
        Assert.DoesNotContain(receipt.Identity, association.Dependencies);
        Assert.DoesNotContain(receipt.Identity, manifest.Dependencies);
        Assert.False(Change(receipt, root => root["trusted"] = true).SemanticsSupported);
    }

    [Fact]
    public void Complete_offline_import_is_content_addressed_and_missing_bundle_does_not_publish_a_prefix()
    {
        var source = CompilerContractFixtures.Source(); var manifest = Manifest(source);
        var association = ArtifactAssembly.Associate(CompilerContractFixtures.Input(source: source), manifest);
        var actual = Actual(manifest); var receipt = Receipt(association, actual); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Incomplete, store.Import(receipt.Identity, Buffers(receipt, association, actual), [source]));
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.SourceCount);
        Assert.Equal(ArtifactImportState.Incomplete, store.Import(receipt.Identity, Buffers(receipt, association, actual, manifest), []));
        Assert.Equal(0, store.Count);
        Assert.Equal(ArtifactImportState.Imported, store.Import(receipt.Identity, Buffers(receipt, association, actual, manifest), [source]));
        Assert.Equal(4, store.Count);
        Assert.Equal(source.Data.CanonicalText, store.FindSource(Pin(source))!.Data.CanonicalText);
        Assert.Equal(1, store.SourceCount);
        Assert.Equal(receipt.Identity, store.Find(receipt.Identity)!.Identity);
        Assert.Null(store.Find(new(receipt.Identity.ArtifactType, 1, 1, new string('f', 64))));
    }

    [Fact]
    public void Supplier_failure_or_duplicate_leaves_the_existing_index_unchanged()
    {
        var manifest = Manifest(); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(manifest.Identity, Buffers(manifest), [CompilerContractFixtures.Source()]));
        IEnumerable<ReadOnlyMemory<byte>> Broken()
        {
            yield return manifest.Data.ToCanonicalBytes();
            throw new InvalidOperationException("synthetic interruption");
        }
        Assert.Throws<InvalidOperationException>(() => store.Import(manifest.Identity, Broken(), []));
        Assert.Throws<FormatException>(() => store.Import(manifest.Identity, Buffers(manifest, manifest), []));
        Assert.Equal(1, store.Count);
    }

    [Theory]
    [InlineData("artifactType")]
    [InlineData("schemaVersion")]
    [InlineData("canonicalEncodingVersion")]
    public void Unsupported_offline_headers_return_a_state_without_publishing_or_opening_source_suppliers(string field)
    {
        var source = CompilerContractFixtures.Source(); var retained = Manifest(source); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(retained.Identity, Buffers(retained), [source]));
        var next = Manifest(CompilerContractFixtures.Source(7));
        var node = JsonNode.Parse(next.Data.CanonicalText)!;
        if (field == "artifactType") node[field] = "coglatas.prototype.future";
        else node[field] = 2;
        var futureBytes = SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes();
        var sourceOpened = false; var disposed = false;
        IEnumerable<ReadOnlyMemory<byte>> Entries()
        {
            try { yield return next.Data.ToCanonicalBytes(); yield return futureBytes; }
            finally { disposed = true; }
        }
        IEnumerable<ProjectSource> Sources() { sourceOpened = true; yield return source; }
        Assert.Equal(ArtifactImportState.Unsupported, store.Import(next.Identity, Entries(), Sources()));
        Assert.True(disposed); Assert.False(sourceOpened);
        Assert.Equal(1, store.Count); Assert.Equal(1, store.SourceCount);
        Assert.Equal(retained.Data, store.Find(retained.Identity)!.Data);
        Assert.Null(store.Find(next.Identity));
        Assert.Equal(futureBytes, SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes());
    }

    [Fact]
    public void Exactly_the_artifact_capacity_is_imported_with_its_source()
    {
        var source = CompilerContractFixtures.Source(); var manifest = Manifest(source); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(manifest.Identity, Buffers(manifest), [source], maximumArtifacts: 1));
        Assert.Equal(1, store.Count); Assert.Equal(1, store.SourceCount);
    }

    [Fact]
    public void Exactly_the_source_capacity_is_imported_without_rejecting_terminal_enumeration()
    {
        var source = CompilerContractFixtures.Source(); var manifest = Manifest(source); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(manifest.Identity, Buffers(manifest),
            [source, CompilerContractFixtures.Source(7)], maximumArtifacts: 2));
        Assert.Equal(1, store.Count); Assert.Equal(2, store.SourceCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void One_over_artifact_or_source_capacity_disposes_and_never_publishes_a_prefix(bool exceedArtifacts)
    {
        var source = CompilerContractFixtures.Source(); var manifest = Manifest(source);
        var nextSource = CompilerContractFixtures.Source(7); var nextManifest = Manifest(nextSource);
        var buffersDisposed = false; var sourcesDisposed = false; var sourcesOpened = false;
        IEnumerable<ReadOnlyMemory<byte>> Entries()
        {
            try
            {
                yield return manifest.Data.ToCanonicalBytes();
                if (exceedArtifacts) yield return nextManifest.Data.ToCanonicalBytes();
            }
            finally { buffersDisposed = true; }
        }
        IEnumerable<ProjectSource> Sources()
        {
            sourcesOpened = true;
            try { yield return source; yield return nextSource; }
            finally { sourcesDisposed = true; }
        }
        var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Exhausted, store.Import(manifest.Identity, Entries(), Sources(), maximumArtifacts: 1));
        Assert.True(buffersDisposed); Assert.Equal(!exceedArtifacts, sourcesOpened); Assert.Equal(!exceedArtifacts, sourcesDisposed);
        Assert.Equal(0, store.Count); Assert.Equal(0, store.SourceCount);
    }

    [Theory]
    [InlineData("present")]
    [InlineData("missing")]
    public void Nonempty_offline_chains_remain_incomplete_without_registered_external_bodies(string state)
    {
        var source = CompilerContractFixtures.Source(); var manifest = Manifest(source, Entry());
        var association = ArtifactAssembly.Associate(CompilerContractFixtures.Input(source: source), manifest);
        var expected = new ArtifactIdentity("coglatas.prototype.unit", 1, 1, new string('a', 64));
        var actual = Actual(manifest, "completed", new { entryKey = "a", expected = expected.Data.Value, state,
            actual = state == "present" ? expected.Data.Value : (object?)null });
        var receipt = Receipt(association, actual); var store = new PrototypeArtifactStore();
        Assert.Equal(state == "present", ArtifactAssembly.MaterializationComplete(association, manifest, actual));
        Assert.Equal(ArtifactImportState.Incomplete, store.Import(receipt.Identity, Buffers(manifest, association, actual, receipt), [source]));
        Assert.Equal(0, store.Count); Assert.Equal(0, store.SourceCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancellation_during_successful_or_terminal_MoveNext_disposes_without_publication(bool yieldItem)
    {
        var manifest = Manifest(); using var cancellation = new CancellationTokenSource(); var disposed = false;
        IEnumerable<ReadOnlyMemory<byte>> Interrupted(Action cancel)
        {
            try
            {
                cancel();
                if (yieldItem) yield return manifest.Data.ToCanonicalBytes();
            }
            finally { disposed = true; }
        }
        var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Cancelled, store.Import(manifest.Identity, Interrupted(cancellation.Cancel), [], cancellation.Token));
        Assert.True(disposed); Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Exhausted_budget_does_not_open_the_next_supplier_or_publish_partial_state()
    {
        var manifest = Manifest(); var opened = false;
        IEnumerable<ProjectSource> Sources() { opened = true; yield return CompilerContractFixtures.Source(); }
        var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Exhausted, store.Import(manifest.Identity, Buffers(manifest), Sources(), maximumBytes: 1));
        Assert.False(opened); Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Tampered_legacy_input_digest_cannot_be_imported_even_with_a_valid_artifact_hash()
    {
        var manifest = Manifest(); var association = ArtifactAssembly.Associate(CompilerContractFixtures.Input(), manifest);
        var tampered = Change(association, root => root["payload"]!["legacyInputDigest"]!["value"] = new string('b', 64));
        var store = new PrototypeArtifactStore();
        Assert.Throws<FormatException>(() => store.Import(tampered.Identity, Buffers(manifest, tampered), [CompilerContractFixtures.Source()]));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Concurrent_offline_imports_preserve_both_immutable_source_contexts()
    {
        var firstSource = CompilerContractFixtures.Source(); var secondSource = CompilerContractFixtures.Source(7);
        var first = Manifest(firstSource); var second = Manifest(secondSource); var store = new PrototypeArtifactStore();
        var outcomes = await Task.WhenAll(
            Task.Run(() => store.Import(first.Identity, Buffers(first), [firstSource])),
            Task.Run(() => store.Import(second.Identity, Buffers(second), [secondSource])));
        Assert.All(outcomes, outcome => Assert.Equal(ArtifactImportState.Imported, outcome));
        Assert.Equal(2, store.Count); Assert.Equal(2, store.SourceCount);
        Assert.Equal(firstSource.Data.CanonicalText, store.FindSource(Pin(firstSource))!.Data.CanonicalText);
        Assert.Equal(secondSource.Data.CanonicalText, store.FindSource(Pin(secondSource))!.Data.CanonicalText);
    }
    [Fact]
    public void Independent_reader_compat_vectors_preserve_unicode_scalar_sequences_and_array_order()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "artifact-reader-compat-vectors-v1.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("PROTOTYPE_ONLY", fixture.RootElement.GetProperty("status").GetString());
        var domain = fixture.RootElement.GetProperty("domain").GetString()!;
        Assert.Equal(PrototypeArtifactRegistry.Domain(PrototypeArtifactRegistry.DependencyManifest, 1, 1), domain);
        var vectors = fixture.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Equal(4, vectors.Length);
        var computed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var vector in vectors)
        {
            var id = vector.GetProperty("id").GetString()!;
            var canonical = vector.GetProperty("canonical").GetString()!;
            var data = SourceJson.Parse(canonical);
            Assert.Equal(canonical, data.CanonicalText);
            var digest = ContentDigest.Compute(domain, data.ToCanonicalBytes()).Value;
            Assert.Equal(vector.GetProperty("sha256").GetString(), digest);
            Assert.True(computed.TryAdd(id, digest));
            Assert.NotEqual(digest, ContentDigest.Compute(domain, Encoding.UTF8.GetBytes(canonical + "\\n")).Value);
        }
        Assert.Equal(4, computed.Count);
        foreach (var pair in fixture.RootElement.GetProperty("casePairs").EnumerateArray())
            Assert.NotEqual(computed[pair[0].GetString()!], computed[pair[1].GetString()!]);
    }

    [Fact]
    public void Unallocated_formal_type_name_is_opaque_and_cannot_be_relabelled_into_existing_prototype_reader()
    {
        var manifest = Manifest();
        var node = JsonNode.Parse(manifest.Data.CanonicalText)!.AsObject();
        node["artifactType"] = "coglatas.dependency-manifest";
        var original = SourceJson.Parse(node.ToJsonString()).ToCanonicalBytes();
        var result = ArtifactDecodeResult.Decode(original);
        Assert.Equal(ArtifactDecodeStatus.Unsupported, result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(original, result.RecoverOriginalBytes());
        var copy = result.RecoverOriginalBytes();
        copy[0] ^= 1;
        Assert.Equal(original, result.RecoverOriginalBytes());
        Assert.NotEqual(original, manifest.Data.ToCanonicalBytes());
    }

}
