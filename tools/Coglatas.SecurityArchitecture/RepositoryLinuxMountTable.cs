using System.Text;

namespace Coglatas.SecurityArchitecture;

/// <summary>Compares physical Linux paths in the underlying filesystem, including bind-mount roots.</summary>
internal sealed class RepositoryLinuxMountTable
{
    private readonly List<Mount> _mounts = [];
    private readonly Func<string, string> _physicalPath;

    internal RepositoryLinuxMountTable(IEnumerable<string> lines, Func<string, string>? physicalPath = null)
    {
        _physicalPath = physicalPath ?? (path => path);
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-");
            if (separator < 6 || fields.Length != separator + 4 || fields[2].Split(':') is not [var major, var minor] ||
                !uint.TryParse(major, out _) || !uint.TryParse(minor, out _))
                throw new IOException("Cannot establish the Linux mount boundary.");
            _mounts.Add(new Mount(fields[2], DecodePath(fields[3]), DecodePath(fields[4]), fields[separator + 1], fields[separator + 3]));
        }
    }

    internal bool IsWithinRepository(string repository, string path)
    {
        var root = Locate(repository);
        var candidate = Locate(path);
        if (root.Device == candidate.Device && AtOrBelow(candidate.Path, root.Path)) return true;
        if (InsideOverlayLayers(root.Mount, candidate, [])) return true;
        // The inspected tree can itself contain mounted filesystems; aliases of those subtrees are also inside it.
        foreach (var mount in _mounts)
        {
            if (!AtOrBelow(mount.Point, repository)) continue;
            var subtree = Locate(mount.Point);
            if (subtree.Device == candidate.Device && AtOrBelow(candidate.Path, subtree.Path)) return true;
            if (InsideOverlayLayers(subtree.Mount, candidate, [])) return true;
        }
        return false;
    }

    private bool InsideOverlayLayers(Mount mount, (string Device, string Path, Mount Mount) candidate, HashSet<Mount> visited)
    {
        if (mount.Type != "overlay" || mount.Device == candidate.Device) return false;
        if (!visited.Add(mount)) throw new IOException("Cannot establish the Linux overlay boundary.");
        var layers = mount.Options.Split(',').Where(option => option.StartsWith("lowerdir=", StringComparison.Ordinal) ||
            option.StartsWith("upperdir=", StringComparison.Ordinal)).ToArray();
        if (!layers.Any(option => option.StartsWith("lowerdir=", StringComparison.Ordinal)))
            throw new IOException("Cannot establish the Linux overlay boundary.");
        foreach (var option in layers)
        {
            foreach (var encoded in option[(option.IndexOf('=') + 1)..].Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var layer = Locate(_physicalPath(DecodePath(encoded)));
                // Treat the entire backing layer as untrusted, including redirected/metacopy subtrees.
                if (layer.Device == candidate.Device && AtOrBelow(candidate.Path, layer.Path)) return true;
                if (InsideOverlayLayers(layer.Mount, candidate, visited)) return true;
            }
        }
        visited.Remove(mount);
        return false;
    }

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

    private sealed record Mount(string Device, string Root, string Point, string Type, string Options);
}
