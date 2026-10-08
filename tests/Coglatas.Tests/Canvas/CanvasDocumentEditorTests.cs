using Coglatas.Domain.Canvas;

namespace Coglatas.Tests.Canvas;

public sealed class CanvasDocumentEditorTests
{
    [Fact]
    public void MindMap_AddsOneRootAndChildrenInAnImmutableSnapshot()
    {
        var original = NewDocument();
        var root = Topic();
        var child = Topic(root.Id);

        var afterRoot = Add(original, root);
        var afterChild = Add(afterRoot, child);

        Assert.Equal(0, original.Revision);
        Assert.Empty(Region(original).Nodes);
        Assert.Equal(2, afterChild.Revision);
        Assert.Equal(root.Id, Region(afterChild).Nodes[1].ParentId);
        Assert.Equal(original.Id, afterChild.Id);
        Assert.Equal(original.TenantId, afterChild.TenantId);
        Assert.Equal(original.WorkspaceId, afterChild.WorkspaceId);
    }

    [Fact]
    public void MindMap_RejectsSecondRootWithoutMutatingThePreviousSnapshot()
    {
        var document = Add(NewDocument(), Topic());
        var revision = document.Revision;

        Assert.Throws<CanvasValidationException>(() => Add(document, Topic()));
        Assert.Equal(revision, document.Revision);
        Assert.Single(Region(document).Nodes);
    }

    [Fact]
    public void MindMap_RejectsAnIndirectParentCycleEvenWithASeparateRoot()
    {
        var root = Topic();
        var middle = Topic(root.Id);
        var leaf = Topic(middle.Id);
        var document = Add(Add(Add(NewDocument(), root), middle), leaf);

        Assert.Throws<CanvasValidationException>(() =>
            CanvasDocumentEditor.Apply(document, new SetCanvasNodeParent(
                document.Revision, PageId(document), RegionId(document), middle.Id, leaf.Id)));

        Assert.Equal(root.Id, Region(document).Nodes[1].ParentId);
    }

    [Fact]
    public void MindMap_CrossLinksDoNotChangeParentage()
    {
        var root = Topic();
        var child = Topic(root.Id);
        var document = Add(Add(NewDocument(), root), child);

        document = Link(document, new CanvasConnection(
            Guid.NewGuid(), child.Id, root.Id, CanvasConnectionKind.CrossLink));

        Assert.Single(Region(document).Connections);
        Assert.Equal(root.Id, Region(document).Nodes[1].ParentId);
    }

    [Fact]
    public void Diagram_AcceptsCyclicDirectedConnectors()
    {
        var a = Shape();
        var b = Shape();
        var document = Add(Add(NewDocument(CanvasRegionKind.Diagram), a), b);

        document = Link(document, new CanvasConnection(
            Guid.NewGuid(), a.Id, b.Id, CanvasConnectionKind.Connector, "out", "in"));
        document = Link(document, new CanvasConnection(
            Guid.NewGuid(), b.Id, a.Id, CanvasConnectionKind.Connector));

        Assert.Equal(2, Region(document).Connections.Length);
        Assert.Equal(4, document.Revision);
    }

    [Fact]
    public void Connections_RejectMissingOrOutOfRegionEndpoints()
    {
        var node = Shape();
        var document = Add(NewDocument(CanvasRegionKind.Diagram), node);

        Assert.Throws<CanvasValidationException>(() =>
            Link(document, new CanvasConnection(
                Guid.NewGuid(), node.Id, Guid.NewGuid(), CanvasConnectionKind.Connector)));

        Assert.Empty(Region(document).Connections);
    }

