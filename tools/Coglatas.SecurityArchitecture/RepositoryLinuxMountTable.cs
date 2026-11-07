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
            _mounts.Add(new Mount(fields[2], DecodePath(fields[3]), DecodePath(fields[4])));
        }
    }

    internal bool IsWithinRepository(string repository, string path)
    {
        var root = Locate(repository);
        var candidate = Locate(path);
        if (root.Device == candidate.Device && AtOrBelow(candidate.Path, root.Path)) return true;
        // The inspected tree can itself contain mounted filesystems; aliases of those subtrees are also inside it.
        foreach (var mount in _mounts)
        {
            if (!AtOrBelow(mount.Point, repository)) continue;
            var subtree = Locate(mount.Point);
            if (subtree.Device == candidate.Device && AtOrBelow(candidate.Path, subtree.Path)) return true;
        }
        return false;
    }

    private (string Device, string Path) Locate(string path)
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
        return (selected.Device, mapped.Length == 0 ? "/" : mapped);
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
                @"\040" => ' ', @"\011" => '\t', @"\012" => '\n', @"\134" => '\\',
                _ => throw new IOException("Cannot establish the Linux mount boundary.")
            });
            i += 3;
        }
        var path = result.ToString();
        if (!path.StartsWith('/') || path.Contains('\0') || path.Split('/').Any(part => part is "." or ".."))
            throw new IOException("Cannot establish the Linux mount boundary.");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private sealed record Mount(string Device, string Root, string Point);
}
