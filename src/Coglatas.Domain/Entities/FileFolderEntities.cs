using Coglatas.Domain.Common;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Authoritative shared folder identity inside one Workspace. The folder tree
/// is logical metadata only; storage provider paths and object keys never
/// participate in the hierarchy.
/// </summary>
public sealed class FileFolder : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public long Version { get; set; } = 1;

    public Workspace? Workspace { get; set; }
    public FileFolder? ParentFolder { get; set; }
    public ICollection<FileFolder> ChildFolders { get; } = new List<FileFolder>();
}

/// <summary>
/// Optimistic concurrency state for the Workspace root container. Absence of
/// a row is the initial root state at logical version 0.
/// </summary>
public sealed class FileFolderRootState : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public long Version { get; set; } = 1;

    public Workspace? Workspace { get; set; }
}

/// <summary>
/// Logical placement of a canonical FileObject in the Workspace folder tree.
/// Absence of a row means the file is at Workspace root with logical version 0.
/// </summary>
public sealed class FileFolderPlacement : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid? FolderId { get; set; }
    public long Version { get; set; } = 1;

    public Workspace? Workspace { get; set; }
    public FileObject? FileObject { get; set; }
    public FileFolder? Folder { get; set; }
}
