using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerMaterializationBindingTests
{
    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot Body) Fixture()
    {
        var source = Source();
        var context = SourceRevisionContext.Committed(new(new(source.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var body = ProjectSource.Create(context, source.Documents);
        var settings = Settings();
        settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(30), version = "1", digest = body.Digest.Data.Value,
            context = body.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        return (Input(settings), new(ExternalSnapshotId.Parse(Id(30)), "1", new(body, true, true, true)));
    }

    [Fact]
    public void DuplicateBodiesAreInvalidEvenWithoutAReference()
    {
        var f = Fixture(); var model = ProjectSemanticBinder.Bind(f.Input, [f.Body, f.Body]);
        Assert.False(model.BindingComplete);
        Assert.Contains(model.Findings, x => x.Diagnostic.Code == "coglatas.binding.snapshot-duplicate");
        Assert.Null(CompilerSemanticValidation.Validate(model).Ir);
    }

    [Fact]
    public void UnauthorizedBodiesAreNotReturnedOrInspectedForPinIntegrity()
    {
        var f = Fixture();
        var denied = new CompilerMaterializedSnapshot(f.Body.SnapshotId, "private-version", f.Body.Snapshot with { IsAuthorized = false });
        var model = ProjectSemanticBinder.Bind(f.Input, [denied]);
        Assert.False(model.BindingComplete); Assert.Empty(model.BoundSnapshots);
        Assert.DoesNotContain(model.Symbols, x => x.Context.Equals(denied.Snapshot.Source.Context));
        Assert.Contains(model.Findings, x => x.Diagnostic.Code == "coglatas.binding.snapshot-unavailable");
        Assert.DoesNotContain(model.Findings, x => x.Diagnostic.Code == "coglatas.binding.snapshot-pin-mismatch");
        Assert.All(model.Findings, x => Assert.Equal(f.Input.Source.Context, x.Diagnostic.Location.Context));
    }

    [Fact]
    public void AuthorizationDenialHasTheSamePublicFindingsForDifferentPrivateBodies()
    {
        var f = Fixture(); var denied = new CompilerMaterializedSnapshot(f.Body.SnapshotId, "1", f.Body.Snapshot with { IsAuthorized = false });
        var different = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(99)), "hidden", new(Source(98), false, false, false));
        var a = ProjectSemanticBinder.Bind(f.Input, [denied]); var b = ProjectSemanticBinder.Bind(f.Input, [different]);
        Assert.Equal(a.Findings.Select(x => x.Diagnostic.Data.CanonicalText), b.Findings.Select(x => x.Diagnostic.Data.CanonicalText));
        Assert.Empty(a.BoundSnapshots); Assert.Empty(b.BoundSnapshots);
    }

    [Fact]
    public void CancellationDuringSupplyStopsBeforeRequestingTheNextBodyAndDisposesTheEnumerator()
    {
        var f = Fixture(); using var cancellation = new CancellationTokenSource();
        var supply = new ObservedSupply(() => cancellation.Cancel(), f.Body);
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(f.Input, supply, cancellation.Token));
        Assert.Equal(1, supply.MoveNextCalls); Assert.True(supply.Disposed);
    }

    [Fact]
    public void SharedBudgetStopsSupplyBeforeReadingTheWholeBoundedBatch()
    {
        var f = Fixture(); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject();
        var budget = ProjectSemanticBinder.Bind(f.Input).LogicalOperationsConsumed + 100;
        settings["executionProfile"]!["maximumOperations"] = budget;
        var supply = new ObservedSupply(() => { }, f.Body, 33);
        var input = Input(settings); var model = ProjectSemanticBinder.Bind(input, supply);
        Assert.False(model.BindingComplete); Assert.True(supply.MoveNextCalls < 33); Assert.True(supply.Disposed);
        Assert.Contains(model.Findings, x => x.Diagnostic.Code.Contains("budget", StringComparison.Ordinal));
        Assert.Empty(model.Symbols); Assert.Empty(model.BoundSnapshots);
        Assert.InRange(model.LogicalOperationsConsumed, 1, budget);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("body")]
    public void UnknownMaterializationMeaningIsPreservedButNeverAccepted(string change)
    {
        var f = Fixture(); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); var body = f.Body.Snapshot.Source;
        if (change == "pin")
        {
            settings["authorizedExternalSnapshots"]![0]!["example.future"] = true;
            Assert.Throws<UnsupportedSourceException>(() => Input(settings));
            return; // The existing immutable input constructor owns this boundary.
        }
        else
        {
            var data = JsonNode.Parse(body.Data.CanonicalText)!.AsObject(); data["example.future"] = Obj(new { secret = "opaque" });
            body = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString())).Source!;
            settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(body.Digest.Data.CanonicalText);
        }
        var input = Input(settings); var supplied = new CompilerMaterializedSnapshot(f.Body.SnapshotId, "1", new(body, true, true, true));
        var model = ProjectSemanticBinder.Bind(input, [supplied]);
        Assert.False(model.BindingComplete);
        Assert.Contains(model.Findings, x => x.State == RelationFindingState.Unsupported && x.Diagnostic.Code == "coglatas.binding.snapshot-unsupported");
        Assert.Null(CompilerSemanticValidation.Validate(model).Ir);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("unknown")]
    public void ExactDeclaredPinsRejectSubstitutedMaterializations(string change)
    {
        var f = Fixture(); var original = f.Body.Snapshot.Source;
        var substituted = change == "source" ? ProjectSource.Create(original.Context, [SourceDocument.Create(DocumentId.Parse(Id(4)), "substitute", original.Documents[0].Root.Data)])
            : change == "revision" ? ProjectSource.Create(Source(99).Context, original.Documents) : original;
        var body = new CompilerMaterializedSnapshot(change == "unknown" ? ExternalSnapshotId.Parse(Id(99)) : f.Body.SnapshotId,
            change == "version" ? "2" : "1", new(substituted, true, true, true));
        var model = ProjectSemanticBinder.Bind(f.Input, [body]);
        Assert.False(model.BindingComplete); Assert.Empty(model.BoundSnapshots);
        Assert.Contains(model.Findings, x => x.Diagnostic.Code == "coglatas.binding.snapshot-pin-mismatch");
        Assert.Null(CompilerSemanticValidation.Validate(model).Ir);
    }

    [Fact]
    public void ConflictingSupplyCannotChooseTheFirstBodyAndDiagnosticsAreOrderIndependent()
    {
        var f = Fixture(); var wrong = new CompilerMaterializedSnapshot(f.Body.SnapshotId, "2", f.Body.Snapshot);
        var a = ProjectSemanticBinder.Bind(f.Input, [f.Body, wrong]); var b = ProjectSemanticBinder.Bind(f.Input, [wrong, f.Body]);
        Assert.False(a.BindingComplete); Assert.False(b.BindingComplete);
        Assert.Contains(a.Findings, x => x.Diagnostic.Code == "coglatas.binding.snapshot-conflict");
        Assert.Equal(a.Findings.Select(x => x.Diagnostic.Data.CanonicalText), b.Findings.Select(x => x.Diagnostic.Data.CanonicalText));
    }

    [Fact]
    public void ExactBodyAdmitsSymbolsWithoutChangingInputIdentityOrProvingExternalClosure()
    {
        var f = Fixture(); var before = f.Input.ToEnvelope(); var model = ProjectSemanticBinder.Bind(f.Input, [f.Body]);
        Assert.True(model.BindingComplete); Assert.Single(model.BoundSnapshots);
        Assert.Equal(before, f.Input.ToEnvelope()); Assert.Equal(f.Body.Snapshot.Source.Digest, model.BoundSnapshots[0].Source.Digest);
        // There are no external references here; admission alone is not an external lowering claim.
        Assert.Empty(model.References);
    }

    private sealed class ObservedSupply(Action beforeFirst, CompilerMaterializedSnapshot body, int count = 2) : IEnumerable<CompilerMaterializedSnapshot?>
    {
        private readonly Action _beforeFirst = beforeFirst;
        private readonly CompilerMaterializedSnapshot _body = body;
        private readonly int _count = count;
        public int MoveNextCalls { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<CompilerMaterializedSnapshot?> GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(ObservedSupply owner) : IEnumerator<CompilerMaterializedSnapshot?>
        {
            public CompilerMaterializedSnapshot Current => owner._body;
            object IEnumerator.Current => Current;
            public bool MoveNext() { owner.MoveNextCalls++; if (owner.MoveNextCalls == 1) owner._beforeFirst(); return owner.MoveNextCalls <= owner._count; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => owner.Disposed = true;
        }
    }
}
