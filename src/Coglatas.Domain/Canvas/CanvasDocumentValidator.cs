namespace Coglatas.Domain.Canvas;

public sealed class CanvasValidationException(string message) : InvalidOperationException(message)
{
}

/// <summary>
/// Strict local invariants for a single document snapshot. Not a replacement for
/// tenant/resource authorization, transport validation, quotas or persisted transactions.
/// </summary>
public static class CanvasDocumentValidator
{
    public static void Validate(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireId(document.Id, "Document");
        RequireId(document.TenantId, "Tenant");
        RequireId(document.WorkspaceId, "Workspace");

        if (document.SchemaVersion != CanvasDocument.CurrentSchemaVersion || document.Revision < 0)
            throw new CanvasValidationException("Unsupported schema version or revision.");

        if (document.Pages.IsDefaultOrEmpty || document.Pages.Length > 128)
            throw new CanvasValidationException("Document requires between 1 and 128 pages.");

        var ids = new HashSet<Guid> { document.Id };

        foreach (var page in document.Pages)
        {
            Register(ids, page.Id, "Page");

            if (string.IsNullOrWhiteSpace(page.Name) || page.Name.Length > 256)
                throw new CanvasValidationException("Page name is invalid.");

            if (page.Regions.IsDefaultOrEmpty || page.Regions.Length > 128)
                throw new CanvasValidationException("Page requires between 1 and 128 regions.");

            foreach (var region in page.Regions)
            {
                Register(ids, region.Id, "Region");

                if (!Enum.IsDefined(region.Kind) || !Enum.IsDefined(region.Layout))
                    throw new CanvasValidationException("Unknown region or layout kind.");
                if (region.Nodes.IsDefault || region.Connections.IsDefault ||
                    region.Nodes.Length > 50_000 || region.Connections.Length > 250_000)
                    throw new CanvasValidationException("Region collection is invalid or too large.");

                var byNodeId = new Dictionary<Guid, CanvasNode>();
                foreach (var node in region.Nodes)
                {
                    if (node is null)
                        throw new CanvasValidationException("Null node.");
                    Register(ids, node.Id, "Node");

                    if (!Enum.IsDefined(node.Kind) || node.Text is null ||
                        node.Text.Length > 32_768 || !node.Position.IsFinite || !node.Size.IsValid)
                        throw new CanvasValidationException("Node content or geometry is invalid.");
                    if (region.Kind == CanvasRegionKind.MindMap && node.Kind != CanvasNodeKind.Topic)
                        throw new CanvasValidationException("Mind map nodes must be topics.");
                    if (region.Kind != CanvasRegionKind.MindMap && node.ParentId is not null)
                        throw new CanvasValidationException("Only mind map nodes have a parent.");

                    byNodeId.Add(node.Id, node);
                }

                ValidateMindMap(region, byNodeId);

                foreach (var connection in region.Connections)
                {
                    if (connection is null)
                        throw new CanvasValidationException("Null connection.");
                    Register(ids, connection.Id, "Connection");

                    if (!Enum.IsDefined(connection.Kind) ||
                        !byNodeId.ContainsKey(connection.SourceNodeId) ||
                        !byNodeId.ContainsKey(connection.TargetNodeId))
                        throw new CanvasValidationException("Connection references a missing or external node.");

                    if (region.Kind == CanvasRegionKind.MindMap &&
                        connection.Kind != CanvasConnectionKind.CrossLink)
                        throw new CanvasValidationException("Mind map hierarchy uses ParentId, not connector edges.");
                    if (connection.SourcePort is { Length: > 128 } ||
                        connection.TargetPort is { Length: > 128 })
                        throw new CanvasValidationException("Connection port identifier is too long.");
                }
            }
        }
    }

    private static void ValidateMindMap(
        CanvasRegion region,
        IReadOnlyDictionary<Guid, CanvasNode> byNodeId)
    {
        if (region.Kind != CanvasRegionKind.MindMap)
            return;

        if (byNodeId.Count > 0 && byNodeId.Values.Count(node => node.ParentId is null) != 1)
            throw new CanvasValidationException("A non-empty mind map requires exactly one root.");

        // Mark each validated ancestor chain once, avoiding quadratic work on deep maps.
        var validated = new HashSet<Guid>();
        foreach (var node in byNodeId.Values)
        {
            if (validated.Contains(node.Id))
                continue;

            var path = new HashSet<Guid>();
            var current = node;
            while (true)
            {
                if (validated.Contains(current.Id))
                    break;
                if (!path.Add(current.Id))
                    throw new CanvasValidationException("Mind map parentage contains a cycle.");
                if (current.ParentId is not Guid parentId)
                    break;
                if (!byNodeId.TryGetValue(parentId, out var parent))
                    throw new CanvasValidationException("Mind map parent must belong to the same region.");
                current = parent;
            }

            validated.UnionWith(path);
        }
    }

    private static void Register(HashSet<Guid> ids, Guid id, string kind)
    {
        RequireId(id, kind);
        if (!ids.Add(id))
            throw new CanvasValidationException($"Duplicate {kind} identity within Canvas document.");
    }

    private static void RequireId(Guid id, string kind)
    {
        if (id == Guid.Empty)
            throw new CanvasValidationException($"{kind} identity must not be empty.");
    }
}
