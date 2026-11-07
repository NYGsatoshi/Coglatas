using System.Text;

namespace Coglatas.SecurityArchitecture;

/// <summary>Compares physical Linux paths in the underlying filesystem, including bind-mount roots.</summary>
internal sealed class RepositoryLinuxMountTable
{
    private readonly List<Mount> _mounts = [];

    internal RepositoryLinuxMountTable(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-");
            if (separator < 6 || fields.Length != separator + 4 || fields[2].Split(':') is not [var major, var minor] ||
                !uint.TryParse(major, out _) || !uint.TryParse(minor, out _))
                throw new IOException("Cannot establish the Linux mount boundary.");
            _mounts.Add(new Mount(fields[2], DecodePath(fields[3]), DecodePath(fields[4]), fields[separator + 1]));
        }
    }

    internal bool IsWithinRepository(string repository, string path)
    {
        var root = Locate(repository);
        var candidate = Locate(path);
        if (root.Device == candidate.Device && AtOrBelow(candidate.Path, root.Path)) return true;
        RequireComparableViews(root.Mount, candidate.Mount);
        // The inspected tree can itself contain mounted filesystems; aliases of those subtrees are also inside it.
        foreach (var mount in _mounts)
        {
            if (!AtOrBelow(mount.Point, repository)) continue;
            var subtree = Locate(mount.Point);
            if (subtree.Device == candidate.Device && AtOrBelow(candidate.Path, subtree.Path)) return true;
            RequireComparableViews(subtree.Mount, candidate.Mount);
        }
        return false;
    }

    private static void RequireComparableViews(Mount repository, Mount candidate)
    {
        // Option pathnames cannot identify the layers held by OverlayFS after namespace changes.
        if (repository.Type == "overlay" && candidate.Type == "overlay" && repository.Device == candidate.Device) return;
        if (IsTransparent(repository.Type) && IsTransparent(candidate.Type)) return;
        throw new IOException("Automatic Git selection cannot establish this filesystem boundary. Configure a trusted Git installation.");
    }

    private static bool IsTransparent(string type) => type is
        "ext2" or "ext3" or "ext4" or "xfs" or "btrfs" or "tmpfs" or "9p" or "virtiofs" or "vfat" or "exfat" or "ntfs3";

    private (string Device, string Path, Mount Mount) Locate(string path)
    {
        Mount? selected = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mount in _mounts)
        {
            if (!AtOrBelow(path, mount.Point)) continue;
            // A hidden stacked mount can also hide its longer child mount paths. Do not guess which is visible.
            if (!seen.Add(mount.Point)) throw new IOException("Cannot establish the Linux mount boundary.");
            if (selected is null || mount.Point.Length > selected.Point.Length) selected = mount;
        }
        if (selected is null) throw new IOException("Cannot establish the Linux mount boundary.");
        var relative = path[selected.Point.Length..].TrimStart('/');
        var mapped = selected.Root.TrimEnd('/') + (relative.Length == 0 ? "" : "/" + relative);
        return (selected.Device, mapped.Length == 0 ? "/" : mapped, selected);
    }

    private static bool AtOrBelow(string path, string parent) =>
        path.Equals(parent, StringComparison.Ordinal) ||
        path.StartsWith(parent.EndsWith('/') ? parent : parent + '/', StringComparison.Ordinal);

    private static string DecodePath(string encoded)
    {
        var result = new StringBuilder();
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '\\') { result.Append(encoded[i]); continue; }
            if (i + 3 >= encoded.Length) throw new IOException("Cannot establish the Linux mount boundary.");
            result.Append(encoded.Substring(i, 4) switch
            {
                @"\040" => ' ', @"\011" => '\t', @"\012" => '\n', @"\134" => '\\', @"\054" => ',', @"\072" => ':',
                _ => throw new IOException("Cannot establish the Linux mount boundary.")
            });
            i += 3;
        }
        var path = result.ToString();
        if (!path.StartsWith('/') || path.Contains('\0') || path.Split('/').Any(part => part is "." or ".."))
            throw new IOException("Cannot establish the Linux mount boundary.");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private sealed record Mount(string Device, string Root, string Point, string Type);
}
