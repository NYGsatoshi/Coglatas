using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class RelationGraphCancellationTests
{
    [Theory]
    [InlineData("snapshots", "open")]
    [InlineData("snapshots", "move")]
    [InlineData("snapshots", "current")]
    [InlineData("units", "open")]
    [InlineData("units", "move")]
    [InlineData("units", "current")]
    public void ApplicationCancellationStopsGraphSupplyAtTheObservedBoundary(string family, string boundary)
    {
        using var cancellation = new CancellationTokenSource(); var input = new RelationScopeSnapshot(Source(), true, true, true);
        var snapshots = new ObservedSupply<RelationScopeSnapshot>(boundary, cancellation.Cancel, new(Source(99), true, true, true));
        var units = new ObservedSupply<UnitDefinition>(boundary, cancellation.Cancel, Unit());
        var error = Assert.Throws<OperationCanceledException>(() => ProjectRelationValidation.Validate(input,
            family == "snapshots" ? snapshots : null, family == "units" ? units : null, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        var observedMoves = family == "snapshots" ? snapshots.MoveNextCalls : units.MoveNextCalls;
        var observedCurrent = family == "snapshots" ? snapshots.CurrentCalls : units.CurrentCalls;
        Assert.Equal(boundary == "open" ? 0 : 1, observedMoves);
        Assert.Equal(boundary == "current" ? 1 : 0, observedCurrent);
        Assert.True(family == "snapshots" ? snapshots.Disposed : units.Disposed);
    }

    [Theory]
    [InlineData("snapshots")]
    [InlineData("units")]
    public void CancellationWhenSupplyEndsKeepsTheOriginalTokenAndSkipsLaterSuppliers(string family)
    {
        using var cancellation = new CancellationTokenSource(); var input = new RelationScopeSnapshot(Source(), true, true, true);
        var snapshots = new ObservedSupply<RelationScopeSnapshot>("move", cancellation.Cancel, new(Source(99), true, true, true), count: 0);
        var units = new ObservedSupply<UnitDefinition>("move", cancellation.Cancel, Unit(), count: 0, throwOnOpen: family == "snapshots");
        var error = Assert.Throws<OperationCanceledException>(() => ProjectRelationValidation.Validate(input,
            family == "snapshots" ? snapshots : null, units, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(family == "snapshots" ? snapshots.Disposed : units.Disposed);
        Assert.Equal(1, family == "snapshots" ? snapshots.MoveNextCalls : units.MoveNextCalls);
        if (family == "snapshots") Assert.Equal(0, units.OpenCalls);
    }

    [Fact]
    public void AnAlreadyCancelledApplicationRequestNeverOpensGraphSupplies()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var snapshots = new ObservedSupply<RelationScopeSnapshot>("none", () => { }, new(Source(99), true, true, true), throwOnOpen: true);
        var units = new ObservedSupply<UnitDefinition>("none", () => { }, Unit(), throwOnOpen: true);
        var error = Assert.Throws<OperationCanceledException>(() => ProjectRelationValidation.Validate(new(Source(), true, true, true), snapshots, units,
            cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(0, snapshots.OpenCalls); Assert.Equal(0, units.OpenCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncancelledGraphRetainsDiamondClosureAndMarksOnlyActualCycleEdges(bool hasCycle)
    {
        var original = Source(); var edges = hasCycle
            ? new[] { Edge(20, 7, 8, original.Context), Edge(21, 8, 7, original.Context), Edge(22, 8, 9, original.Context), Edge(23, 9, 10, original.Context) }
            : new[] { Edge(20, 7, 8, original.Context), Edge(21, 7, 9, original.Context), Edge(22, 8, 10, original.Context), Edge(23, 9, 10, original.Context) };
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(Task(7), Task(8), Task(9), Task(10))),
            Json(new JsonArray(edges.Select(item => (JsonNode)item).ToArray())));
        var scope = new RelationScopeSnapshot(source, true, true, true);
        var direct = RelationGraphValidator.Validate(scope); var application = ProjectRelationValidation.Validate(scope).Result;
        Assert.Equal(direct.Findings, application.Findings); Assert.Equal(direct.Resolutions, application.Resolutions);
        Assert.Equal(8, direct.Resolutions.Count); Assert.All(direct.Resolutions, item => Assert.Equal(ReferenceResolutionState.Resolved, item.State));
        if (!hasCycle) { Assert.True(direct.IsComplete); Assert.Empty(direct.Findings); }
        else
        {
            Assert.Equal(new[] { RelationId.Parse(Id(20)), RelationId.Parse(Id(21)) }, direct.Findings.Select(item => item.Location.RelationId!.Value));
            var expected = Enumerable.Repeat(("coglatas.relation.dependency-cycle", RelationFindingState.Invalid), 2);
            Assert.Equal(expected, direct.Findings.Select(item => (item.Code, item.State)));
        }
    }

    private static JsonObject Task(int id) => Obj(new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
        payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
            state = "coglatas.known", value = "1" }, ports = Array.Empty<object>() } });
    private static JsonObject Endpoint(int entity, SourceRevisionContext context) => Obj(new { kind = "coglatas.entity", expectedKind = "coglatas.task",
        reference = new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = Id(entity), context = context.Data.Value } });
    private static JsonObject Edge(int id, int from, int to, SourceRevisionContext context) => Obj(new { relationId = Id(id), documentId = Id(4),
        kind = RelationKinds.DependsOn, schemaVersion = 1, descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
        source = Endpoint(from, context), target = Endpoint(to, context), payload = new { mode = "coglatas.finish-to-start", lag = new {
            type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "0" } } });
    private static UnitDefinition Unit()
    {
        var data = Json(Obj(new { unitId = "unit.kg", version = 1, dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } }));
        return new(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()));
    }

    private sealed class ObservedSupply<T>(string boundary, Action cancel, T item, int count = 2, bool throwOnOpen = false) : IEnumerable<T?> where T : class
    {
        private readonly string _boundary = boundary;
        private readonly Action _cancel = cancel;
        private readonly T _item = item;
        private readonly int _count = count;
        public int OpenCalls { get; private set; }
        public int MoveNextCalls { get; private set; }
        public int CurrentCalls { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<T?> GetEnumerator()
        {
            OpenCalls++;
            if (throwOnOpen) throw new InvalidOperationException("Graph supply must remain unopened.");
            var result = new Enumerator(this);
            if (_boundary == "open") _cancel();
            return result;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(ObservedSupply<T> owner) : IEnumerator<T?>
        {
            public T Current { get { owner.CurrentCalls++; if (owner._boundary == "current") owner._cancel(); return owner._item; } }
            object IEnumerator.Current => Current;
            public bool MoveNext() { owner.MoveNextCalls++; if (owner.MoveNextCalls == 1 && owner._boundary == "move") owner._cancel(); return owner.MoveNextCalls <= owner._count; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => owner.Disposed = true;
        }
    }
}
