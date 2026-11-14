using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;
public sealed class SyntheticSnapshotMaterializationTests
{
    [Theory]
    [InlineData("available")] [InlineData("missing")] [InlineData("denied")]
    public void OriginalSnapshotBytesOrOpaqueDenialReproduceActualCoreBindings(string admission)
    {
        var f = ScenarioReferenceRebaseTests.Fixture();
        CompilerMaterializedSnapshot[] supplied = admission == "missing" ? [] : [new(f.Snapshot.SnapshotId, f.Snapshot.Version,
            new(f.Snapshot.Snapshot.Source, admission == "available", true, true))];
        var data = SyntheticSnapshotMaterialization.Export(f.Input, supplied); var replay = SyntheticSnapshotMaterialization.Read(f.Input, data);
        Assert.Equal(supplied.Length, replay.Count);
        Assert.Equal(PureCoreCompiler.Compile(f.Input, f.Input.Compiler, supplied).Result.Data, PureCoreCompiler.Compile(f.Input, f.Input.Compiler, replay).Result.Data);
        var before = ProjectSemanticBinder.Bind(f.Input, supplied); var after = ProjectSemanticBinder.Bind(f.Input, replay);
        Assert.Equal(new CompilerSemanticInspection(before).Data, new CompilerSemanticInspection(after).Data);
        Assert.Equal(admission == "available", after.BindingComplete);
        if (admission == "available") Assert.Equal(f.Snapshot.Snapshot.Source.Data, replay[0].Snapshot.Source.Data);
        if (admission == "denied") { Assert.False(data.Value.GetProperty("entries")[0].TryGetProperty("source", out _)); Assert.False(replay[0].Snapshot.IsAuthorized); }
    }
    [Theory]
    [InlineData("input")] [InlineData("source")] [InlineData("digest")] [InlineData("version")] [InlineData("context")]
    [InlineData("duplicate")] [InlineData("extra")] [InlineData("future")] [InlineData("unavailable-body")] [InlineData("count")]
    public void AlteredSyntheticMaterializationCannotReplayAnAdmittedSnapshot(string mode)
    {
        var f = ScenarioReferenceRebaseTests.Fixture(); var data = SyntheticSnapshotMaterialization.Export(f.Input, [f.Snapshot]);
        var root = JsonNode.Parse(data.CanonicalText)!.AsObject(); var entries = root["entries"]!.AsArray(); var entry = entries[0]!;
        if (mode == "input") root["inputDigest"]!["value"] = new string('a', 64);
        if (mode == "source") entry["source"]!["text"] = "{}";
        if (mode == "digest") entry["source"]!["digest"]!["value"] = new string('a', 64);
        if (mode == "version") entry["version"] = "2";
        if (mode == "context") entry["context"]!["revisionId"] = Id(999);
        if (mode == "duplicate") entries.Add(entry.DeepClone());
        if (mode == "extra") entry["currentAuthority"] = true;
        if (mode == "future") entry["admission"] = "Trusted";
        if (mode == "unavailable-body") entry["admission"] = "Unavailable";
        if (mode == "count") while (entries.Count < 33) entries.Add(entry.DeepClone());
        Assert.Throws<FormatException>(() => SyntheticSnapshotMaterialization.Read(f.Input, Json(root)));
    }
    [Fact]
    public void ExportStopsBoundedEnumerationAndDoesNotLeakADeniedBody()
    {
        var f = ScenarioReferenceRebaseTests.Fixture(); var moves = 0;
        IEnumerable<CompilerMaterializedSnapshot> Many() { while (true) { moves++; yield return f.Snapshot; } }
        Assert.Throws<FormatException>(() => SyntheticSnapshotMaterialization.Export(f.Input, Many())); Assert.Equal(33, moves);
        var denied = new CompilerMaterializedSnapshot(f.Snapshot.SnapshotId, f.Snapshot.Version, new(f.Snapshot.Snapshot.Source, false, false, false));
        var exported = SyntheticSnapshotMaterialization.Export(f.Input, [denied]);
        Assert.DoesNotContain("declarationsComplete", exported.CanonicalText); Assert.DoesNotContain("logicalName", exported.CanonicalText);
    }
    [Fact]
    public void NoncanonicalOriginalSnapshotBytesCannotBorrowTheCanonicalSourceDigest()
    {
        var f = ScenarioReferenceRebaseTests.Fixture(); var data = SyntheticSnapshotMaterialization.Export(f.Input, [f.Snapshot]);
        var node = JsonNode.Parse(data.CanonicalText)!.AsObject(); var source = node["entries"]![0]!["source"]!;
        source["text"] = source["text"]!.GetValue<string>() + " ";
        Assert.Throws<FormatException>(() => SyntheticSnapshotMaterialization.Read(f.Input, Json(node)));
    }

}
