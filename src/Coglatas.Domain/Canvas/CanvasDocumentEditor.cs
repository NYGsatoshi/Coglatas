using System.Collections.Immutable;

namespace Coglatas.Domain.Canvas;

/// <summary>
/// Canvas commands are not ProjectIDE commands. The document revision acts as
/// an optimistic guard; durable idempotency and collaborative ordering are future concerns.
/// </summary>
public abstract record CanvasCommand(long ExpectedRevision, Guid PageId, Guid RegionId);

public sealed record AddCanvasNode(long ExpectedRevision, Guid PageId, Guid RegionId, CanvasNode Node)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record RenameCanvasNode(long ExpectedRevision, Guid PageId, Guid RegionId, Guid NodeId, string Text)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record MoveCanvasNode(long ExpectedRevision, Guid PageId, Guid RegionId, Guid NodeId, CanvasPoint Position)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record SetCanvasNodeParent(long ExpectedRevision, Guid PageId, Guid RegionId, Guid NodeId, Guid? ParentId)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record RemoveCanvasNode(long ExpectedRevision, Guid PageId, Guid RegionId, Guid NodeId)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record AddCanvasConnection(long ExpectedRevision, Guid PageId, Guid RegionId, CanvasConnection Connection)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record RemoveCanvasConnection(long ExpectedRevision, Guid PageId, Guid RegionId, Guid ConnectionId)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed record SetCanvasLayout(long ExpectedRevision, Guid PageId, Guid RegionId, CanvasLayoutKind Layout)
    : CanvasCommand(ExpectedRevision, PageId, RegionId);

public sealed class CanvasRevisionConflictException(long expected, long actual)
    : InvalidOperationException($"Canvas revision conflict: expected {expected}, actual {actual}.")
{
    public long ExpectedRevision { get; } = expected;
    public long ActualRevision { get; } = actual;
}

public static class CanvasDocumentEditor
{
    /// <summary>
    /// Applies a single command to a new immutable snapshot. Invalid commands never mutate
    /// the caller's document. Persistence must re-check revision and authority atomically.
    /// </summary>
    public static CanvasDocument Apply(CanvasDocument document, CanvasCommand command)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);
        CanvasDocumentValidator.Validate(document);

        if (document.Revision != command.ExpectedRevision)
            throw new CanvasRevisionConflictException(command.ExpectedRevision, document.Revision);

        var pageIndex = IndexOfPage(document.Pages, command.PageId);
        if (pageIndex < 0)
            throw new CanvasValidationException("Page is not in the document.");

        var page = document.Pages[pageIndex];
        var regionIndex = IndexOfRegion(page.Regions, command.RegionId);
        if (regionIndex < 0)
            throw new CanvasValidationException("Region is not in the specified page.");

        var region = page.Regions[regionIndex];
        var changed = command switch
        {
            AddCanvasNode add => region with { Nodes = region.Nodes.Add(
                add.Node ?? throw new CanvasValidationException("Node cannot be null.")) },
            RenameCanvasNode rename => ChangeNode(
                region, rename.NodeId, node => node with { Text = rename.Text }),
            MoveCanvasNode move => ChangeNode(
                region, move.NodeId, node => node with { Position = move.Position }),
            SetCanvasNodeParent parent => ChangeNode(
                region, parent.NodeId, node => node with { ParentId = parent.ParentId }),
            RemoveCanvasNode remove => RemoveNode(region, remove.NodeId),
            AddCanvasConnection add => region with { Connections = region.Connections.Add(
                add.Connection ?? throw new CanvasValidationException("Connection cannot be null.")) },
            RemoveCanvasConnection remove => RemoveConnection(region, remove.ConnectionId),
            SetCanvasLayout layout => region with { Layout = layout.Layout },
            _ => throw new CanvasValidationException("Unsupported Canvas command.")
        };

        var nextPage = page with { Regions = page.Regions.SetItem(regionIndex, changed) };
        var next = document with
        {
            Revision = checked(document.Revision + 1),
            Pages = document.Pages.SetItem(pageIndex, nextPage)
        };

        CanvasDocumentValidator.Validate(next);
        return next;
    }

    private static CanvasRegion ChangeNode(CanvasRegion region, Guid id,
        Func<CanvasNode, CanvasNode> change)
    {
        var index = IndexOfNode(region.Nodes, id);
        if (index < 0)
            throw new CanvasValidationException("Node is not in the specified region.");
        return region with { Nodes = region.Nodes.SetItem(index, change(region.Nodes[index])) };
    }

    private static CanvasRegion RemoveNode(CanvasRegion region, Guid id)
    {
        var index = IndexOfNode(region.Nodes, id);
        if (index < 0)
            throw new CanvasValidationException("Node is not in the specified region.");

        if (region.Nodes.Any(node => node.ParentId == id))
            throw new CanvasValidationException("Remove mind map descendants before removing their parent.");

        return region with
        {
            Nodes = region.Nodes.RemoveAt(index),
            Connections = region.Connections
                .Where(link => link.SourceNodeId != id && link.TargetNodeId != id)
                .ToImmutableArray()
        };
    }

    private static CanvasRegion RemoveConnection(CanvasRegion region, Guid id)
    {
        for (var index = 0; index < region.Connections.Length; index++)
        {
            if (region.Connections[index].Id == id)
                return region with { Connections = region.Connections.RemoveAt(index) };
        }
        throw new CanvasValidationException("Connection is not in the specified region.");
    }

    private static int IndexOfPage(ImmutableArray<CanvasPage> items, Guid id)
    {
        for (var i = 0; i < items.Length; i++)
            if (items[i].Id == id) return i;
        return -1;
    }

    private static int IndexOfRegion(ImmutableArray<CanvasRegion> items, Guid id)
    {
        for (var i = 0; i < items.Length; i++)
            if (items[i].Id == id) return i;
        return -1;
    }

    private static int IndexOfNode(ImmutableArray<CanvasNode> items, Guid id)
    {
        for (var i = 0; i < items.Length; i++)
            if (items[i].Id == id) return i;
        return -1;
    }
}
