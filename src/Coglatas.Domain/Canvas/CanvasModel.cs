using System.Collections.Immutable;

namespace Coglatas.Domain.Canvas;

public enum CanvasRegionKind
{
    Freeform = 0,
    MindMap = 1,
    Diagram = 2
}

public enum CanvasNodeKind
{
    Topic = 0,
    Shape = 1,
    StickyNote = 2,
    Text = 3
}

public enum CanvasConnectionKind
{
    CrossLink = 0,
    Connector = 1
}

public enum CanvasLayoutKind
{
    Manual = 0,
    Radial = 1,
    LeftTree = 2,
    RightTree = 3,
    TopDown = 4,
    Flowchart = 5
}

public readonly record struct CanvasPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct CanvasSize(double Width, double Height)
{
    public bool IsValid => double.IsFinite(Width) && Width > 0
        && double.IsFinite(Height) && Height > 0;
}

/// <summary>
/// Visual content only. Hierarchical parentage is meaningful solely within MindMap regions.
/// No external domain entities or execution semantics are implied by a node.
/// </summary>
public sealed record CanvasNode(
    Guid Id,
    CanvasNodeKind Kind,
    string Text,
    CanvasPoint Position,
    CanvasSize Size,
    Guid? ParentId = null,
    bool IsPinned = false);

/// <summary>
/// Cross-links and graphical connectors are distinct from MindMap parentage.
/// Ports are optional symbolic names; their catalog is owned by a future editor/shape pack.
/// </summary>
public sealed record CanvasConnection(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    CanvasConnectionKind Kind,
    string? SourcePort = null,
    string? TargetPort = null);

public sealed record CanvasRegion(
    Guid Id,
    CanvasRegionKind Kind,
    CanvasLayoutKind Layout,
    ImmutableArray<CanvasNode> Nodes,
    ImmutableArray<CanvasConnection> Connections);

public sealed record CanvasPage(
    Guid Id,
    string Name,
    ImmutableArray<CanvasRegion> Regions);

/// <summary>
/// UI- and storage-neutral Canvas snapshot. Authorization and transactional revision
/// enforcement belong to future application/infrastructure adapters, not this model.
/// </summary>
public sealed record CanvasDocument(
    Guid Id,
    Guid TenantId,
    Guid WorkspaceId,
    int SchemaVersion,
    long Revision,
    ImmutableArray<CanvasPage> Pages)
{
    public const string SchemaId = "coglatas.canvas.document";
    public const int CurrentSchemaVersion = 1;

    public static CanvasDocument Create(Guid tenantId, Guid workspaceId,
        CanvasRegionKind regionKind = CanvasRegionKind.MindMap)
    {
        var region = new CanvasRegion(
            Guid.NewGuid(),
            regionKind,
            regionKind == CanvasRegionKind.MindMap ? CanvasLayoutKind.RightTree : CanvasLayoutKind.Manual,
            ImmutableArray<CanvasNode>.Empty,
            ImmutableArray<CanvasConnection>.Empty);

        var document = new CanvasDocument(
            Guid.NewGuid(),
            tenantId,
            workspaceId,
            CurrentSchemaVersion,
            0,
            ImmutableArray.Create(new CanvasPage(
                Guid.NewGuid(),
                "Page 1",
                ImmutableArray.Create(region))));

        CanvasDocumentValidator.Validate(document);
        return document;
    }
}
