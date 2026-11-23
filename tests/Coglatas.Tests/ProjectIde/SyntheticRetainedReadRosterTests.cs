using System.Collections;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticRetainedReadRosterTests
{
    [Theory]
    [InlineData("replace")] [InlineData("clear")] [InlineData("append")] [InlineData("remove")]
    public async Task RetainedLegacyReferenceSearchCannotBeRewrittenThroughSynchronizationStorage(string action)
    {
        var input = CompilerAnalysisFixtures.Constraint(false);
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"),
            Artifact("ir"), Artifact("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var request = new CompilerHostRequest(new(input), 1);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.True(view.TryAccept(response));
        var inspection = view.Inspection!;
        var actual = Assert.Single(inspection.References, item => item.Origin.PropertyPath == "/target");
        var symbol = Assert.Single(inspection.Symbols, item => item.Symbol.Key == actual.SymbolKey);
        Assert.True(view.Select(symbol.Symbol.Location));
        var search = Assert.IsType<SyntheticWorkbenchViewModel.ReferenceSearchResult>(view.FindReferences());
        Assert.Same(actual, Assert.Single(search.References)); Assert.False(search.Truncated);
        var source = input.Source.Data; var result = view.Result; var data = inspection.Data; var digest = inspection.Digest;
        var coverage = search.CoverageComplete;
        var foreignOrigin = ProjectLocation.Create(actual.Origin.DocumentId, Source(99).Context,
            actual.Origin.PropertyPath, entityId: actual.Origin.EntityId, relationId: actual.Origin.RelationId);
        var replacement = new CompilerBoundReference(foreignOrigin, actual.State, actual.SymbolKey);
        Assert.NotEqual(actual.Origin.Context, replacement.Origin.Context);
        AttemptStorageRewrite(search.References, replacement, action);
        Assert.Same(source, input.Source.Data); Assert.Same(result, view.Result);
        Assert.Same(data, inspection.Data); Assert.Same(digest, inspection.Digest);
        Assert.Same(actual, Assert.Single(inspection.FindReferences(symbol.Symbol.Location)));
        Assert.Equal(coverage, search.CoverageComplete); Assert.False(search.Truncated);
        Assert.Same(actual, Assert.Single(search.References));
        Assert.True(view.SelectReference(actual)); Assert.True(view.NavigateReferenceOrigin());
        Assert.Equal(actual.Origin, view.SourceTarget);
    }

    [Theory]
    [InlineData("replace")] [InlineData("clear")] [InlineData("append")] [InlineData("remove")]
    public void RetainedSnapshotReadCannotDisagreeWithItsOriginalAdmittedTransport(string action)
    {
        var fixture = ScenarioReferenceRebaseTests.Fixture();
        var transport = SyntheticSnapshotMaterialization.Export(fixture.Input, [fixture.Snapshot]);
        var snapshots = SyntheticSnapshotMaterialization.Read(fixture.Input, transport);
        var actual = Assert.Single(snapshots);
        Assert.True(actual.Snapshot.IsAuthorized); Assert.Equal(fixture.Snapshot.Snapshot.Source.Data, actual.Snapshot.Source.Data);
        var full = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, [fixture.Snapshot]);
        Assert.Equal(full.Result.Data, PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, snapshots).Result.Data);
        var source = fixture.Input.Source.Data; var originalBytes = transport.ToCanonicalBytes();
        var replacement = new CompilerMaterializedSnapshot(actual.SnapshotId, actual.Version,
            actual.Snapshot with { IsAuthorized = false });
        AttemptStorageRewrite(snapshots, replacement, action);
        Assert.Same(source, fixture.Input.Source.Data); Assert.Equal(originalBytes, transport.ToCanonicalBytes());
        var reread = SyntheticSnapshotMaterialization.Read(fixture.Input, transport);
        Assert.Equal(actual.Snapshot.Source.Data, Assert.Single(reread).Snapshot.Source.Data);
        Assert.True(reread[0].Snapshot.IsAuthorized);
        Assert.Equal(full.Result.Data, PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, reread).Result.Data);
        Assert.Same(actual, Assert.Single(snapshots));
        Assert.Equal(full.Result.Data, PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, snapshots).Result.Data);
    }

    private static void AttemptStorageRewrite<T>(IReadOnlyList<T> values, T replacement, string action) where T : class
    {
        var original = Assert.Single(values);
        var generic = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(generic.IsReadOnly); Assert.True(generic.Contains(original)); Assert.Equal(0, generic.IndexOf(original));
        Assert.Throws<NotSupportedException>(() => generic[0] = replacement);
        Assert.Throws<NotSupportedException>(() => generic.Clear());
        Assert.Throws<NotSupportedException>(() => generic.Add(replacement));
        Assert.Throws<NotSupportedException>(() => generic.Insert(0, replacement));
        Assert.Throws<NotSupportedException>(() => generic.Remove(original));
        Assert.Throws<NotSupportedException>(() => generic.RemoveAt(0));
        var copy = new T[1]; generic.CopyTo(copy, 0); Assert.Same(original, copy[0]);
        copy[0] = replacement; Assert.Same(original, values[0]);
        Assert.Equal(values, values.ToArray());
        if (values is not ICollection legacy || legacy.SyncRoot is not IList<T> storage) return;
        switch (action)
        {
            case "replace": storage[0] = replacement; break;
            case "clear": storage.Clear(); break;
            case "append": storage.Add(replacement); break;
            case "remove": storage.RemoveAt(0); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}
