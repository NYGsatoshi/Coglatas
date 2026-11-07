using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde.Entry;

public sealed class EntryContractTests
{
    private static SourceJson Data(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static PrototypeArtifact Manifest(ProjectSource source) => PrototypeArtifact.Create(
        PrototypeArtifactRegistry.DependencyManifest, ArtifactSourcePin.FromSource(source), Data(new { entries = Array.Empty<object>() }));
    private static PrototypeArtifact Actual(PrototypeArtifact manifest, string state = "completed") => PrototypeArtifact.Create(
        PrototypeArtifactRegistry.MaterializationSet, manifest.Source!, Data(new
        { dependencyManifest = manifest.Identity.Data.Value, entries = Array.Empty<object>(), enumeration = state }));
    private static PrototypeArtifact Receipt(PrototypeArtifact association, PrototypeArtifact actual,
        string execution = "completed", string coverage = "complete", string producer = "local-diagnostic") => PrototypeArtifact.Create(
        PrototypeArtifactRegistry.MaterializationReceipt, association.Source!, Data(new
        {
            compilerInput = association.Identity.Data.Value, actualSet = actual.Identity.Data.Value,
            producerClaim = new { kind = producer, identity = "synthetic-producer" },
            captureContext = new { runId = "run-one", capturedAtUtc = "2026-11-07T00:00:00.0000000Z" },
            execution, coverage, provenance = new[] { "synthetic-origin" }
        }));
    // Independent framing writer: no Product encoder or framing helper is used.
    private static byte[] Frame(params (byte Kind, byte[] Body)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write("CGPC0001"u8);
        var number = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)entries.Length); stream.Write(number);
        foreach (var (kind, body) in entries)
        {
            stream.WriteByte(kind); BinaryPrimitives.WriteUInt32BigEndian(number, (uint)body.Length);
            stream.Write(number); stream.Write(body);
        }
        return stream.ToArray();
    }
    private static ReadOnlyMemory<byte>[] Bodies(params PrototypeArtifact[] artifacts) => artifacts.Select(a => new ReadOnlyMemory<byte>(a.Data.ToCanonicalBytes())).ToArray();

    [Theory]
    [InlineData("digest")]
    [InlineData("type")]
    [InlineData("schema")]
    [InlineData("encoding")]
    public void Reader_rejects_expected_identity_substitution_without_publishing_a_body(string dimension)
    {
        var manifest = Manifest(Source()); var identity = manifest.Identity; var bytes = manifest.Data.ToCanonicalBytes();
        var expected = new ArtifactIdentity(dimension == "type" ? PrototypeArtifactRegistry.MaterializationSet : identity.ArtifactType,
            dimension == "schema" ? 2 : 1, dimension == "encoding" ? 2 : 1,
            dimension == "digest" ? new string('f', 64) : identity.FullDigest);
        var result = ArtifactDecodeResult.Decode(bytes, expected);
        Assert.Equal(ArtifactDecodeStatus.Invalid, result.Status); Assert.Null(result.Artifact);
        Assert.Equal(bytes, result.RecoverOriginalBytes());
        Assert.Equal(ArtifactDecodeStatus.Decoded, ArtifactDecodeResult.Decode(bytes, identity).Status);
    }

    [Fact]
    public void Cancelled_readers_return_no_partial_object_and_recover_defensive_originals()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var artifact = Manifest(Source()); var bytes = artifact.Data.ToCanonicalBytes();
        var result = ArtifactDecodeResult.Decode(bytes, artifact.Identity, cancel.Token);
        Assert.Equal(ArtifactDecodeStatus.Cancelled, result.Status); Assert.Null(result.Artifact);
        var recovered = result.RecoverOriginalBytes(); recovered[0] = 0;
        Assert.Equal(bytes, result.RecoverOriginalBytes());
        var sourceBytes = ProjectSourceCodec.Encode(Source());
        var source = ProjectSourceCodec.Decode(sourceBytes, cancellationToken: cancel.Token);
        Assert.Equal(SourceDecodeStatus.Cancelled, source.Status); Assert.Null(source.Source);
        Assert.Equal(sourceBytes, source.RecoverOriginalBytes());
    }

    [Fact]
    public void Container_preserves_noncanonical_legacy_source_bytes_origin_and_the_previous_index()
    {
        var source = Source(); var manifest = Manifest(source); var store = new PrototypeArtifactStore();
        var rawSource = Encoding.UTF8.GetBytes(" \n" + source.Data.CanonicalText + "\n");
        var bytes = Frame((1, manifest.Data.ToCanonicalBytes()), (2, rawSource));
        var original = (byte[])bytes.Clone();
        Assert.Equal(ArtifactImportState.Imported, store.ImportContainer(manifest.Identity, "synthetic-origin", bytes));
        bytes[0] = 0;
        var retained = Assert.IsType<PrototypeOfflineContainer>(store.FindContainer("synthetic-origin"));
        Assert.Equal("synthetic-origin", retained.Origin); Assert.Equal(manifest.Identity, retained.Root);
        Assert.Equal(original, retained.RecoverOriginalBytes());
        Assert.Equal(rawSource, retained.RecoverSourceBytes(ArtifactSourcePin.FromSource(source)));
        var recovery = retained.RecoverOriginalBytes(); recovery[0] = 0;
        Assert.Equal(original, retained.RecoverOriginalBytes());
        Assert.Equal(source.Digest, store.FindSource(ArtifactSourcePin.FromSource(source))!.Digest);
        Assert.Equal(ArtifactImportState.Imported, store.ImportContainer(manifest.Identity, "synthetic-origin", original));
        Assert.Equal(1, store.Count); Assert.Equal(1, store.SourceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(7)] [InlineData(11)] [InlineData(12)] [InlineData(16)] [InlineData(-1)]
    public void Every_truncated_container_boundary_rejects_without_publication(int cut)
    {
        var source = Source(); var old = Manifest(source); var next = Manifest(Source(7)); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(old.Identity, Bodies(old), [source]));
        var bytes = Frame((1, next.Data.ToCanonicalBytes()), (2, ProjectSourceCodec.Encode(Source(7))));
        var truncated = bytes[..(cut < 0 ? bytes.Length - 1 : cut)];
        Assert.Throws<FormatException>(() => store.ImportContainer(next.Identity, "truncated", truncated));
        Assert.Equal(1, store.Count); Assert.Equal(old.Identity, store.Find(old.Identity)!.Identity);
        Assert.Null(store.Find(next.Identity)); Assert.Null(store.FindContainer("truncated"));
    }

    [Theory]
    [InlineData("artifact-duplicate")] [InlineData("source-duplicate")] [InlineData("source-conflict")]
    [InlineData("trailing")] [InlineData("length-overflow")] [InlineData("invalid-body")]
    public void Duplicate_conflicting_or_malformed_frames_never_publish(string fault)
    {
        var source = Source(); var manifest = Manifest(source); var artifact = manifest.Data.ToCanonicalBytes(); var body = ProjectSourceCodec.Encode(source);
        var frames = new List<(byte, byte[])> { (1, artifact), (2, body) };
        if (fault == "artifact-duplicate") frames.Add((1, artifact));
        if (fault.StartsWith("source-", StringComparison.Ordinal)) frames.Add((2, fault == "source-duplicate" ? body : Encoding.UTF8.GetBytes(" " + source.Data.CanonicalText)));
        if (fault == "invalid-body") frames[0] = (1, "{}"u8.ToArray());
        var bytes = Frame(frames.ToArray());
        if (fault == "trailing") bytes = [.. bytes, 0];
        if (fault == "length-overflow") BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(13, 4), uint.MaxValue);
        var store = new PrototypeArtifactStore();
        Assert.Throws<FormatException>(() => store.ImportContainer(manifest.Identity, "bad", bytes));
        Assert.Equal(0, store.Count); Assert.Equal(0, store.SourceCount); Assert.Null(store.FindContainer("bad"));
    }

    [Theory]
    [InlineData("unsupported")] [InlineData("missing")] [InlineData("exhausted")] [InlineData("cancelled")]
    public void Nonimport_states_remain_distinct_and_preserve_existing_content(string state)
    {
        var source = Source(); var manifest = Manifest(source); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.Import(manifest.Identity, Bodies(manifest), [source]));
        var next = Manifest(Source(7)); var bytes = Frame((1, next.Data.ToCanonicalBytes()), (2, ProjectSourceCodec.Encode(Source(7))));
        if (state == "unsupported") bytes[7] = (byte)'2';
        if (state == "missing") bytes = Frame((2, ProjectSourceCodec.Encode(Source(7))));
        using var cancel = new CancellationTokenSource(); if (state == "cancelled") cancel.Cancel();
        var expected = state switch { "unsupported" => ArtifactImportState.Unsupported, "missing" => ArtifactImportState.Incomplete,
            "exhausted" => ArtifactImportState.Exhausted, _ => ArtifactImportState.Cancelled };
        Assert.Equal(expected, store.ImportContainer(next.Identity, "failed", bytes, cancel.Token, maximumBytes: state == "exhausted" ? bytes.Length - 1 : bytes.Length));
        Assert.Equal(1, store.Count); Assert.Equal(1, store.SourceCount); Assert.Null(store.Find(next.Identity)); Assert.Null(store.FindContainer("failed"));
    }

    [Fact]
    public void Origin_conflict_and_exact_processing_limits_fail_closed()
    {
        var source = Source(); var manifest = Manifest(source); var bytes = Frame((1, manifest.Data.ToCanonicalBytes()), (2, ProjectSourceCodec.Encode(source)));
        var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Imported, store.ImportContainer(manifest.Identity, "one", bytes, maximumArtifacts: 1, maximumBytes: bytes.Length));
        var next = Manifest(Source(7)); var other = Frame((1, next.Data.ToCanonicalBytes()), (2, ProjectSourceCodec.Encode(Source(7))));
        Assert.Throws<FormatException>(() => store.ImportContainer(next.Identity, "one", other));
        var overflow = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32BigEndian(overflow.AsSpan(8, 4), uint.MaxValue);
        Assert.Equal(ArtifactImportState.Exhausted, store.ImportContainer(manifest.Identity, "overflow", overflow, maximumArtifacts: 1));
        Assert.Equal(bytes, store.FindContainer("one")!.RecoverOriginalBytes()); Assert.Null(store.Find(next.Identity));
    }

    [Theory]
    [InlineData("cancelled")] [InlineData("exhausted")] [InlineData("invalid")]
    public void Empty_interrupted_materialization_cannot_publish_an_imported_receipt(string enumeration)
    {
        var input = Input(); var manifest = Manifest(input.Source); var association = ArtifactAssembly.Associate(input, manifest);
        var actual = Actual(manifest, enumeration); var receipt = Receipt(association, actual); var store = new PrototypeArtifactStore();
        Assert.Equal(ArtifactImportState.Incomplete, store.Import(actual.Identity, Bodies(manifest, actual), [input.Source]));
        Assert.Equal(ArtifactImportState.Incomplete, store.Import(receipt.Identity, Bodies(manifest, association, actual, receipt), [input.Source]));
        Assert.Equal(0, store.Count); Assert.Equal(0, store.SourceCount);
        Assert.Equal(ArtifactImportState.Incomplete, store.ImportContainer(receipt.Identity, "interrupted",
            Frame((1, manifest.Data.ToCanonicalBytes()), (1, association.Data.ToCanonicalBytes()),
                (1, actual.Data.ToCanonicalBytes()), (1, receipt.Data.ToCanonicalBytes()), (2, ProjectSourceCodec.Encode(input.Source)))));
        Assert.Null(store.FindContainer("interrupted"));
    }

    [Theory]
    [InlineData("completed", "complete", "trusted-ci-candidate")]
    [InlineData("failed", "complete", "backend-worker-candidate")]
    [InlineData("cancelled", "partial", "unknown")]
    public void Receipt_axes_and_producer_claims_cannot_create_a_verdict_or_proof(string execution, string coverage, string producer)
    {
        var input = Input(); var manifest = Manifest(input.Source); var association = ArtifactAssembly.Associate(input, manifest);
        var actual = Actual(manifest); var receipt = Receipt(association, actual, execution, coverage, producer);
        var assessment = PrototypeReceiptAssessment.Assess(receipt, input, association, manifest, actual, manifest.Source, "run-one");
        Assert.True(assessment.MaterializationComplete); Assert.Equal(execution, assessment.ExecutionClaim);
        Assert.Equal(coverage, assessment.CoverageClaim); Assert.Equal(producer, assessment.ProducerClaim.Value.GetProperty("kind").GetString());
        Assert.Equal("run-one", assessment.RunId); Assert.Equal("synthetic-origin", Assert.Single(assessment.Provenance));
        Assert.Equal(CompilerFreshness.Current, assessment.Freshness);
        Assert.Equal(CompilerVerdict.Inconclusive, assessment.Verdict); Assert.Equal(CompilerEvidenceKind.Missing, assessment.EvidenceKind);
        Assert.Equal(CompilerFreshness.Stale, PrototypeReceiptAssessment.Assess(receipt, input, association, manifest, actual, manifest.Source, "other-run").Freshness);
        Assert.Equal(CompilerFreshness.Stale, PrototypeReceiptAssessment.Assess(receipt, input, association, manifest, actual, ArtifactSourcePin.FromSource(Source(7)), "run-one").Freshness);
        Assert.Equal(CompilerFreshness.Undetermined, PrototypeReceiptAssessment.Assess(receipt, input, association, manifest, actual).Freshness);
        Assert.Throws<FormatException>(() => PrototypeReceiptAssessment.Assess(receipt, Input(source: Source(7)), association, manifest, actual));
    }
}
