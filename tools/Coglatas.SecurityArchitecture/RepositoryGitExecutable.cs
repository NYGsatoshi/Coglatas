using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Coglatas.SecurityArchitecture;

/// <summary>Selects host Git with automatic repository checks or explicit operator trust configuration.</summary>
internal static class RepositoryGitExecutable
{
    internal const string TrustedExecutableEnvironmentVariable = "COGLATAS_TRUSTED_GIT_EXECUTABLE";

    internal static string Resolve(string repositoryRoot, string? searchPath, string? trustedExecutable = null,
        IReadOnlyCollection<string>? inspectedRepositories = null)
    {
        var windows = OperatingSystem.IsWindows();
        var repositories = new[] { repositoryRoot }.Concat(inspectedRepositories ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var windowsRoots = windows ? repositories.Select(WindowsIdentity).ToArray() : [];
        var roots = windows ? [] : repositories.Select(PhysicalPath).ToArray();
        if (trustedExecutable is not null)
        {
            // Only the invoking operator supplies this trust input; never derive it from repository declarations.
            if (trustedExecutable.Contains('\0') || !Path.IsPathFullyQualified(trustedExecutable) ||
                string.IsNullOrEmpty(Path.GetFileName(trustedExecutable)))
                throw new FileNotFoundException("The configured trusted Git installation is unavailable.");
            if (windows)
            {
                if (!File.Exists(trustedExecutable)) throw new FileNotFoundException("The configured trusted Git installation is unavailable.");
                var configuredDirectory = Path.GetDirectoryName(trustedExecutable)!;
                if (HasWindowsRepositoryAncestor(configuredDirectory, windowsRoots) ||
                    HasWindowsRepositoryAncestor(WindowsExecutablePath(configuredDirectory), windowsRoots))
                    throw new FileNotFoundException("The configured Git installation is inside an inspected repository.");
                var executable = WindowsExecutablePath(trustedExecutable);
                if (HasWindowsRepositoryAncestor(executable, windowsRoots))
                    throw new FileNotFoundException("The configured Git installation is inside the inspected repository.");
                return executable;
            }
            var directory = PhysicalPath(Path.GetDirectoryName(trustedExecutable)!);
            var selected = Path.Combine(directory, Path.GetFileName(trustedExecutable));
            var physical = PhysicalPath(selected);
            if (!File.Exists(physical) || HasUnixRepositoryAncestor(Path.GetDirectoryName(trustedExecutable)!, roots, null) ||
                roots.Any(root => IsWithinUnixRepository(physical, root)) || Access(physical, 1) != 0)
                throw new FileNotFoundException("The configured trusted Git installation is unavailable.");
            return selected;
        }
        var mounts = OperatingSystem.IsLinux() ? new RepositoryLinuxMountTable(File.ReadLines("/proc/self/mountinfo")) : null;
        foreach (var rawEntry in (searchPath ?? "").Split(Path.PathSeparator))
        {
            // Windows PATH commonly quotes installation paths containing spaces. Unix quotes are literal.
            var entry = OperatingSystem.IsWindows() && rawEntry is ['"', .., '"']
                ? rawEntry[1..^1] : rawEntry;
            if (entry.Contains('\0') || !Path.IsPathFullyQualified(entry)) continue;
            try
            {
                var candidate = Path.Combine(entry, OperatingSystem.IsWindows() ? "git.exe" : "git");
                if (windows)
                {
                    if (HasWindowsRepositoryAncestor(entry, windowsRoots) ||
                        HasWindowsRepositoryAncestor(WindowsExecutablePath(entry), windowsRoots)) continue;
                    if (!File.Exists(candidate)) continue;
                    var executable = WindowsExecutablePath(candidate);
                    if (HasWindowsRepositoryAncestor(executable, windowsRoots)) continue;
                    return executable;
                }
                // Resolve the directory physically but retain the Git invocation name for trusted multicall shims.
                var directory = PhysicalPath(entry);
                candidate = Path.Combine(directory, "git");
                var physical = PhysicalPath(candidate);
                if (!File.Exists(physical)) continue;
                if (HasUnixRepositoryAncestor(entry, roots, mounts) || roots.Any(root => IsWithinUnixRepository(physical, root))) continue;
                if (mounts is not null && roots.Any(root => mounts.IsWithinRepository(root, physical))) continue;
                if (Access(physical, 1) != 0) continue;
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
            {
                // An unusable host PATH entry does not authorize falling back to implicit current-directory search.
            }
        }
        throw new FileNotFoundException("No supported Git installation is available outside the inspected repository.");
    }

    private static bool HasWindowsRepositoryAncestor(string path, WindowsFileIdentity[] roots) =>
        roots.Any(root => IsWithinWindowsRepository(path, root));

    private static bool HasUnixRepositoryAncestor(string entry, string[] roots, RepositoryLinuxMountTable? mounts)
    {
        // Inspect the original path's ancestry as well as its final target: an input repository can supply a link.
        for (string? path = entry; path is not null; path = Path.GetDirectoryName(path))
        {
            var physical = PhysicalPath(path);
            if (roots.Any(root => IsWithinUnixRepository(physical, root))) return true;
            if (mounts is not null && roots.Any(root => mounts.IsWithinRepository(root, physical,
                    requireComparableViews: path == entry))) return true;
        }
        return false;
    }

    private static bool IsWithinUnixRepository(string path, string root) =>
        path.Equals(root, StringComparison.Ordinal) || path.StartsWith(
            root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string PhysicalPath(string path)
    {
        if (path.Contains('\0')) throw new ArgumentException("Unsupported path.");
        var pointer = RealPath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero) throw new IOException("Cannot resolve the physical path.");
        try { return Marshal.PtrToStringUTF8(pointer) ?? throw new IOException("Cannot resolve the physical path."); }
        finally { Free(pointer); }
    }

    private static string WindowsExecutablePath(string candidate)
    {
        using var handle = OpenWindowsPath(candidate);
        var path = FinalWindowsPath(handle, 0);
        // Git for Windows' launcher must receive its ordinary absolute installation path.
        path = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        if (!Path.IsPathFullyQualified(path) || !SameWindowsIdentity(WindowsIdentity(path), WindowsIdentity(handle)))
            throw new IOException("Unsupported executable path.");
        return path;
    }

    private static bool IsWithinWindowsRepository(string executable, WindowsFileIdentity root)
    {
        // Compare actual ancestor identities, including local/UNC/mapped-drive aliases of the same directory.
        for (string? path = executable; path is not null; path = Path.GetDirectoryName(path))
            if (SameWindowsIdentity(WindowsIdentity(path), root)) return true;
        return false;
    }

    private static WindowsFileIdentity WindowsIdentity(string path)
    {
        if (path.Contains('\0')) throw new ArgumentException("Unsupported path.");
        using var handle = OpenWindowsPath(path);
        return WindowsIdentity(handle);
    }

    private static WindowsFileIdentity WindowsIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18, out var identity, 24))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return identity;
    }

    private static bool SameWindowsIdentity(WindowsFileIdentity first, WindowsFileIdentity second) =>
        first.VolumeSerialNumber == second.VolumeSerialNumber && first.FileIdLow == second.FileIdLow &&
        first.FileIdHigh == second.FileIdHigh;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileIdentity
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    private static SafeFileHandle OpenWindowsPath(string path)
    {
        var handle = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static string FinalWindowsPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, flags);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, flags);
        }
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return buffer.ToString();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out WindowsFileIdentity information, uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(IntPtr pointer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