    [Fact]
    public void RevisionConflict_RejectsStaleMutationsBeforeApplying()
    {
        var document = Add(NewDocument(), Topic());
        var stale = new AddCanvasNode(
            0, PageId(document), RegionId(document), Topic());

        var error = Assert.Throws<CanvasRevisionConflictException>(() =>
            CanvasDocumentEditor.Apply(document, stale));

        Assert.Equal(0, error.ExpectedRevision);
        Assert.Equal(1, error.ActualRevision);
        Assert.Single(Region(document).Nodes);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void Coordinates_MustBeFinite(double x, double y)
    {
        var invalid = Topic() with { Position = new CanvasPoint(x, y) };

        Assert.Throws<CanvasValidationException>(() => Add(NewDocument(), invalid));
    }

    [Fact]
    public void NodeSizes_MustBeFiniteAndPositive()
    {
        var invalid = Topic() with { Size = new CanvasSize(0, 80) };

        Assert.Throws<CanvasValidationException>(() => Add(NewDocument(), invalid));
    }

    [Fact]
    public void DeletingDiagramNodeRemovesOnlyItsIncidentConnections()
    {
        var a = Shape();
        var b = Shape();
        var document = Add(Add(NewDocument(CanvasRegionKind.Diagram), a), b);
        document = Link(document, new CanvasConnection(
            Guid.NewGuid(), a.Id, b.Id, CanvasConnectionKind.Connector));

        document = CanvasDocumentEditor.Apply(document, new RemoveCanvasNode(
            document.Revision, PageId(document), RegionId(document), b.Id));

        Assert.Single(Region(document).Nodes);
        Assert.Equal(a.Id, Region(document).Nodes[0].Id);
        Assert.Empty(Region(document).Connections);
    }

    [Fact]
    public void DeletingMindMapParentRequiresRemovingDescendantsFirst()
    {
        var root = Topic();
        var child = Topic(root.Id);
        var document = Add(Add(NewDocument(), root), child);

        Assert.Throws<CanvasValidationException>(() =>
            CanvasDocumentEditor.Apply(document, new RemoveCanvasNode(
                document.Revision, PageId(document), RegionId(document), root.Id)));
    }

    [Fact]
    public void DuplicateCanvasIdentitiesAreRejected()
    {
        var root = Topic();
        var document = Add(NewDocument(), root);

        var duplicate = Topic(root.Id) with { Id = root.Id };
        Assert.Throws<CanvasValidationException>(() => Add(document, duplicate));
    }

    [Fact]
    public void InvalidSchemaOrTenantScopeCannotBeAccepted()
    {
        var document = NewDocument();

        Assert.Throws<CanvasValidationException>(() =>
            CanvasDocumentValidator.Validate(document with { SchemaVersion = 999 }));
        Assert.Throws<CanvasValidationException>(() =>
            CanvasDocumentValidator.Validate(document with { TenantId = Guid.Empty }));
        Assert.Throws<CanvasValidationException>(() =>
            CanvasDocumentValidator.Validate(document with { Revision = -1 }));
    }

    private static CanvasDocument NewDocument(CanvasRegionKind kind = CanvasRegionKind.MindMap) =>
        CanvasDocument.Create(Guid.NewGuid(), Guid.NewGuid(), kind);

    private static CanvasNode Topic(Guid? parentId = null) =>
        new(Guid.NewGuid(), CanvasNodeKind.Topic, "Topic",
            new CanvasPoint(10, 20), new CanvasSize(120, 60), parentId);

    private static CanvasNode Shape() =>
        new(Guid.NewGuid(), CanvasNodeKind.Shape, "Shape",
            new CanvasPoint(10, 20), new CanvasSize(140, 90));

    private static CanvasRegion Region(CanvasDocument document) =>
        document.Pages[0].Regions[0];

    private static Guid PageId(CanvasDocument document) => document.Pages[0].Id;
    private static Guid RegionId(CanvasDocument document) => Region(document).Id;

    private static CanvasDocument Add(CanvasDocument document, CanvasNode node) =>
        CanvasDocumentEditor.Apply(document,
            new AddCanvasNode(document.Revision, PageId(document), RegionId(document), node));

    private static CanvasDocument Link(CanvasDocument document, CanvasConnection connection) =>
        CanvasDocumentEditor.Apply(document,
            new AddCanvasConnection(document.Revision, PageId(document), RegionId(document), connection));
}
